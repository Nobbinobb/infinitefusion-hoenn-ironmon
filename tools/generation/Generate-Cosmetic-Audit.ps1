param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))),
    [string]$OutputPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'docs\audits\generated\COSMETICS_GENERATED.json'),
    [string]$PreviousPath,
    [ValidateRange(1, 600)][int]$TimeoutSeconds = 90
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GameRuntime-Tooling.ps1')
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
Assert-PathWithinDirectory -Path $resolvedOutput -Directory $projectRoot
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedOutput) | Out-Null
if (-not $PreviousPath) { $PreviousPath = $resolvedOutput }
$bootstrap = @(
    '$ironmon_cosmetic_audit_path = ' + (ConvertTo-Json $resolvedOutput.Replace('\', '/') -Compress)
    '$ironmon_cosmetic_previous_path = ' + (ConvertTo-Json ([IO.Path]::GetFullPath($PreviousPath)).Replace('\', '/') -Compress)
    [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Script-Loader.rb'))
    'IronmonScriptLoader.load_directory("Data/Scripts", [/\A(?:997|998|999)/])'
    [IO.File]::ReadAllText((Join-Path $projectRoot 'src\cosmetics\Catalog.rb'))
    [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Export-CosmeticAudit.rb'))
) -join [Environment]::NewLine
Remove-Item -LiteralPath "$resolvedOutput.error" -Force -ErrorAction SilentlyContinue
Invoke-IronmonGameRuntime -GameRoot $GameRoot -RubySource $bootstrap -TimeoutSeconds $TimeoutSeconds -OperationName 'cosmetic catalog audit' -ErrorReportPath "$resolvedOutput.error"
$report = Get-Content -LiteralPath $resolvedOutput -Raw | ConvertFrom-Json
if ($report.schema_version -ne 1 -or $null -eq $report.summary) { throw 'Invalid cosmetic audit output.' }
Write-Output "Audited $($report.summary.catalog_entries) cosmetics: $($report.summary.available_entries) available, $($report.summary.available_total_points) points in total."
foreach ($warning in $report.warnings) { Write-Warning $warning }
