param(
  [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
. (Join-Path $PSScriptRoot 'UpstreamInputs.ps1')
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Input snapshot directory must be new.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null

$gameCommit = Get-UpstreamRevision 'infinitefusion/infinitefusion-hoenn-public' 'releases'
$metadataCommit = Get-UpstreamRevision 'infinitefusion/pif-downloadables' 'master'
$gameSettings = Join-Path $output 'game-settings.rb'
Save-UpstreamFile "https://raw.githubusercontent.com/infinitefusion/infinitefusion-hoenn-public/$gameCommit/Data/Scripts/001_Settings.rb" $gameSettings
$gameText = Get-Content -LiteralPath $gameSettings -Raw
$settingsUrl = Get-RubyStringConstant $gameText 'HTTP_CONFIGS_FILE_URL'
if ((Get-RubyStringConstant $gameText 'HTTP_CONFIGS_FILE_PATH') -ne 'Data/Scripts/DownloadedSettings.rb') {
  throw 'Upstream changed the downloaded-settings destination; update the installer before building.'
}
$settingsUrl = ConvertTo-FrozenMetadataUrl $settingsUrl $metadataCommit
$files = @()
$specifications = @(
  @{ name = 'settings'; path = 'Data/Scripts/DownloadedSettings.rb'; url = $settingsUrl; file = 'Settings.rb' }
)
$settingsFile = Join-Path $output 'Settings.rb'
Save-UpstreamFile $settingsUrl $settingsFile
$settingsText = Get-Content -LiteralPath $settingsFile -Raw
foreach ($spec in @(
  @{ name = 'custom_sprites'; constant = 'SPRITES_FILE_URL'; destination = 'CUSTOM_SPRITES_FILE_PATH'; path = 'Data/sprites/CUSTOM_SPRITES'; file = 'CUSTOM_SPRITES' },
  @{ name = 'base_sprites'; constant = 'BASE_SPRITES_FILE_URL'; destination = 'BASE_SPRITES_FILE_PATH'; path = 'Data/sprites/BASE_SPRITES'; file = 'BASE_SPRITES' },
  @{ name = 'credits'; constant = 'CREDITS_FILE_URL'; destination = 'CREDITS_FILE_PATH'; path = 'Data/sprites/Sprite_Credits.csv'; file = 'Sprite_Credits.csv' }
)) {
  if ((Get-RubyStringConstant $gameText $spec.destination) -ne $spec.path) {
    throw "Upstream changed the $($spec.name) destination; update the installer before building."
  }
  $url = ConvertTo-FrozenMetadataUrl (Get-RubyStringConstant $settingsText $spec.constant) $metadataCommit
  Save-UpstreamFile $url (Join-Path $output $spec.file)
  $specifications += @{ name = $spec.name; path = $spec.path; url = $url; file = $spec.file }
}
foreach ($spec in $specifications) {
  $path = Join-Path $output $spec.file
  Assert-UpstreamContent $spec.name $path
  $files += [ordered]@{
    name = $spec.name; path = $spec.path; file = $spec.file; url = $spec.url
    sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    bytes = (Get-Item -LiteralPath $path).Length
  }
}
$manifest = [ordered]@{
  schema_version = 1
  game_repository = 'infinitefusion/infinitefusion-hoenn-public'; game_branch = 'releases'
  game_commit = $gameCommit; metadata_commit = $metadataCommit
  resolved_at = [DateTime]::UtcNow.ToString('o'); files = $files
  fingerprint = Get-UpstreamFingerprint $gameCommit $files
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'upstream-inputs.json') -Encoding utf8NoBOM
Write-Output "Resolved upstream inputs: $($manifest.fingerprint) (game $gameCommit)."
