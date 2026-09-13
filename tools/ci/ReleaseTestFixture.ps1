function New-TestReleaseCandidate([string]$Directory, [string]$Version, [string]$Commit, [string]$Fingerprint, [string]$Repository) {
  <#
  .SYNOPSIS
  Creates public synthetic bytes for offline orchestration contracts; C# tests cover actual archives and signatures.
  #>
  . (Join-Path $PSScriptRoot 'UpdateArtifacts.ps1')
  $specifications = [ordered]@{
    "Ironmon-v$Version-win-x64.zip"='tracker-self-contained'
    "Ironmon-v$Version-win-x64-runtime-required.zip"='tracker-runtime-required'
    "Ironmon-Setup-v$Version-win-x64.exe"='setup'
    'update-notes.md'='release-notes'
    'self-contained-files.json'='ironmon-files'
    'runtime-required-files.json'='ironmon-files'
    'game-files.json'='game-files'
  }
  $assets = @($specifications.Keys | ForEach-Object {
    $path = Join-Path $Directory $_
    'Synthetic public test content.' | Set-Content -LiteralPath $path -Encoding utf8NoBOM
    @{name=$_;role=$specifications[$_];url="https://github.com/Nobbinobb/infinitefusion-hoenn-ironmon/releases/download/v$Version/$_";bytes=(Get-Item $path).Length;sha256=(Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()}
  })
  $metadataPath = Join-Path $Directory 'update-data.json'
  'Public metadata container fixture.' | Set-Content -LiteralPath $metadataPath -Encoding utf8NoBOM
  $container = @{name='update-data.json';bytes=(Get-Item $metadataPath).Length;sha256=(Get-FileHash $metadataPath -Algorithm SHA256).Hash.ToLowerInvariant()}
  foreach ($asset in $assets | Where-Object role -CIn @('release-notes','ironmon-files','game-files')) {
    $asset.container = $container
    $asset.url = "https://github.com/Nobbinobb/infinitefusion-hoenn-ironmon/releases/download/v$Version/update-data.json"
  }
  @{documentType='release';schemaVersion=2;repository='Nobbinobb/infinitefusion-hoenn-ironmon';channel='stable';ironmonVersion=$Version;trackerVersion=$Version;releaseSequence=1;game=@{preferredCommit=$Commit};helper=@{version='1.0.1';bytes=1;sha256=('0' * 64)};assets=$assets} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Directory 'update-manifest.json') -Encoding utf8NoBOM
  foreach ($name in 'update-trusted-keys.json','release-evidence.zip') { 'Public fixture.' | Set-Content -LiteralPath (Join-Path $Directory $name) -Encoding utf8NoBOM }
  $roles = Get-UpdateArtifactRoles $Directory $Version
  $candidateAssets = @($roles.Keys | ForEach-Object {
    $path = Join-Path $Directory $_
    $hash = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $_" | Set-Content -LiteralPath (Join-Path $Directory (Get-ReleaseChecksumName $_)) -Encoding utf8NoBOM
    @{name=$_;role=$roles[$_];sha256=$hash;bytes=(Get-Item $path).Length}
  })
  $candidate = @{schema_version=2;version=$Version;source_commit=$Commit;source_tree=$Commit;run_id='123';repository=$Repository;inputs=@{fingerprint=$Fingerprint;game_commit=$Commit};assets=$candidateAssets}
  $candidate | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Directory 'candidate.json') -Encoding utf8NoBOM
  return $candidate
}
