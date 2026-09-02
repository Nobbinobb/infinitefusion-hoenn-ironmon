param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [ValidateRange(1, 600)][int]$TimeoutSeconds = 90,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$fixturePath = Join-Path $projectRoot "tests\fixtures\generation-profile-v1.json"
$poolFixturePath = Join-Path $projectRoot "tests\fixtures\custom-fusion-pool-v1.json"
$testPath = Join-Path $projectRoot "tests\runtime\Generation-Profile.rb"
$resultPath = Join-Path $projectRoot "runtime-generation-profile.tests"
$errorPath = Join-Path $projectRoot "runtime-generation-profile.error"

. (Join-Path $PSScriptRoot "generation\GameRuntime-Tooling.ps1")
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "Build-Distribution.ps1")
}
Remove-Item -LiteralPath $resultPath, $errorPath -Force -ErrorAction SilentlyContinue
$rubyFixturePath = ([IO.Path]::GetFullPath($fixturePath)).Replace('\', '/')
$rubyPoolFixturePath = ([IO.Path]::GetFullPath($poolFixturePath)).Replace('\', '/')
$rubyResultPath = ([IO.Path]::GetFullPath($resultPath)).Replace('\', '/')
$testSource = [IO.File]::ReadAllText($testPath, [Text.Encoding]::UTF8)
$loaderSource = [IO.File]::ReadAllText(
    (Join-Path $PSScriptRoot "generation\Script-Loader.rb"),
    [Text.Encoding]::UTF8
)
$bootstrap = @(
    "begin"
    "`$ironmon_generation_profile_fixture_path = `"$rubyFixturePath`""
    "`$ironmon_custom_fusion_component_fixture_path = `"$rubyPoolFixturePath`""
    "`$ironmon_generation_profile_test_output_path = `"$rubyResultPath`""
    $loaderSource
    "IronmonScriptLoader.load_directory(`"Data/Scripts`", [/\A(?:998|999)/])"
    "GameData.load_all"
    $testSource
    "exit! 0"
    "rescue Exception => error"
    "File.binwrite(`"$($errorPath.Replace('\', '/'))`", `"#{error.class}: #{error.message}\n#{error.backtrace.join(`"\n`")}`")"
    "exit! 1"
    "end"
) -join [Environment]::NewLine

Invoke-IronmonGameRuntime `
    -GameRoot $GameRoot `
    -RubySource $bootstrap `
    -TimeoutSeconds $TimeoutSeconds `
    -OperationName "generation-profile runtime tests" `
    -ErrorReportPath $errorPath
if (-not (Test-Path -LiteralPath $resultPath)) {
    throw "The generation-profile runtime tests did not produce a result."
}
$result = [IO.File]::ReadAllText($resultPath, [Text.Encoding]::UTF8).Trim()
if ($result -ne "generation-profile tests passed") {
    throw $result
}
Remove-Item -LiteralPath $resultPath, $errorPath -Force -ErrorAction SilentlyContinue
Write-Output $result
