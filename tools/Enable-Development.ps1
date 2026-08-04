$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$gameRoot = Split-Path -Parent $projectRoot
$source = Join-Path $projectRoot "dev\001_Force_Debug.rb"
$destinationDirectory = Join-Path $gameRoot `
  "Data\Scripts\998_Ironmon_Development"
$destination = Join-Path $destinationDirectory "001_Force_Debug.rb"

New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
Copy-Item -LiteralPath $source -Destination $destination -Force

Write-Output "Ironmon development debug mode is enabled."
