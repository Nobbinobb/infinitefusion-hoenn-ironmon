# Exercise the real scheduler with isolated GitHub/input doubles. No network or
# repository mutation is possible; unexpected GitHub operations fail the test.
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('ironmon-watch-contracts-' + [guid]::NewGuid().ToString('N'))
$savedEnvironment = @{}
foreach ($name in 'GITHUB_REPOSITORY', 'RUNNER_TEMP', 'GITHUB_STEP_SUMMARY') {
  $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}
New-Item -ItemType Directory -Path "$testRoot/tools" -Force | Out-Null
function gh {
  $global:LASTEXITCODE = 0
  $arguments = @($args)
  $state = $global:IronmonWatchTestState
  if ($arguments[0] -eq 'run' -and $arguments[1] -eq 'download') {
    $directory = $arguments[[Array]::IndexOf($arguments, '--dir') + 1]
    New-Item -ItemType Directory -Path $directory | Out-Null
    @{fingerprint=$state.oldFingerprint} | ConvertTo-Json | Set-Content (Join-Path $directory 'upstream-inputs.json')
    return
  }
  if ($arguments[0] -ne 'api') { throw 'Unexpected scheduler command.' }
  $endpoint = $arguments[-1]
  if ($endpoint -like '*/pulls?state=*') {
    return (ConvertTo-Json -InputObject @(@($state.pull)) -Depth 8 -Compress)
  }
  if ($endpoint -like '*/workflows/*/runs?*') {
    $workflow = if ($endpoint -like '*/tracker-ci.yml/*') { 'tracker' } else { 'candidate' }
    return (ConvertTo-Json -InputObject @{workflow_runs=@($state.runs[$workflow])} -Depth 8)
  }
  if ($endpoint -match '/runs/(\d+)/artifacts\?') {
    $id = [int]$Matches[1]
    $artifacts = @()
    if (-not ($state.missingArtifact -contains $id)) {
      $artifacts = @(@{name='upstream-inputs';expired=($state.expiredArtifact -contains $id)})
    }
    return (ConvertTo-Json -InputObject @{artifacts=$artifacts} -Depth 8)
  }
  if ($endpoint -like '*/pulls/1') {
    return (ConvertTo-Json -InputObject @{state='open';head=@{sha=$state.currentHead}} -Depth 6)
  }
  if ($endpoint -like '*/runs/102/jobs?*') {
    $jobs = if ($state.releaseCandidateBuilt) { @(@{name='candidate / build';conclusion='success'}) } else { @(@{name='candidate';conclusion='skipped'}) }
    return (ConvertTo-Json -InputObject @{jobs=$jobs} -Depth 6)
  }
  if ($endpoint -match '/runs/(\d+)/rerun$' -and $arguments[1] -eq '--method' -and $arguments[2] -eq 'POST') {
    $state.requested += [int]$Matches[1]
    return
  }
  throw "Unexpected GitHub operation in scheduler test: $($arguments -join ' ')"
}
function Invoke-WatchCase([string]$Name, [scriptblock]$Arrange, [int[]]$Expected, [bool]$ExpectFailure = $false) {
  $state = @{
    oldFingerprint='current'; currentHead=('a' * 40); requested=@(); missingArtifact=@(); expiredArtifact=@(); releaseCandidateBuilt=$false
    pull=@{number=1;head=@{sha=('a' * 40);repo=@{full_name='owner/repo'}}}
    runs=@{}
  }
  foreach ($entry in @{tracker=101;candidate=102}.GetEnumerator()) {
    $state.runs[$entry.Key] = @{
      id=$entry.Value; head_sha=('a' * 40); pull_requests=@(@{number=1})
      status='completed';conclusion='success';run_attempt=1;created_at=[DateTimeOffset]::UtcNow.ToString('o')
    }
  }
  $global:IronmonWatchTestState = $state
  & $Arrange $state
  $env:RUNNER_TEMP = Join-Path $testRoot $Name
  New-Item -ItemType Directory -Path $env:RUNNER_TEMP | Out-Null
  $env:GITHUB_STEP_SUMMARY = Join-Path $env:RUNNER_TEMP 'summary.md'
  $failed = $false
  try { & "$testRoot/tools/Refresh-UpstreamValidation.ps1" } catch { $failed = $true; if (-not $ExpectFailure) { throw } }
  if ($failed -ne $ExpectFailure -or (($state.requested | Sort-Object) -join ',') -ne (($Expected | Sort-Object) -join ',')) {
    throw "Scheduler contract failed: $Name."
  }
  if ($ExpectFailure -and (Get-Content $env:GITHUB_STEP_SUMMARY -Raw) -notmatch 'PR #1.*30-day rerun window') {
    throw 'Expired-run failure must identify the affected PR and recovery.'
  }
}
try {
  Copy-Item (Join-Path $PSScriptRoot 'Refresh-UpstreamValidation.ps1') -Destination "$testRoot/tools"
  @'
param([string]$OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
@{fingerprint='current'} | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'upstream-inputs.json')
'@ | Set-Content "$testRoot/tools/Get-UpstreamInputs.ps1"
  $env:GITHUB_REPOSITORY = 'owner/repo'
  Invoke-WatchCase 'unchanged' {} @()
  Invoke-WatchCase 'changed' { param($s) $s.oldFingerprint='old' } @(101,102)
  $savedCulture = [Threading.Thread]::CurrentThread.CurrentCulture
  try {
    [Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('de-DE')
    Invoke-WatchCase 'german-date-culture' { param($s) $s.oldFingerprint='old' } @(101,102)
    Invoke-WatchCase 'string-dates' {
      param($s) $s.oldFingerprint='old'
      foreach ($run in $s.runs.Values) { $run.created_at=[DateTimeOffset]::UtcNow.ToString('r',[Globalization.CultureInfo]::InvariantCulture) }
    } @(101,102)
  } finally { [Threading.Thread]::CurrentThread.CurrentCulture = $savedCulture }
  Invoke-WatchCase 'expired-artifact' { param($s) $s.expiredArtifact=@(101) } @(101)
  Invoke-WatchCase 'ordinary-pr' { param($s) $s.oldFingerprint='old'; $s.missingArtifact=@(102) } @(101)
  Invoke-WatchCase 'pruned-successful-runs' {
    param($s) $s.missingArtifact=@(101,102); $s.releaseCandidateBuilt=$true
    foreach ($run in $s.runs.Values) { $run.run_attempt=3 }
  } @(101,102)
  Invoke-WatchCase 'running' { param($s) $s.oldFingerprint='old'; $s.runs.tracker.status='in_progress' } @(102)
  Invoke-WatchCase 'head-moved' { param($s) $s.oldFingerprint='old'; $s.currentHead=('b' * 40) } @()
  Invoke-WatchCase 'fork' { param($s) $s.oldFingerprint='old'; $s.pull.head.repo.full_name='outside/fork' } @()
  Invoke-WatchCase 'rerun-window' {
    param($s) $s.oldFingerprint='old'
    foreach ($run in $s.runs.Values) { $run.created_at=[DateTimeOffset]::UtcNow.AddDays(-31).ToString('o') }
  } @() $true
  Invoke-WatchCase 'resolution-failure-limit' {
    param($s) $s.missingArtifact=@(101,102)
    foreach ($run in $s.runs.Values) { $run.conclusion='failure'; $run.run_attempt=3 }
  } @()
  Write-Output 'Upstream scheduler contracts passed: unchanged/changed inputs, expired artifacts, ordinary PRs, running checks, moved heads, forks, rerun window and resolution retry limit.'
} finally {
  Remove-Item Function:gh
  Remove-Variable IronmonWatchTestState -Scope Global -ErrorAction SilentlyContinue
  foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name,$savedEnvironment[$name]) }
  $resolved = [IO.Path]::GetFullPath($testRoot)
  $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
  if (-not $resolved.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^ironmon-watch-contracts-[0-9a-f]{32}$') { throw 'Unsafe scheduler test cleanup path.' }
  Remove-Item -LiteralPath $resolved -Recurse -Force
}
