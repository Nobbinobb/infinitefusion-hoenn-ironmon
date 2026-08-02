$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$gameRoot = Split-Path -Parent $projectRoot
$source = Join-Path $projectRoot "src"
$distribution = Join-Path $projectRoot "dist\Data\Scripts\997_Ironmon"
$distributionRoot = Join-Path $projectRoot "dist"
$installationGuide = Join-Path $projectRoot "docs\INSTALLATION.md"
$installation = Join-Path $gameRoot "Data\Scripts\997_Ironmon"

New-Item -ItemType Directory -Force -Path $distribution | Out-Null
New-Item -ItemType Directory -Force -Path $installation | Out-Null

Get-ChildItem -LiteralPath $distribution -Filter "*.rb" | Remove-Item -Force
Get-ChildItem -LiteralPath $installation -Filter "*.rb" | Remove-Item -Force
Copy-Item -Path (Join-Path $source "*.rb") -Destination $distribution
Copy-Item -Path (Join-Path $source "*.rb") -Destination $installation
Copy-Item -LiteralPath $installationGuide -Destination (Join-Path $distributionRoot "INSTALLATION.md")

Write-Output "Ironmon source copied to the distribution and local game."
