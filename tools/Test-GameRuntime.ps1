param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [ValidateRange(1, 600)]
    [int]$TimeoutSeconds = 90,
    [switch]$ShowGameWindow
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$generationRoot = Join-Path $PSScriptRoot "generation"
$resolvedGameRoot = [IO.Path]::GetFullPath($GameRoot)
$scriptLoaderPath = Join-Path $generationRoot "Script-Loader.rb"
$diagnosticTestPath = Join-Path $projectRoot "tests\runtime\Diagnostic-Access.rb"
$catchAssistanceTestPath = Join-Path $projectRoot "tests\runtime\Catch-Assistance.rb"
$battleItemTestPath = Join-Path $projectRoot "tests\runtime\Battle-Items.rb"
$repelOverlayTestPath = Join-Path $projectRoot "tests\runtime\Repel-Overlay.rb"
$itemRandomizationTestPath = Join-Path $projectRoot "tests\runtime\Item-Randomization.rb"
$seededRunImportTestPath = Join-Path $projectRoot "tests\runtime\Seeded-Run-Import.rb"
$diagnosticResultPath = Join-Path $projectRoot "runtime-diagnostic-access.tests"
$catchAssistanceResultPath = Join-Path $projectRoot "runtime-catch-assistance.tests"
$battleItemResultPath = Join-Path $projectRoot "runtime-battle-items.tests"
$repelOverlayResultPath = Join-Path $projectRoot "runtime-repel-overlay.tests"
$itemRandomizationResultPath = Join-Path $projectRoot "runtime-item-randomization.tests"
$seededRunImportResultPath = Join-Path $projectRoot "runtime-seeded-run-import.tests"
$diagnosticErrorPath = Join-Path $projectRoot "runtime-diagnostic-access.error"

. (Join-Path $generationRoot "GameRuntime-Tooling.ps1")

& (Join-Path $PSScriptRoot "Build-Distribution.ps1")
& (Join-Path $generationRoot "Generate-Area-Catalog.ps1") `
    -GameRoot $resolvedGameRoot `
    -TimeoutSeconds $TimeoutSeconds `
    -ShowGameWindow:$ShowGameWindow `
    -ValidateInstalledScripts `
    -RunAreaProgressTests

$scriptLoaderSource = [IO.File]::ReadAllText(
    $scriptLoaderPath,
    [Text.Encoding]::UTF8
)
$diagnosticTestSource = [IO.File]::ReadAllText(
    $diagnosticTestPath,
    [Text.Encoding]::UTF8
)
$catchAssistanceTestSource = [IO.File]::ReadAllText(
    $catchAssistanceTestPath,
    [Text.Encoding]::UTF8
)
$battleItemTestSource = [IO.File]::ReadAllText(
    $battleItemTestPath,
    [Text.Encoding]::UTF8
)
$repelOverlayTestSource = [IO.File]::ReadAllText(
    $repelOverlayTestPath,
    [Text.Encoding]::UTF8
)
$itemRandomizationTestSource = [IO.File]::ReadAllText(
    $itemRandomizationTestPath,
    [Text.Encoding]::UTF8
)
$seededRunImportTestSource = [IO.File]::ReadAllText(
    $seededRunImportTestPath,
    [Text.Encoding]::UTF8
)
$rubyResultPath = $diagnosticResultPath.Replace('\', '/')
$rubyCatchAssistanceResultPath = $catchAssistanceResultPath.Replace('\', '/')
$rubyBattleItemResultPath = $battleItemResultPath.Replace('\', '/')
$rubyRepelOverlayResultPath = $repelOverlayResultPath.Replace('\', '/')
$rubyItemRandomizationResultPath = $itemRandomizationResultPath.Replace('\', '/')
$rubySeededRunImportResultPath = $seededRunImportResultPath.Replace('\', '/')
$bootstrapSource = @(
    "begin"
    "`$ironmon_diagnostic_access_test_output_path = `"$rubyResultPath`""
    "`$ironmon_catch_assistance_test_output_path = `"$rubyCatchAssistanceResultPath`""
    "`$ironmon_battle_item_test_output_path = `"$rubyBattleItemResultPath`""
    "`$ironmon_repel_overlay_test_output_path = `"$rubyRepelOverlayResultPath`""
    "`$ironmon_item_randomization_test_output_path = `"$rubyItemRandomizationResultPath`""
    "`$ironmon_seeded_run_import_test_output_path = `"$rubySeededRunImportResultPath`""
    "Dir.chdir(`"$($resolvedGameRoot.Replace('\', '/'))`")"
    $scriptLoaderSource
    "IronmonScriptLoader.load_directory(`"Data/Scripts`", [/\A(?:998|999)/])"
    "GameData.load_all"
    $catchAssistanceTestSource
    $battleItemTestSource
    $repelOverlayTestSource
    $itemRandomizationTestSource
    $seededRunImportTestSource
    $diagnosticTestSource
    "exit! 0"
    "rescue Exception => error"
    "backtrace = error.backtrace ? error.backtrace.join(`"\n`") : `"`""
    "File.binwrite(`"$rubyResultPath`", `"#{error.class}: #{error.message}\n#{backtrace}`")"
    "exit! 1"
    "end"
) -join "`n"
Remove-Item -LiteralPath $diagnosticResultPath, $catchAssistanceResultPath, $battleItemResultPath, $repelOverlayResultPath, $itemRandomizationResultPath, $seededRunImportResultPath, $diagnosticErrorPath `
    -Force `
    -ErrorAction SilentlyContinue
