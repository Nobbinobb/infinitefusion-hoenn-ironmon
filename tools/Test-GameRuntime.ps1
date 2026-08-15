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
$diagnosticResultPath = Join-Path $projectRoot "runtime-diagnostic-access.tests"
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
$rubyResultPath = $diagnosticResultPath.Replace('\', '/')
$bootstrapSource = @(
    "begin"
    "`$ironmon_diagnostic_access_test_output_path = `"$rubyResultPath`""
    "Dir.chdir(`"$($resolvedGameRoot.Replace('\', '/'))`")"
    $scriptLoaderSource
    "IronmonScriptLoader.load_directory(`"Data/Scripts`", [/\A(?:998|999)/])"
    $diagnosticTestSource
    "exit! 0"
    "rescue Exception => error"
    "backtrace = error.backtrace ? error.backtrace.join(`"\n`") : `"`""
    "File.binwrite(`"$rubyResultPath`", `"#{error.class}: #{error.message}\n#{backtrace}`")"
    "exit! 1"
    "end"
) -join "`n"
Remove-Item -LiteralPath $diagnosticResultPath, $diagnosticErrorPath `
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
}
finally {
    Remove-Item -LiteralPath $diagnosticResultPath, $diagnosticErrorPath `
        -Force `
        -ErrorAction SilentlyContinue
}

Write-Output "Bundled-runtime area progress and diagnostic access tests passed."
