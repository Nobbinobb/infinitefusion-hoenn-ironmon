<#
.SYNOPSIS
Generates updater metadata from exact completed archives and the selected game inventory.
#>
param([Parameter(Mandatory)][string]$Version, [Parameter(Mandatory)][string]$GameCommit, [string]$HistoryDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $PSScriptRoot 'UpdateArtifacts.ps1')
$settings = git --no-replace-objects -C (Split-Path -Parent $projectRoot) show "${GameCommit}:Data/Scripts/001_Settings.rb" | Out-String
if ($LASTEXITCODE -ne 0) { throw 'Cannot read the selected game version.' }
$match = [regex]::Match($settings, '(?m)^\s*GAME_VERSION_NUMBER\s*=\s*["''](?<version>\d+\.\d+\.\d+)["'']')
if (-not $match.Success) { throw 'The selected game has no supported version label.' }
$release = Join-Path $projectRoot 'release'
[IO.File]::Copy((Join-Path $projectRoot "docs/releases/RELEASE_NOTES_$Version.md"), (Join-Path $release 'update-notes.md'), $false)
$request = [ordered]@{
  directory=$release; version=$Version; gameVersion=$match.Groups['version'].Value; gameCommit=$GameCommit
  gameInventory=(Join-Path $projectRoot 'data/updater/baselines/hoenn.json.gz')
  gameManifest=(Join-Path $projectRoot 'data/updater/baselines/hoenn.manifest.json')
  trustFile=(Join-Path $projectRoot 'resources/updater/trusted-keys.json')
  historyDirectory=$(if ($HistoryDirectory) { $HistoryDirectory } else { $null })
}
$path = Join-Path $projectRoot 'data/updater/release-request.json'
$request | ConvertTo-Json -Depth 8 -Compress | Set-Content -LiteralPath $path -Encoding utf8NoBOM
$tool = Join-Path $projectRoot 'tracker/tools/Ironmon.ReleaseTool/bin/Release/net10.0/Ironmon.ReleaseTool.dll'
Invoke-UpdateCandidateTool $tool @('create', $path)
foreach ($asset in Get-ChildItem -LiteralPath $release -File | Where-Object { -not $_.Name.EndsWith('.sha256.txt') }) {
  $hash = (Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  $sidecar = Join-Path $release (Get-ReleaseChecksumName $asset.Name)
  $expected = "$hash  $($asset.Name)"
  if (Test-Path -LiteralPath $sidecar) {
    if ((Get-Content -LiteralPath $sidecar -Raw).Trim() -cne $expected) { throw 'Existing artifact checksum differs.' }
  } else { $expected | Set-Content -LiteralPath $sidecar -Encoding utf8NoBOM }
}
