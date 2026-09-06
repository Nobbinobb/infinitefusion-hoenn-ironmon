<#
.SYNOPSIS
Validates individual gym badge snapshots and live updates in the bundled game runtime.
#>
param([int]$TimeoutSeconds = 90)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'generation\GameRuntime-Tooling.ps1')
$projectRoot = Split-Path -Parent $PSScriptRoot
$gameRoot = Split-Path -Parent $projectRoot
$reportPath = Join-Path $projectRoot 'data\gym-badge-test.txt'
$loader = Get-Content (Join-Path $PSScriptRoot 'generation\Script-Loader.rb') -Raw
$testSource = Get-Content (Join-Path $projectRoot 'tests\runtime\Gym-Badges.rb') -Raw
$bootstrap = @(
    '# encoding: UTF-8'
    'begin'
    $loader
    'IronmonScriptLoader.load_directory("Data/Scripts", [/\A(?:998|999)/])'
    'GameData.load_all'
    ('$ironmon_gym_badge_test_output_path = "' + $reportPath.Replace('\', '/') + '"')
    $testSource
    'exit! 0'
    'rescue Exception => error'
    ('File.binwrite("' + $reportPath.Replace('\', '/') + '.error", "#{error.class}: #{error.message}\n#{error.backtrace.join("\n")}")')
    'exit! 1'
    'end'
) -join "`n"
& (Join-Path $PSScriptRoot 'Build-Distribution.ps1')
Remove-Item -LiteralPath $reportPath, "$reportPath.error" -Force -ErrorAction SilentlyContinue
Invoke-IronmonGameRuntime -GameRoot $gameRoot -RubySource $bootstrap -TimeoutSeconds $TimeoutSeconds -OperationName 'gym badge tests' -ErrorReportPath "$reportPath.error"
Get-Content -LiteralPath $reportPath
