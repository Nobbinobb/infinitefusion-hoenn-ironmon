<#
.SYNOPSIS
Validates Favorite Clause searches in the bundled game runtime without loading a save.
#>
param([int]$TimeoutSeconds = 90)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'generation\GameRuntime-Tooling.ps1')
$projectRoot = Split-Path -Parent $PSScriptRoot
$gameRoot = Split-Path -Parent $projectRoot
$reportPath = Join-Path $projectRoot 'data\favorite-search-test.json'
$loader = Get-Content (Join-Path $PSScriptRoot 'generation\Script-Loader.rb') -Raw
$testSource = Get-Content (Join-Path $projectRoot 'tests\runtime\Favorite-Search.rb') -Raw
$bootstrap = @(
    '# encoding: UTF-8'
    'begin'
    $loader
    'IronmonScriptLoader.load_directory("Data/Scripts", [/\A(?:998|999)/])'
    'GameData.load_all'
    ('$ironmon_favorite_search_output_path = "' + $reportPath.Replace('\', '/') + '"')
    $testSource
    'exit! 0'
    'rescue Exception => error'
    ('File.binwrite("' + $reportPath.Replace('\', '/') + '.error", "#{error.class}: #{error.message}\n#{error.backtrace.join("\n")}")')
    'exit! 1'
    'end'
) -join "`n"
& (Join-Path $PSScriptRoot 'Build-Distribution.ps1')
Remove-Item -LiteralPath "$reportPath.error" -Force -ErrorAction SilentlyContinue
Invoke-IronmonGameRuntime -GameRoot $gameRoot -RubySource $bootstrap -TimeoutSeconds $TimeoutSeconds -OperationName 'favorite search tests' -ErrorReportPath "$reportPath.error"
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$report | Select-Object passed, assertions | ConvertTo-Json -Compress
Write-Output "Full report: $reportPath"