try {
    Invoke-IronmonGameRuntime `
        -GameRoot $resolvedGameRoot `
        -RubySource $bootstrapSource `
        -TimeoutSeconds $TimeoutSeconds `
        -OperationName "diagnostic access runtime tests" `
        -ErrorReportPath $diagnosticResultPath `
        -ShowGameWindow:$ShowGameWindow

    if (-not (Test-Path -LiteralPath $diagnosticResultPath) -or
        (Get-Content -LiteralPath $diagnosticResultPath -Raw).Trim() -ne
            "diagnostic access runtime tests passed") {
        throw "The bundled runtime did not complete the diagnostic access tests."
    }
    if (-not (Test-Path -LiteralPath $catchAssistanceResultPath) -or
        (Get-Content -LiteralPath $catchAssistanceResultPath -Raw).Trim() -ne
            "catch assistance runtime tests passed") {
        throw "The bundled runtime did not complete the catch assistance tests."
    }
    if (-not (Test-Path -LiteralPath $battleItemResultPath) -or
        (Get-Content -LiteralPath $battleItemResultPath -Raw).Trim() -ne
            "battle item runtime tests passed") {
        throw "The bundled runtime did not complete the battle item tests."
    }
    if (-not (Test-Path -LiteralPath $repelOverlayResultPath) -or
        (Get-Content -LiteralPath $repelOverlayResultPath -Raw).Trim() -ne
            "repel overlay runtime tests passed") {
        throw "The bundled runtime did not complete the Repel overlay tests."
    }
    if (-not (Test-Path -LiteralPath $itemRandomizationResultPath) -or
        (Get-Content -LiteralPath $itemRandomizationResultPath -Raw).Trim() -ne
            "item randomization runtime tests passed") {
        throw "The bundled runtime did not complete the item randomization tests."
    }
    if (-not (Test-Path -LiteralPath $seededRunImportResultPath) -or
        (Get-Content -LiteralPath $seededRunImportResultPath -Raw).Trim() -ne
            "seeded-run import runtime tests passed") {
        throw "The bundled runtime did not complete the seeded-run import tests."
    }
}
finally {
    Remove-Item -LiteralPath $diagnosticResultPath, $catchAssistanceResultPath, $battleItemResultPath, $repelOverlayResultPath, $itemRandomizationResultPath, $seededRunImportResultPath, $diagnosticErrorPath `
        -Force `
        -ErrorAction SilentlyContinue
}

Write-Output "Bundled-runtime area progress, diagnostic access, catch assistance, battle item, Repel overlay, item randomization, and seeded-run import tests passed."
