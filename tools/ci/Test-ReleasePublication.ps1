# Exercise publication orchestration against an in-memory GitHub double. No network,
# release, tag or repository mutation is possible in this test.
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("ironmon-publication-contracts-" + [guid]::NewGuid().ToString('N'))
$savedEnvironment = @{}
foreach ($name in 'GITHUB_ACTIONS', 'GITHUB_REF', 'GITHUB_WORKSPACE', 'GITHUB_REPOSITORY', 'GITHUB_STEP_SUMMARY', 'RUNNER_TEMP') {
  $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}
New-Item -ItemType Directory -Path "$testRoot/tools", "$testRoot/candidate", "$testRoot/source/tracker/src/Ironmon.Tracker.App", "$testRoot/source/docs/releases", "$testRoot/temp" -Force | Out-Null
$global:IronmonPublicationTestState = @{}
$global:IronmonPublicationTestState.fakeRelease = $null
$global:IronmonPublicationTestState.dispatched = 0
$global:IronmonPublicationTestState.published = 0
$global:IronmonPublicationTestState.uploads = 0
$global:IronmonPublicationTestState.commit = 'a' * 40
$global:IronmonPublicationTestState.fingerprint = 'b' * 64
$global:IronmonPublicationTestState.latestFingerprint = $global:IronmonPublicationTestState.fingerprint
function git {
  $global:LASTEXITCODE = 0
  return ('a' * 40)
}
function gh {
  $global:LASTEXITCODE = 0
  $arguments = @($args)
  if ($arguments[0] -eq 'workflow' -and $arguments[1] -eq 'run') { $global:IronmonPublicationTestState.dispatched++; return }
  if ($arguments[0] -eq 'release' -and $arguments[1] -eq 'list') {
    if ($global:IronmonPublicationTestState.fakeRelease) { return (@(@{tagName='v1.2.3';isDraft=$global:IronmonPublicationTestState.fakeRelease.draft}) | ConvertTo-Json -AsArray) }
    return '[]'
  }
  if ($arguments[0] -eq 'api' -and $arguments[1] -like '*/git/matching-refs/*') { return '[]' }
  if ($arguments[0] -eq 'release' -and $arguments[1] -eq 'create') {
    $global:IronmonPublicationTestState.fakeRelease = @{target_commitish=$global:IronmonPublicationTestState.commit;draft=$true;assets=@()}
    return
  }
  if ($arguments[0] -eq 'api' -and $arguments[1] -like '*/releases/tags/*') {
    return ($global:IronmonPublicationTestState.fakeRelease | ConvertTo-Json -Depth 6)
  }
  if ($arguments[0] -eq 'release' -and $arguments[1] -eq 'upload') {
    $path = $arguments[3]
    $global:IronmonPublicationTestState.fakeRelease.assets += @{name=[IO.Path]::GetFileName($path);size=(Get-Item $path).Length;digest=('sha256:' + (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant())}
    $global:IronmonPublicationTestState.uploads++
    return
  }
  if ($arguments[0] -eq 'release' -and $arguments[1] -eq 'edit') {
    $global:IronmonPublicationTestState.fakeRelease.draft = $false
    $global:IronmonPublicationTestState.published++
    return
  }
  throw "Unexpected GitHub operation in offline test: $($arguments -join ' ')"
}
try {
  Copy-Item (Join-Path $PSScriptRoot 'Publish-Release.ps1'), (Join-Path $PSScriptRoot 'Candidate.ps1') -Destination "$testRoot/tools"
  # Replace only the network resolver in this isolated fixture with known input provenance.
  @'
param([string]$OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Copy-Item (Join-Path $env:GITHUB_WORKSPACE 'latest.json') -Destination (Join-Path $OutputDirectory 'upstream-inputs.json') -Force
'@ | Set-Content "$testRoot/tools/Get-UpstreamInputs.ps1"
  '<Project><PropertyGroup><ApplicationDisplayVersion>1.2.3</ApplicationDisplayVersion></PropertyGroup></Project>' | Set-Content "$testRoot/source/tracker/src/Ironmon.Tracker.App/Ironmon.Tracker.App.csproj"
  '# Ironmon 1.2.3' | Set-Content "$testRoot/source/docs/releases/RELEASE_NOTES_1.2.3.md"
  $assets = @('Ironmon-v1.2.3-win-x64.zip', 'Ironmon-v1.2.3-win-x64-runtime-required.zip', 'release-evidence.zip') | ForEach-Object {
    $path = Join-Path "$testRoot/candidate" $_
    'verified bytes' | Set-Content $path
    $hash = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $_" | Set-Content ($path -replace '\.zip$', '.sha256.txt')
    @{name=$_;sha256=$hash;bytes=(Get-Item $path).Length}
  }
  @{schema_version=1;version='1.2.3';source_commit=$global:IronmonPublicationTestState.commit;source_tree=$global:IronmonPublicationTestState.commit;run_id='123';repository='owner/repo';inputs=@{fingerprint=$global:IronmonPublicationTestState.fingerprint;game_commit=$global:IronmonPublicationTestState.commit};assets=@($assets)} | ConvertTo-Json -Depth 8 | Set-Content "$testRoot/candidate/candidate.json"
  $env:GITHUB_ACTIONS = 'true'
  $env:GITHUB_REF = 'refs/heads/main'
  $env:GITHUB_WORKSPACE = $testRoot
  $env:GITHUB_REPOSITORY = 'owner/repo'
  $env:GITHUB_STEP_SUMMARY = "$testRoot/summary.md"
  $env:RUNNER_TEMP = "$testRoot/temp"
  @{fingerprint=$global:IronmonPublicationTestState.fingerprint} | ConvertTo-Json | Set-Content "$testRoot/latest.json"
  & "$testRoot/tools/Publish-Release.ps1" -SourceCommit $global:IronmonPublicationTestState.commit
  if ($global:IronmonPublicationTestState.published -ne 1 -or $global:IronmonPublicationTestState.uploads -ne 7 -or $global:IronmonPublicationTestState.dispatched -ne 0) { throw 'Initial publication did not verify and publish all seven assets.' }
  & "$testRoot/tools/Publish-Release.ps1" -SourceCommit $global:IronmonPublicationTestState.commit
  if ($global:IronmonPublicationTestState.published -ne 1 -or $global:IronmonPublicationTestState.uploads -ne 7) { throw 'Retry must leave published assets immutable.' }
  $global:IronmonPublicationTestState.latestFingerprint = 'c' * 64
  @{fingerprint=$global:IronmonPublicationTestState.latestFingerprint} | ConvertTo-Json | Set-Content "$testRoot/latest.json"
  & "$testRoot/tools/Publish-Release.ps1" -SourceCommit $global:IronmonPublicationTestState.commit
  if ($global:IronmonPublicationTestState.dispatched -ne 1 -or $global:IronmonPublicationTestState.published -ne 1) { throw 'Changed upstream must dispatch a rebuild without publication.' }
  $rejected = $false
  try { & "$testRoot/tools/Publish-Release.ps1" -SourceCommit $global:IronmonPublicationTestState.commit -RefreshAttempt 3 } catch { $rejected = $true }
  if (-not $rejected -or $global:IronmonPublicationTestState.dispatched -ne 1) { throw 'Refresh retries must be bounded.' }
  $global:IronmonPublicationTestState.latestFingerprint = $global:IronmonPublicationTestState.fingerprint
  @{fingerprint=$global:IronmonPublicationTestState.latestFingerprint} | ConvertTo-Json | Set-Content "$testRoot/latest.json"
  $global:IronmonPublicationTestState.fakeRelease.assets[0].digest = 'sha256:' + ('d' * 64)
  $rejected = $false
  try { & "$testRoot/tools/Publish-Release.ps1" -SourceCommit $global:IronmonPublicationTestState.commit } catch { $rejected = $true }
  if (-not $rejected -or $global:IronmonPublicationTestState.published -ne 1) { throw 'Mismatched remote assets must never be overwritten.' }
  Write-Output 'Publication contracts passed: publish, immutable retry, upstream rebuild, retry limit and remote tampering.'
} finally {
  Remove-Item Function:git, Function:gh
  Remove-Variable IronmonPublicationTestState -Scope Global
  foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
  $resolved = [IO.Path]::GetFullPath($testRoot)
  $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
  if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^ironmon-publication-contracts-[0-9a-f]{32}$') { throw 'Unsafe publication test cleanup path.' }
  Remove-Item -LiteralPath $resolved -Recurse -Force
}
