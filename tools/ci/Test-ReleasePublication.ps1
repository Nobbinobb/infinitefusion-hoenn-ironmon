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
$global:IronmonPublicationTestState.remoteBytes = @{}
$global:IronmonPublicationTestState.rejectMetadata = $false
$global:IronmonPublicationTestState.signings = 0
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
    $global:IronmonPublicationTestState.fakeRelease = @{tag_name='v1.2.3';target_commitish=$global:IronmonPublicationTestState.commit;draft=$true;assets=@()}
    return
  }
  if ($arguments[0] -eq 'release' -and $arguments[1] -eq 'view') {
    if ($arguments -contains 'assets') { return (@{assets=$global:IronmonPublicationTestState.fakeRelease.assets} | ConvertTo-Json -Depth 6) }
    return '456'
  }
  if ($arguments[0] -eq 'api' -and $arguments[1] -eq 'repos/owner/repo/releases/456') {
    return ($global:IronmonPublicationTestState.fakeRelease | ConvertTo-Json -Depth 6)
  }
  if ($arguments[0] -eq 'release' -and $arguments[1] -eq 'upload') {
    $path = $arguments[3]
    $global:IronmonPublicationTestState.fakeRelease.assets += @{name=[IO.Path]::GetFileName($path);size=(Get-Item $path).Length;digest=('sha256:' + (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant())}
    $global:IronmonPublicationTestState.uploads++
    $global:IronmonPublicationTestState.remoteBytes[[IO.Path]::GetFileName($path)] = [IO.File]::ReadAllBytes($path)
    return
  }
  if ($arguments[0] -eq 'release' -and $arguments[1] -eq 'download') {
    $name = $arguments[[Array]::IndexOf($arguments, '--pattern') + 1]
    $directory = $arguments[[Array]::IndexOf($arguments, '--dir') + 1]
    if ($name -cne 'update-manifest.sig.json') { throw 'Only the exact published signature can be restored.' }
    [IO.File]::WriteAllBytes((Join-Path $directory $name), $global:IronmonPublicationTestState.remoteBytes[$name])
    return
  }
  if ($arguments[0] -eq 'release' -and $arguments[1] -eq 'edit') {
    $global:IronmonPublicationTestState.fakeRelease.draft = $false
    $global:IronmonPublicationTestState.published++
    return
  }
  throw "Unexpected GitHub operation in offline test: $($arguments -join ' ')"
}
function dotnet {
  $global:LASTEXITCODE = 0
  if ($args[0] -cne 'fixture-tool') { throw 'Unexpected executable in publication contract.' }
  if ($global:IronmonPublicationTestState.rejectMetadata) { $global:LASTEXITCODE = 1; return }
  if ($args[1] -ceq 'sign') {
    $global:IronmonPublicationTestState.signings++
    $path = Join-Path $args[2] 'update-manifest.sig.json'
    if (-not (Test-Path -LiteralPath $path)) { 'Public synthetic signature fixture.' | Set-Content -LiteralPath $path -Encoding utf8NoBOM }
  } elseif ($args[1] -cne 'verify') { throw 'Unexpected metadata operation.' }
}
try {
  Copy-Item (Join-Path $PSScriptRoot 'Publish-Release.ps1'), (Join-Path $PSScriptRoot 'Candidate.ps1'), (Join-Path $PSScriptRoot 'UpdateArtifacts.ps1') -Destination "$testRoot/tools"
  # Replace only the network resolver in this isolated fixture with known input provenance.
  @'
param([string]$OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Copy-Item (Join-Path $env:GITHUB_WORKSPACE 'latest.json') -Destination (Join-Path $OutputDirectory 'upstream-inputs.json') -Force
'@ | Set-Content "$testRoot/tools/Get-UpstreamInputs.ps1"
  '<Project><PropertyGroup><ApplicationDisplayVersion>1.2.3</ApplicationDisplayVersion></PropertyGroup></Project>' | Set-Content "$testRoot/source/tracker/src/Ironmon.Tracker.App/Ironmon.Tracker.App.csproj"
  '# Ironmon 1.2.3' | Set-Content "$testRoot/source/docs/releases/RELEASE_NOTES_1.2.3.md"
  . (Join-Path $PSScriptRoot 'ReleaseTestFixture.ps1')
  $null = New-TestReleaseCandidate "$testRoot/candidate" '1.2.3' $global:IronmonPublicationTestState.commit $global:IronmonPublicationTestState.fingerprint 'owner/repo'
  $expectedAssets = 7
  $env:GITHUB_ACTIONS = 'true'
  $env:GITHUB_REF = 'refs/heads/main'
  $env:GITHUB_WORKSPACE = $testRoot
  $env:GITHUB_REPOSITORY = 'owner/repo'
  $env:GITHUB_STEP_SUMMARY = "$testRoot/summary.md"
  $env:RUNNER_TEMP = "$testRoot/temp"
  @{fingerprint=$global:IronmonPublicationTestState.fingerprint} | ConvertTo-Json | Set-Content "$testRoot/latest.json"
  $global:IronmonPublicationTestState.rejectMetadata = $true
  $rejected = $false
  try { & "$testRoot/tools/Publish-Release.ps1" -UpdateTool fixture-tool -SourceCommit $global:IronmonPublicationTestState.commit } catch { $rejected = $true }
  if (-not $rejected -or $global:IronmonPublicationTestState.published -ne 0 -or $global:IronmonPublicationTestState.uploads -ne 0 -or $global:IronmonPublicationTestState.signings -ne 0) { throw 'Rejected metadata must not be signed, uploaded or published.' }
  $global:IronmonPublicationTestState.rejectMetadata = $false
  & "$testRoot/tools/Publish-Release.ps1" -UpdateTool fixture-tool -SourceCommit $global:IronmonPublicationTestState.commit
  if ($global:IronmonPublicationTestState.published -ne 1 -or $global:IronmonPublicationTestState.uploads -ne $expectedAssets -or $global:IronmonPublicationTestState.dispatched -ne 0) { throw 'Initial publication did not verify and publish all role-bound assets.' }
  $expectedNames = @('Ironmon-v1.2.3-win-x64.zip','Ironmon-v1.2.3-win-x64-runtime-required.zip','Ironmon-Setup-v1.2.3-win-x64.exe','update-manifest.json','update-manifest.sig.json','update-data.json','SHA256SUMS.txt')
  if ((@($global:IronmonPublicationTestState.fakeRelease.assets.name | Sort-Object) -join "`n") -cne (($expectedNames | Sort-Object) -join "`n")) { throw 'The public release must contain only the three player downloads and four metadata/checksum documents.' }
  $checksumLines = @(Get-Content -LiteralPath "$testRoot/candidate/SHA256SUMS.txt")
  if ($checksumLines.Count -ne 6) { throw 'One checksum list must cover every other published file exactly once.' }
  foreach ($asset in $global:IronmonPublicationTestState.fakeRelease.assets | Where-Object name -CNE 'SHA256SUMS.txt') {
    if ("$($asset.digest.Substring(7))  $($asset.name)" -cnotin $checksumLines) { throw 'The combined checksum list differs from a published asset.' }
  }
  Remove-Item -LiteralPath "$testRoot/candidate/update-manifest.sig.json", "$testRoot/candidate/SHA256SUMS.txt"
  & "$testRoot/tools/Publish-Release.ps1" -UpdateTool fixture-tool -SourceCommit $global:IronmonPublicationTestState.commit
  if ($global:IronmonPublicationTestState.published -ne 1 -or $global:IronmonPublicationTestState.uploads -ne $expectedAssets) { throw 'Retry must leave published assets immutable.' }
  $signingsBeforeRefresh = $global:IronmonPublicationTestState.signings
  $global:IronmonPublicationTestState.latestFingerprint = 'c' * 64
  @{fingerprint=$global:IronmonPublicationTestState.latestFingerprint} | ConvertTo-Json | Set-Content "$testRoot/latest.json"
  & "$testRoot/tools/Publish-Release.ps1" -UpdateTool fixture-tool -SourceCommit $global:IronmonPublicationTestState.commit
  if ($global:IronmonPublicationTestState.dispatched -ne 1 -or $global:IronmonPublicationTestState.published -ne 1) { throw 'Changed upstream must dispatch a rebuild without publication.' }
  if ($global:IronmonPublicationTestState.signings -ne $signingsBeforeRefresh) { throw 'Stale upstream candidates must not reach signing.' }
  $rejected = $false
  try { & "$testRoot/tools/Publish-Release.ps1" -UpdateTool fixture-tool -SourceCommit $global:IronmonPublicationTestState.commit -RefreshAttempt 3 } catch { $rejected = $true }
  if (-not $rejected -or $global:IronmonPublicationTestState.dispatched -ne 1) { throw 'Refresh retries must be bounded.' }
  $global:IronmonPublicationTestState.latestFingerprint = $global:IronmonPublicationTestState.fingerprint
  @{fingerprint=$global:IronmonPublicationTestState.latestFingerprint} | ConvertTo-Json | Set-Content "$testRoot/latest.json"
  $global:IronmonPublicationTestState.fakeRelease.assets[0].digest = 'sha256:' + ('d' * 64)
  $rejected = $false
  try { & "$testRoot/tools/Publish-Release.ps1" -UpdateTool fixture-tool -SourceCommit $global:IronmonPublicationTestState.commit } catch { $rejected = $true }
  if (-not $rejected -or $global:IronmonPublicationTestState.published -ne 1) { throw 'Mismatched remote assets must never be overwritten.' }
  $global:IronmonPublicationTestState.fakeRelease.assets = @($global:IronmonPublicationTestState.fakeRelease.assets | Select-Object -Last 2)
  $global:IronmonPublicationTestState.fakeRelease.draft = $true
  $global:IronmonPublicationTestState.uploads = 0
  $global:IronmonPublicationTestState.published = 0
  & "$testRoot/tools/Publish-Release.ps1" -UpdateTool fixture-tool -SourceCommit $global:IronmonPublicationTestState.commit
  if ($global:IronmonPublicationTestState.uploads -ne ($expectedAssets - 2) -or $global:IronmonPublicationTestState.published -ne 1) { throw 'A partial draft must resume through its release ID without replacing existing verified assets.' }
  $global:IronmonPublicationTestState.fakeRelease.tag_name = 'v9.9.9'
  $rejected = $false
  try { & "$testRoot/tools/Publish-Release.ps1" -UpdateTool fixture-tool -SourceCommit $global:IronmonPublicationTestState.commit } catch { $rejected = $true }
  if (-not $rejected -or $global:IronmonPublicationTestState.published -ne 1) { throw 'A resolved release with a different tag must be rejected.' }
  Write-Output 'Publication contracts passed: rejected metadata, publish, exact signature retry, unsigned upstream rebuild, retry limit, remote tampering, partial-draft recovery and release-ID identity.'
} finally {
  Remove-Item Function:git, Function:gh, Function:dotnet
  Remove-Variable IronmonPublicationTestState -Scope Global
  foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
  $resolved = [IO.Path]::GetFullPath($testRoot)
  $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
  if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^ironmon-publication-contracts-[0-9a-f]{32}$') { throw 'Unsafe publication test cleanup path.' }
  Remove-Item -LiteralPath $resolved -Recurse -Force
}
