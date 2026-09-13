<#
.SYNOPSIS
Restores authenticated historical inventories from the previous immutable stable release.
.DESCRIPTION
Runs in the read-only candidate job. It never signs metadata or modifies old
published assets. An existing signed release must restore completely; transient
download or authentication failures cannot silently discard adoption support.
#>
param([Parameter(Mandatory)][string]$Tool, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
. (Join-Path $PSScriptRoot 'UpdateArtifacts.ps1')
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$trust = Join-Path $projectRoot 'resources/updater/trusted-keys.json'
$releases = @(gh api 'repos/Nobbinobb/infinitefusion-hoenn-ironmon/releases?per_page=100' | ConvertFrom-Json)
$previous = $releases | Where-Object { -not $_.draft -and -not $_.prerelease -and $_.tag_name -cmatch '^v\d+\.\d+\.\d+$' } | Sort-Object { [version]$_.tag_name.Substring(1) } -Descending | Select-Object -First 1
if (-not $previous) { return }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'History output already exists; use a fresh build directory.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
function Get-HistoricalAsset([string]$Name) {
  if ($Name -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,150}$') { throw 'Unsafe historical asset name.' }
  $matchingAssets = @($previous.assets | Where-Object name -CEQ $Name)
  if ($matchingAssets.Count -ne 1 -or $matchingAssets[0].digest -cnotmatch '^sha256:[0-9a-f]{64}$') { throw 'Historical assets require independent GitHub digests.' }
  gh release download $previous.tag_name --repo Nobbinobb/infinitefusion-hoenn-ironmon --pattern $Name --dir $OutputDirectory
  $path = Join-Path $OutputDirectory $Name
  if ((Get-Item -LiteralPath $path).Length -ne $matchingAssets[0].size -or ('sha256:' + (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()) -cne $matchingAssets[0].digest) { throw 'Historical asset does not match GitHub evidence.' }
}
if (@($previous.assets | Where-Object name -CEQ 'update-manifest.json').Count -eq 0) {
  Get-HistoricalAsset 'candidate.json'
  $candidate = Get-Content -LiteralPath (Join-Path $OutputDirectory 'candidate.json') -Raw | ConvertFrom-Json
  $version = $previous.tag_name.Substring(1)
  $commit = $candidate.inputs.game_commit
  if ($candidate.schema_version -ne 1 -or $candidate.version -cne $version -or $candidate.repository -cne 'Nobbinobb/infinitefusion-hoenn-ironmon' -or $commit -cnotmatch '^[0-9a-f]{40}$') { throw 'Invalid pre-updater candidate provenance.' }
  foreach ($name in "Ironmon-v$version-win-x64.zip", "Ironmon-v$version-win-x64-runtime-required.zip") { Get-HistoricalAsset $name }
  $gameRoot = Split-Path -Parent $projectRoot
  git -C $gameRoot fetch --no-tags https://github.com/infinitefusion/infinitefusion-hoenn-public.git $commit
  $settings = git --no-replace-objects -C $gameRoot show "${commit}:Data/Scripts/001_Settings.rb" | Out-String
  if ($settings -notmatch '(?m)^\s*GAME_VERSION_NUMBER\s*=\s*["'']([0-9]+\.[0-9]+\.[0-9]+)["'']') { throw 'Historical game version is unavailable.' }
  $gameVersion = $Matches[1]
  & (Join-Path $projectRoot 'tools/generation/Generate-Game-Adoption-Inventory.ps1') -GameRoot $gameRoot -GameCommit $commit -OutputPath (Join-Path $OutputDirectory 'legacy-game.json.gz') -ManifestPath (Join-Path $OutputDirectory 'legacy-game.manifest.json')
  @{version=$version;gameVersion=$gameVersion;gameInventory='legacy-game.json.gz';gameManifest='legacy-game.manifest.json'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'legacy-import.json') -Encoding utf8NoBOM
  return
}
foreach ($name in 'update-manifest.json','update-manifest.sig.json') {
  Get-HistoricalAsset $name
}
$manifest = Get-Content -LiteralPath (Join-Path $OutputDirectory 'update-manifest.json') -Raw | ConvertFrom-Json
if ('v' + $manifest.ironmonVersion -cne $previous.tag_name) { throw 'Release history tag mismatch.' }
if (@($manifest.assets).Count -gt 256) { throw 'Too many historical assets.' }
if ($manifest.schemaVersion -eq 2) {
  Get-HistoricalAsset 'update-data.json'
} else {
  foreach ($asset in $manifest.assets | Where-Object role -CIn @('ironmon-files','legacy-ironmon-files','game-files')) {
    Get-HistoricalAsset $asset.name
  }
}
Invoke-UpdateCandidateTool $Tool @('verify', $OutputDirectory, $trust, 'history')
