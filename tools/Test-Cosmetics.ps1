param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [ValidateRange(1, 600)][int]$TimeoutSeconds = 90,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$resultRoot = Join-Path $projectRoot 'data\cosmetics-validation'
New-Item -ItemType Directory -Force -Path $resultRoot | Out-Null
. (Join-Path $PSScriptRoot 'generation\GameRuntime-Tooling.ps1')
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'Build-Distribution.ps1') }
$errorPath = Join-Path $resultRoot 'runtime.error'
$resultPath = Join-Path $resultRoot 'runtime.json'
Remove-Item -LiteralPath $errorPath, $resultPath -Force -ErrorAction SilentlyContinue
$bootstrap = @(
    '$ironmon_cosmetic_test_root = ' + (ConvertTo-Json $resultRoot.Replace('\', '/') -Compress)
    [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'generation\Script-Loader.rb'))
    'begin'
    'IronmonScriptLoader.load_directory("Data/Scripts", [/\A(?:998|999)/])'
    'GameData.load_all'
    [IO.File]::ReadAllText((Join-Path $projectRoot 'tests\runtime\Cosmetics.rb'))
    'IronmonCosmeticRuntimeTests.run'
    'exit! 0'
    'rescue Exception => error'
    'File.binwrite(File.join($ironmon_cosmetic_test_root, "runtime.error"), "#{error.class}: #{error.message}\n#{error.backtrace.join("\n")}")'
    'exit! 1'
    'end'
) -join [Environment]::NewLine
Invoke-IronmonGameRuntime -GameRoot $GameRoot -RubySource $bootstrap -TimeoutSeconds $TimeoutSeconds -OperationName 'cosmetic runtime tests' -ErrorReportPath $errorPath
if (-not (Test-Path -LiteralPath $resultPath)) { throw 'The cosmetic runtime tests did not produce a result.' }
Get-Content -LiteralPath $resultPath -Raw
