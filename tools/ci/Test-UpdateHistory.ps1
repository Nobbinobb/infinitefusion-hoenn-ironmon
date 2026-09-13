<#
.SYNOPSIS
Exercises automatic legacy-history restoration with public offline fixtures and no game or network access.
#>
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('ironmon-history-contracts-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path "$testRoot/project/tools/ci", "$testRoot/project/tools/generation", "$testRoot/source" -Force | Out-Null
$global:IronmonHistoryTest = @{tamper=$false;generated=$null;commit=('a' * 40);assets=@();source="$testRoot/source";downloads=@();verified=0}
function gh {
  $global:LASTEXITCODE = 0
  if ($args[0] -ceq 'api') {
    return (@(@{tag_name='v1.2.2';draft=$false;prerelease=$false;assets=$global:IronmonHistoryTest.assets}) | ConvertTo-Json -Depth 8 -AsArray)
  }
  if ($args[0] -ceq 'release' -and $args[1] -ceq 'download') {
    $name = $args[[Array]::IndexOf($args, '--pattern') + 1]
    $global:IronmonHistoryTest.downloads += $name
    $directory = $args[[Array]::IndexOf($args, '--dir') + 1]
    $path = Join-Path $directory $name
    Copy-Item -LiteralPath (Join-Path $global:IronmonHistoryTest.source $name) -Destination $path
    if ($global:IronmonHistoryTest.tamper) { 'Corrupt downloaded bytes.' | Add-Content -LiteralPath $path }
    return
  }
  throw 'Unexpected history network operation.'
}
function git {
  $global:LASTEXITCODE = 0
  if ($args -contains 'show') { return 'GAME_VERSION_NUMBER = "6.9.0"' }
  if ($args -notcontains 'fetch' -or $args -notcontains $global:IronmonHistoryTest.commit) { throw 'History did not fetch the exact selected historical commit.' }
}
function dotnet {
  $global:LASTEXITCODE = 0
  if ($args[0] -cne 'fixture-tool' -or $args[1] -cne 'verify' -or $args[4] -cne 'history') { throw 'Unexpected history verification operation.' }
  foreach ($name in 'update-manifest.json','update-manifest.sig.json','update-data.json') {
    if (-not (Test-Path -LiteralPath (Join-Path $args[2] $name))) { throw 'Compact history is incomplete before verification.' }
  }
  $global:IronmonHistoryTest.verified++
}
try {
  Copy-Item (Join-Path $PSScriptRoot 'Restore-UpdateHistory.ps1'), (Join-Path $PSScriptRoot 'UpdateArtifacts.ps1') -Destination "$testRoot/project/tools/ci"
  @'
param([string]$GameRoot, [string]$GameCommit, [string]$OutputPath, [string]$ManifestPath)
if ($GameCommit -cne $global:IronmonHistoryTest.commit) { throw 'Historical generation selected a different game.' }
$global:IronmonHistoryTest.generated = $GameCommit
'Fixture generation boundary.' | Set-Content -LiteralPath $OutputPath
'Fixture companion boundary.' | Set-Content -LiteralPath $ManifestPath
'@ | Set-Content "$testRoot/project/tools/generation/Generate-Game-Adoption-Inventory.ps1"
  @{schema_version=1;version='1.2.2';repository='Nobbinobb/infinitefusion-hoenn-ironmon';inputs=@{game_commit=$global:IronmonHistoryTest.commit}} | ConvertTo-Json -Depth 4 | Set-Content "$testRoot/source/candidate.json" -Encoding utf8NoBOM
  foreach ($name in 'Ironmon-v1.2.2-win-x64.zip','Ironmon-v1.2.2-win-x64-runtime-required.zip') { 'Public package fixture.' | Set-Content -LiteralPath (Join-Path "$testRoot/source" $name) -Encoding utf8NoBOM }
  $global:IronmonHistoryTest.assets = @(Get-ChildItem "$testRoot/source" -File | ForEach-Object { @{name=$_.Name;size=$_.Length;digest=('sha256:' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())} })
  & "$testRoot/project/tools/ci/Restore-UpdateHistory.ps1" -Tool unused-fixture-tool -OutputDirectory "$testRoot/history"
  $request = Get-Content "$testRoot/history/legacy-import.json" -Raw | ConvertFrom-Json
  if ($request.version -cne '1.2.2' -or $request.gameVersion -cne '6.9.0' -or $global:IronmonHistoryTest.generated -cne $global:IronmonHistoryTest.commit) { throw 'Legacy restoration did not generate the exact previous release baseline automatically.' }
  $global:IronmonHistoryTest.tamper = $true
  $global:IronmonHistoryTest.generated = $null
  $rejected = $false
  try { & "$testRoot/project/tools/ci/Restore-UpdateHistory.ps1" -Tool unused-fixture-tool -OutputDirectory "$testRoot/corrupt" } catch { $rejected = $true }
  if (-not $rejected -or $global:IronmonHistoryTest.generated) { throw 'Tampered historical downloads must fail before baseline generation.' }
  $global:IronmonHistoryTest.tamper = $false
  $global:IronmonHistoryTest.downloads = @()
  @{schemaVersion=2;ironmonVersion='1.2.2';assets=@()} | ConvertTo-Json | Set-Content "$testRoot/source/update-manifest.json" -Encoding utf8NoBOM
  foreach ($name in 'update-manifest.sig.json','update-data.json') { 'Public compact-history fixture.' | Set-Content -LiteralPath (Join-Path "$testRoot/source" $name) -Encoding utf8NoBOM }
  $global:IronmonHistoryTest.assets = @('update-manifest.json','update-manifest.sig.json','update-data.json' | ForEach-Object {
    $file = Get-Item -LiteralPath (Join-Path "$testRoot/source" $_)
    @{name=$file.Name;size=$file.Length;digest=('sha256:' + (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant())}
  })
  & "$testRoot/project/tools/ci/Restore-UpdateHistory.ps1" -Tool fixture-tool -OutputDirectory "$testRoot/compact"
  if ($global:IronmonHistoryTest.verified -ne 1 -or $global:IronmonHistoryTest.generated -or ($global:IronmonHistoryTest.downloads -join ',') -cne 'update-manifest.json,update-manifest.sig.json,update-data.json') { throw 'Compact history must restore and verify only its three published metadata documents.' }
  Write-Output 'History contracts passed: automatic exact-commit legacy generation, independent GitHub digests, and compact three-document restoration.'
}
finally {
  Remove-Item Function:git, Function:gh, Function:dotnet
  Remove-Variable IronmonHistoryTest -Scope Global
  $resolved = [IO.Path]::GetFullPath($testRoot)
  $parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
  if (-not $resolved.StartsWith($parent, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -cnotmatch '^ironmon-history-contracts-[0-9a-f]{32}$') { throw 'Unsafe history fixture cleanup path.' }
  Remove-Item -LiteralPath $resolved -Recurse -Force
}
