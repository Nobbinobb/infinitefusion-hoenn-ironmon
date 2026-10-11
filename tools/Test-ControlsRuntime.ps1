<#
.SYNOPSIS
Validates temporary tracker controls in an explicitly selected bundled game runtime.
#>
param(
    [Parameter(Mandatory)]
    [string]$GameRoot,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'generation/GameRuntime-Tooling.ps1')

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'Build-Distribution.ps1') -GameRoot $GameRoot -ReuseGeneratedAssets
}

$resultPath = Join-Path $projectRoot 'data/controls-runtime.tests'
$errorPath = Join-Path $projectRoot 'data/controls-runtime.error'
$loader = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'generation/Script-Loader.rb'))
$test = [IO.File]::ReadAllText((Join-Path $projectRoot 'tests/runtime/Controls.rb'))
$rubyResult = $resultPath.Replace('\', '/').Replace("'", "\\'")
$rubyError = $errorPath.Replace('\', '/').Replace("'", "\\'")
$source = @(
    'begin'
    ('$ironmon_controls_test_output_path = ''{0}''' -f $rubyResult)
    $loader
    'IronmonScriptLoader.load_directory("Data/Scripts", [/\A(?:998|999)/])'
    'GameData.load_all'
    $test
    'rescue Exception => error'
    ('File.binwrite(''{0}'', [error.class.to_s, error.message, *error.backtrace].join("\n"))' -f $rubyError)
    'exit!(1)'
    'end'
    'exit!(0)'
) -join "`n"

Invoke-IronmonGameRuntime -GameRoot $GameRoot -RubySource $source -TimeoutSeconds 90 -OperationName 'controller input regression tests' -ErrorReportPath $errorPath
Get-Content -LiteralPath $resultPath
