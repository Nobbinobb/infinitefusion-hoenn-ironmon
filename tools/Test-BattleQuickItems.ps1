param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'generation/GameRuntime-Tooling.ps1')
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'Build-Distribution.ps1') -GameRoot $GameRoot -ReuseGeneratedAssets
}
$outputDirectory = Join-Path $projectRoot 'data/battle-quick-items-validation'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$resultPath = Join-Path $outputDirectory 'result.txt'
$errorPath = Join-Path $outputDirectory 'error.txt'
Remove-Item -LiteralPath $resultPath, $errorPath -Force -ErrorAction SilentlyContinue
$loader = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'generation/Script-Loader.rb'))
$test = [IO.File]::ReadAllText((Join-Path $projectRoot 'tests/runtime/Battle-Quick-Items.rb'))
$source = @(
    'begin'
    ('$ironmon_quick_items_output = ''{0}''' -f $outputDirectory.Replace('\', '/'))
    $loader
    'IronmonScriptLoader.load_directory("Data/Scripts", [/\A(?:998|999)/])'
    'GameData.load_all'
    $test
    'rescue Exception => error'
    ('File.binwrite(''{0}'', [error.class.to_s, error.message, *error.backtrace].join("\n"))' -f $errorPath.Replace('\', '/'))
    'exit!(1)'
    'end'
    'exit!(0)'
) -join "`n"
Invoke-IronmonGameRuntime -GameRoot $GameRoot -RubySource $source -TimeoutSeconds 90 -OperationName 'battle quick-item validation' -ErrorReportPath $errorPath
if (-not (Test-Path -LiteralPath $resultPath)) { throw 'Battle quick-item validation did not complete.' }
Get-Content -LiteralPath $resultPath
