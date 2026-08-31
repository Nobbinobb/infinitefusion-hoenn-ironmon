<#
.SYNOPSIS
Generates the release defense catalog and public catalog audit in the bundled runtime.
.DESCRIPTION
Compiles the reviewed audit definitions with current game names and move flags.
Catalog hashes record provenance only; old data remains usable after game updates.
#>
param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))),
    [string]$OutputPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'data\defense_presentation.json'),
    [string]$AuditPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'docs\audits\generated\DEFENSE_PRESENTATION_GENERATED.csv'),
    [ValidateRange(1, 600)][int]$TimeoutSeconds = 90
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GameRuntime-Tooling.ps1')
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$resolvedGameRoot = [IO.Path]::GetFullPath($GameRoot)
$resolvedOutputPath = [IO.Path]::GetFullPath($OutputPath)
$resolvedAuditPath = [IO.Path]::GetFullPath($AuditPath)
$rulesPath = Join-Path $projectRoot 'docs\audits\DEFENSE_PRESENTATION_RULES.json'
Assert-PathWithinDirectory -Path $resolvedOutputPath -Directory $projectRoot
Assert-PathWithinDirectory -Path $resolvedAuditPath -Directory $projectRoot
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedOutputPath) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedAuditPath) | Out-Null
$assignments = @{
    ironmon_defense_rules_path = $rulesPath
    ironmon_defense_catalog_path = $resolvedOutputPath
    ironmon_defense_audit_path = $resolvedAuditPath
}
$bootstrap = @(
    foreach ($entry in $assignments.GetEnumerator()) {
        '$' + $entry.Key + ' = ' + (ConvertTo-Json -InputObject $entry.Value.Replace('\', '/') -Compress)
    }
    [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Script-Loader.rb'))
    [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Export-DefensePresentation.rb'))
) -join [Environment]::NewLine
Remove-Item -LiteralPath "$resolvedOutputPath.error" -Force -ErrorAction SilentlyContinue
Invoke-IronmonGameRuntime -GameRoot $resolvedGameRoot -RubySource $bootstrap -TimeoutSeconds $TimeoutSeconds -OperationName 'defense presentation generation' -ErrorReportPath "$resolvedOutputPath.error"
foreach ($generatedPath in $resolvedOutputPath, $resolvedAuditPath) {
    if (-not (Test-Path -LiteralPath $generatedPath) -or (Get-Item -LiteralPath $generatedPath).Length -eq 0) {
        throw "The game runtime did not produce '$generatedPath'."
    }
}
Write-Output "Generated defense presentation catalog and audit: $resolvedOutputPath"
