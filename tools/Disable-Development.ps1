$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$gameRoot = Split-Path -Parent $projectRoot
$destination = Join-Path $gameRoot `
  "Data\Scripts\998_Ironmon_Development\001_Force_Debug.rb"

if (Test-Path -LiteralPath $destination) {
  Remove-Item -LiteralPath $destination -Force
}

Write-Output "Ironmon development debug mode is disabled."
