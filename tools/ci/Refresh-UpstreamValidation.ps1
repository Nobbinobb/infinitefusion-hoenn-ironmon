$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$snapshotDirectory = Join-Path $env:RUNNER_TEMP 'watch-upstream-inputs'
& (Join-Path $PSScriptRoot 'Get-UpstreamInputs.ps1') -OutputDirectory $snapshotDirectory
$snapshot = Get-Content (Join-Path $snapshotDirectory 'upstream-inputs.json') -Raw | ConvertFrom-Json
$pages = gh api --paginate --slurp "repos/$env:GITHUB_REPOSITORY/pulls?state=open&base=main&per_page=100" | ConvertFrom-Json
$pulls = @($pages | ForEach-Object { $_ } | Where-Object { $_.head.repo.full_name -eq $env:GITHUB_REPOSITORY })
$requested = 0
$expiredRuns = @()
foreach ($pull in $pulls) {
  foreach ($workflow in 'tracker-ci.yml', 'release-candidate.yml') {
    $runs = gh api "repos/$env:GITHUB_REPOSITORY/actions/workflows/$workflow/runs?event=pull_request&head_sha=$($pull.head.sha)&per_page=100" | ConvertFrom-Json
    $run = $runs.workflow_runs | Where-Object { $_.head_sha -eq $pull.head.sha -and $_.pull_requests.number -contains $pull.number } | Sort-Object id -Descending | Select-Object -First 1
    if (-not $run -or $run.status -ne 'completed') { continue }
    $artifacts = gh api "repos/$env:GITHUB_REPOSITORY/actions/runs/$($run.id)/artifacts?per_page=100" | ConvertFrom-Json
    $inputArtifact = $artifacts.artifacts | Where-Object name -eq 'upstream-inputs' | Select-Object -First 1
    $artifact = $inputArtifact | Where-Object { -not $_.expired }
    # A missing artifact can mean either an ordinary PR or a pruned release
    # candidate. Check the completed jobs before treating it as an ordinary PR.
    if ($workflow -eq 'release-candidate.yml' -and -not $inputArtifact -and $run.conclusion -eq 'success') {
      $jobs = gh api "repos/$env:GITHUB_REPOSITORY/actions/runs/$($run.id)/jobs?per_page=100" | ConvertFrom-Json
      if (-not ($jobs.jobs | Where-Object { $_.name -eq 'candidate / build' -and $_.conclusion -eq 'success' })) { continue }
    }
    $oldFingerprint = $null
    if ($artifact) {
      $directory = Join-Path $env:RUNNER_TEMP "watch-$($run.id)"
      gh run download $run.id --repo $env:GITHUB_REPOSITORY --name upstream-inputs --dir $directory
      $oldFingerprint = (Get-Content (Join-Path $directory 'upstream-inputs.json') -Raw | ConvertFrom-Json).fingerprint
    } elseif (-not $inputArtifact -and $run.conclusion -ne 'success' -and $run.run_attempt -ge 3) {
      Write-Output "PR #$($pull.number): input resolution/artifacts unavailable after repeated attempts; inspect the failed run."
      continue
    }
    if ($oldFingerprint -eq $snapshot.fingerprint) { continue }
    # PowerShell 7.5 can deserialize JSON dates to DateTime. Preserve that value;
    # converting it back through a culture-dependent string can swap month/day.
    $createdAt = if ($run.created_at -is [DateTime]) { [DateTimeOffset]$run.created_at } else {
      [DateTimeOffset]::Parse([string]$run.created_at, [Globalization.CultureInfo]::InvariantCulture)
    }
    if ($createdAt -lt [DateTimeOffset]::UtcNow.AddDays(-30)) {
      $expiredRuns += "PR #$($pull.number): $workflow is outside GitHub's 30-day rerun window; update the PR branch to start fresh validation."
      continue
    }
    # Recheck the PR head immediately before dispatching; no commits or PR code
    # are checked out or executed in this privileged scheduler.
    $current = gh api "repos/$env:GITHUB_REPOSITORY/pulls/$($pull.number)" | ConvertFrom-Json
    if ($current.state -ne 'open' -or $current.head.sha -ne $pull.head.sha) { continue }
    gh api --method POST "repos/$env:GITHUB_REPOSITORY/actions/runs/$($run.id)/rerun"
    Write-Output "Requested fresh $workflow for PR #$($pull.number); upstream inputs changed or expired."
    $requested++
  }
}
"Upstream input check completed; requested $requested validation runs for open same-repository PRs." >> $env:GITHUB_STEP_SUMMARY
if ($expiredRuns.Count) {
  $expiredRuns | ForEach-Object { $_ >> $env:GITHUB_STEP_SUMMARY }
  throw ($expiredRuns -join ' ')
}
