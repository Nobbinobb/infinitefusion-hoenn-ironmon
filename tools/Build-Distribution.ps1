$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$gameRoot = Split-Path -Parent $projectRoot
$source = Join-Path $projectRoot "src"
$catalog = Join-Path $projectRoot "data\area_catalog.dat"
$distribution = Join-Path $projectRoot "dist\Data\Scripts\997_Ironmon"
$distributionData = Join-Path $projectRoot "dist\Data\Ironmon"
$distributionRoot = Join-Path $projectRoot "dist"
$installationGuide = Join-Path $projectRoot "docs\guides\INSTALLATION.md"
$releaseNotes = Join-Path $projectRoot "docs\releases\RELEASE_NOTES_0.7.4.md"
$installation = Join-Path $gameRoot "Data\Scripts\997_Ironmon"
$installationData = Join-Path $gameRoot "Data\Ironmon"

New-Item -ItemType Directory -Force -Path $distribution | Out-Null
New-Item -ItemType Directory -Force -Path $distributionData | Out-Null
New-Item -ItemType Directory -Force -Path $installation | Out-Null
New-Item -ItemType Directory -Force -Path $installationData | Out-Null

Get-ChildItem -LiteralPath $distribution -Filter "*.rb" | Remove-Item -Force
Get-ChildItem -LiteralPath $installation -Filter "*.rb" | Remove-Item -Force
Remove-Item -LiteralPath (Join-Path $distributionData "area_catalog.json") -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $installationData "area_catalog.json") -Force -ErrorAction SilentlyContinue
Copy-Item -Path (Join-Path $source "*.rb") -Destination $distribution
Copy-Item -Path (Join-Path $source "*.rb") -Destination $installation
Copy-Item -LiteralPath $catalog -Destination (Join-Path $distributionData "area_catalog.dat")
Copy-Item -LiteralPath $catalog -Destination (Join-Path $installationData "area_catalog.dat")
Copy-Item -LiteralPath $installationGuide -Destination (Join-Path $distributionRoot "INSTALLATION.md")
Copy-Item -LiteralPath $releaseNotes -Destination (Join-Path $distributionRoot "RELEASE_NOTES.md")

Write-Output "Ironmon source and data copied to the distribution and local game."
