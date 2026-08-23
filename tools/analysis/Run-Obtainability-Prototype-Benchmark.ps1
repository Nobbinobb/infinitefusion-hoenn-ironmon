param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))),
    [string]$OutputPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "simulation-results\obtainability-prototype-benchmark.json"),
    [ValidateRange(0, 2147483646)]
    [int]$Seed = 1187411801,
    [ValidateRange(60, 3600)]
    [int]$TimeoutSeconds = 900,
    [switch]$SkipBackground,
    [switch]$ShowGameWindow
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$generationRoot = Join-Path (Split-Path -Parent $PSScriptRoot) "generation"
$resolvedGameRoot = [IO.Path]::GetFullPath($GameRoot)
$resolvedOutputPath = [IO.Path]::GetFullPath($OutputPath)
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "src"))
$manifestPath = Join-Path $sourceRoot "load_order.json"
$loaderPath = Join-Path $generationRoot "Script-Loader.rb"
$benchmarkPath = Join-Path $PSScriptRoot "Obtainability-Prototype-Benchmark.rb"
$errorPath = "$resolvedOutputPath.error"
$progressPath = "$resolvedOutputPath.progress"

. (Join-Path $generationRoot "GameRuntime-Tooling.ps1")

foreach ($requiredPath in $manifestPath, $loaderPath, $benchmarkPath) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "The obtainability prototype benchmark dependency was not found at '$requiredPath'."
    }
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedOutputPath) | Out-Null
Remove-Item -LiteralPath $resolvedOutputPath, $errorPath, $progressPath, "$resolvedOutputPath.tmp" -Force -ErrorAction SilentlyContinue

$loaderSource = [IO.File]::ReadAllText($loaderPath, [Text.Encoding]::UTF8)
$rubyOutput = $resolvedOutputPath.Replace('\', '/')
$rubyGameRoot = $resolvedGameRoot.Replace('\', '/')
$rubySourceRoot = $sourceRoot.Replace('\', '/')
$rubyManifest = $manifestPath.Replace('\', '/')
$rubyBenchmarkPath = ([IO.Path]::GetFullPath($benchmarkPath)).Replace('\', '/')
$bootstrapSource = @(
    "begin"
    "`$ironmon_obtainability_prototype_benchmark_output_path = `"$rubyOutput`""
    "`$ironmon_obtainability_prototype_benchmark_seed = $Seed"
    "`$ironmon_obtainability_prototype_benchmark_skip_background = $($SkipBackground.IsPresent.ToString().ToLowerInvariant())"
    "Dir.chdir(`"$rubyGameRoot`")"
    $loaderSource
    "File.binwrite(`"$($progressPath.Replace('\', '/'))`", `"phase=loading base scripts\n`")"
    "IronmonScriptLoader.load_directory(`"Data/Scripts`", [/\A(?:997|998|999)/])"
    "GameData.load_all"
    "`$game_temp = Game_Temp.new"
    "Game.load_sprites_list_caches"
    "IronmonScriptLoader.load_manifest(`"$rubySourceRoot`", `"$rubyManifest`")"
    "benchmark_source = File.binread(`"$rubyBenchmarkPath`")"
    "eval(benchmark_source, TOPLEVEL_BINDING, `"$rubyBenchmarkPath`")"
    "exit! 0"
    "rescue Exception => error"
    "backtrace = error.backtrace ? error.backtrace.join(`"\n`") : `"`""
    "File.binwrite(`"$($errorPath.Replace('\', '/'))`", `"#{error.class}: #{error.message}\n#{backtrace}`")"
    "exit! 1"
    "end"
) -join "`n"

try {
    Invoke-IronmonGameRuntime `
        -GameRoot $resolvedGameRoot `
        -RubySource $bootstrapSource `
        -TimeoutSeconds $TimeoutSeconds `
        -OperationName "obtainability prototype benchmark" `
        -ErrorReportPath $errorPath `
        -ProgressPath $progressPath `
        -ProgressActivity "Ironmon obtainability prototype benchmark" `
        -ShowGameWindow:$ShowGameWindow

    if (-not (Test-Path -LiteralPath $resolvedOutputPath)) {
        throw "The bundled runtime did not produce an obtainability prototype benchmark report."
    }

    $report = Get-Content -LiteralPath $resolvedOutputPath -Raw | ConvertFrom-Json
    Write-Output "Obtainability prototype benchmark completed."
    Write-Output "Scenario: $($report.scenario)"
    Write-Output "Recipe preparation: $($report.recipe_preparation_milliseconds) ms"
    Write-Output "Deferred service initialization: $($report.initialization_milliseconds) ms"
    Write-Output "Fusion calculation: $($report.unordered_pairs) pairs in $($report.calculation_milliseconds) ms across $($report.requests) foreground chunks"
    Write-Output "First foreground chunk: $($report.first_batch_milliseconds) ms"
    Write-Output "Foreground latency: average $($report.average_request_milliseconds) ms, p95 $($report.p95_request_milliseconds) ms, maximum $($report.maximum_request_milliseconds) ms"
    Write-Output "Maximum foreground chunk: $($report.maximum_request_phase_before) to $($report.maximum_request_phase_after), $($report.maximum_request_processed_pair_delta) material pairs"
    if (-not $SkipBackground) {
        Write-Output "Background slices: $($report.background_slice_average_milliseconds) ms average, $($report.background_slice_maximum_milliseconds) ms maximum for a $($report.background_slice_budget_milliseconds) ms target"
        Write-Output "Background precomputation: $($report.background_precalculation_processed_pairs) pairs in $($report.background_precalculation_milliseconds) ms across $($report.background_slices) slices; stopped at $($report.background_precalculation_phase)"
        Write-Output "Maximum background slice: $($report.background_slice_maximum_phase_before) to $($report.background_slice_maximum_phase_after)"
    }
    Write-Output "Total: $($report.total_milliseconds) ms"
    Write-Output "Report: $resolvedOutputPath"
}
finally {
    Remove-Item -LiteralPath $errorPath, $progressPath, "$resolvedOutputPath.tmp" -Force -ErrorAction SilentlyContinue
}
