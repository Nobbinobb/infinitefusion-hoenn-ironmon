<#
.SYNOPSIS
Validates tracker-owned title-menu actions with saves isolated below the project's ignored data directory.
#>
param([string]$GameRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)), [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'generation/GameRuntime-Tooling.ps1')
$testSaveRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'data/game-management-saves'))
Assert-PathWithinDirectory -Path $testSaveRoot -Directory (Join-Path $projectRoot 'data')
if (Test-Path -LiteralPath $testSaveRoot) {
    Remove-Item -LiteralPath $testSaveRoot -Recurse -Force
}
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'Build-Distribution.ps1') -GameRoot $GameRoot -DistributionRoot (Join-Path $projectRoot 'data/run-management-dist') -ReuseGeneratedAssets
}
$resultPath = Join-Path $projectRoot 'data/game-management-runtime.tests'
$errorPath = Join-Path $projectRoot 'data/game-management-runtime.error'
Remove-Item -LiteralPath $resultPath, $errorPath -Force -ErrorAction SilentlyContinue
$loader = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'generation/Script-Loader.rb'))
$test = [IO.File]::ReadAllText((Join-Path $projectRoot 'tests/runtime/Game-Management.rb'))
$rubyResult = $resultPath.Replace('\', '/') | ConvertTo-Json -Compress
$rubyError = $errorPath.Replace('\', '/') | ConvertTo-Json -Compress
$source = @(
    'begin'
    ('$ironmon_game_management_test_output_path = {0}' -f $rubyResult)
    ('$ironmon_game_management_test_save_root = {0}' -f ($testSaveRoot.Replace('\', '/') | ConvertTo-Json -Compress))
    $loader
    'IronmonScriptLoader.load_directory("Data/Scripts", [/\A(?:998|999)/])'
    $test
    'rescue Exception => error'
    ('File.binwrite({0}, [error.class.to_s, error.message, *error.backtrace].join("\n"))' -f $rubyError)
    'exit!(1)'
    'end'
    'exit!(0)'
) -join "`n"
Invoke-IronmonGameRuntime -GameRoot $GameRoot -RubySource $source -TimeoutSeconds 180 -OperationName 'tracker run-management regression tests' -ErrorReportPath $errorPath
Get-Content -LiteralPath $resultPath
