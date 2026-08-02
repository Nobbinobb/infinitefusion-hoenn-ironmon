$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $PSScriptRoot "Build-Distribution.ps1"
$distribution = Join-Path $projectRoot "dist"
$releaseDirectory = Join-Path $projectRoot "release"
$archive = Join-Path $releaseDirectory "Ironmon-Milestone-1-v0.1.0.zip"
$checksum = Join-Path $releaseDirectory "Ironmon-Milestone-1-v0.1.0.sha256.txt"

& $buildScript
New-Item -ItemType Directory -Force -Path $releaseDirectory | Out-Null
if (Test-Path -LiteralPath $archive) {
  Remove-Item -LiteralPath $archive -Force
}
if (Test-Path -LiteralPath $checksum) {
  Remove-Item -LiteralPath $checksum -Force
}
Compress-Archive -Path (Join-Path $distribution "*") -DestinationPath $archive
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksum -Value "$hash  Ironmon-Milestone-1-v0.1.0.zip"

Write-Output "Created $archive"
Write-Output "SHA256 $hash"
