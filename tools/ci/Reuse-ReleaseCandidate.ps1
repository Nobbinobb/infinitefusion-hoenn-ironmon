param([Parameter(Mandatory)][string]$SnapshotDirectory)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
. (Join-Path $PSScriptRoot 'Candidate.ps1')
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location -LiteralPath $projectRoot
$sourceCommit = git rev-parse HEAD
$tree = git rev-parse 'HEAD^{tree}'
[xml]$project = Get-Content tracker/src/Ironmon.Tracker.App/Ironmon.Tracker.App.csproj -Raw
$version = [string]$project.Project.PropertyGroup.ApplicationDisplayVersion
$snapshot = Get-Content (Join-Path $SnapshotDirectory 'upstream-inputs.json') -Raw | ConvertFrom-Json
$pulls = gh api "repos/$env:GITHUB_REPOSITORY/commits/$sourceCommit/pulls" | ConvertFrom-Json
$pull = $pulls | Where-Object { $_.merged_at -and $_.merge_commit_sha -eq $sourceCommit -and $_.base.ref -eq 'main' -and $_.head.repo.full_name -eq $env:GITHUB_REPOSITORY } | Select-Object -First 1
if (-not $pull) { Write-Output 'No matching same-repository release PR; building approved source.'; return }
$runs = gh run list --repo $env:GITHUB_REPOSITORY --workflow release-candidate.yml --commit $pull.head.sha --event pull_request --status success --limit 20 --json databaseId,headSha | ConvertFrom-Json
foreach ($run in $runs) {
  $directory = Join-Path $env:RUNNER_TEMP "candidate-$($run.databaseId)"
  try {
    gh run download $run.databaseId --repo $env:GITHUB_REPOSITORY --name release-candidate --dir $directory
    $manifest = Assert-ReleaseCandidate $directory $tree $snapshot.fingerprint $version
    if ($manifest.repository -cne $env:GITHUB_REPOSITORY -or $manifest.run_id -ne [string]$run.databaseId) { throw 'Candidate run identity mismatch.' }
    Copy-Item -LiteralPath $directory -Destination (Join-Path $projectRoot 'release') -Recurse
    'reused=true' >> $env:GITHUB_OUTPUT
    "Reusing exact verified candidate bytes from run $($run.databaseId)." >> $env:GITHUB_STEP_SUMMARY
    return
  } catch {
    Write-Output "Candidate $($run.databaseId) cannot be reused: $($_.Exception.Message)"
  }
}
Write-Output 'No retained candidate matches the approved tree and latest inputs; a full rebuild is required.'

