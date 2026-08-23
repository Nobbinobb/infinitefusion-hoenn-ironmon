param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))),
    [string]$OutputPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "simulation-results\obtainability-foundation-benchmark.json"),
    [string]$AreaCatalogPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "data\area_catalog.dat"),
    [ValidateRange(0, 2147483646)]
    [int]$Seed = 1187411801,
    [ValidateRange(0, 20)]
    [int]$InverseTargetCount = 3,
    [ValidateRange(60, 7200)]
    [int]$TimeoutSeconds = 1800,
    [switch]$FullMaterialScan,
    [switch]$ShowGameWindow
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$generationRoot = Join-Path (Split-Path -Parent $PSScriptRoot) "generation"
$resolvedGameRoot = [IO.Path]::GetFullPath($GameRoot)
$resolvedOutputPath = [IO.Path]::GetFullPath($OutputPath)
$resolvedAreaCatalogPath = [IO.Path]::GetFullPath($AreaCatalogPath)
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "src"))
$manifestPath = Join-Path $sourceRoot "load_order.json"
$loaderPath = Join-Path $generationRoot "Script-Loader.rb"
$benchmarkPath = Join-Path $PSScriptRoot "Obtainability-Foundation-Benchmark.rb"
$errorPath = "$resolvedOutputPath.error"
$progressPath = "$resolvedOutputPath.progress"

. (Join-Path $generationRoot "GameRuntime-Tooling.ps1")

foreach ($requiredPath in $manifestPath, $loaderPath, $benchmarkPath, $resolvedAreaCatalogPath) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "The obtainability benchmark dependency was not found at '$requiredPath'."
    }
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedOutputPath) | Out-Null
Remove-Item -LiteralPath $resolvedOutputPath, $errorPath, $progressPath, "$resolvedOutputPath.tmp" -Force -ErrorAction SilentlyContinue

$loaderSource = [IO.File]::ReadAllText($loaderPath, [Text.Encoding]::UTF8)
$rubyOutput = $resolvedOutputPath.Replace('\', '/')
$rubyGameRoot = $resolvedGameRoot.Replace('\', '/')
$rubySourceRoot = $sourceRoot.Replace('\', '/')
$rubyManifest = $manifestPath.Replace('\', '/')
$rubyAreaCatalog = $resolvedAreaCatalogPath.Replace('\', '/')
$rubyProgress = $progressPath.Replace('\', '/')
$rubyBenchmarkPath = ([IO.Path]::GetFullPath($benchmarkPath)).Replace('\', '/')
$rubyFullMaterialScan = if ($FullMaterialScan) { "true" } else { "false" }
$bootstrapSource = @(
    "begin"
    "`$ironmon_obtainability_benchmark_output_path = `"$rubyOutput`""
    "`$ironmon_obtainability_benchmark_seed = $Seed"
    "`$ironmon_obtainability_benchmark_area_catalog_path = `"$rubyAreaCatalog`""
    "`$ironmon_obtainability_benchmark_full_material_scan = $rubyFullMaterialScan"
    "`$ironmon_obtainability_benchmark_inverse_target_count = $InverseTargetCount"
    "Dir.chdir(`"$rubyGameRoot`")"
    $loaderSource
    "File.binwrite(`"$rubyProgress`", `"phase=loading base scripts\n`")"
    "IronmonScriptLoader.load_directory(`"Data/Scripts`", [/\A(?:997|998|999)/])"
    "File.binwrite(`"$rubyProgress`", `"phase=loading game data\n`")"
    "GameData.load_all"
    "`$game_temp = Game_Temp.new"
    "File.binwrite(`"$rubyProgress`", `"phase=loading sprite catalog\n`")"
    "Game.load_sprites_list_caches"
    "File.binwrite(`"$rubyProgress`", `"phase=loading Ironmon manifest\n`")"
    "IronmonScriptLoader.load_manifest(`"$rubySourceRoot`", `"$rubyManifest`")"
    "File.binwrite(`"$rubyProgress`", `"phase=parsing benchmark\n`")"
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
        -OperationName "obtainability foundation benchmark" `
        -ErrorReportPath $errorPath `
        -ProgressPath $progressPath `
        -ProgressActivity "Ironmon obtainability foundation benchmark" `
        -ShowGameWindow:$ShowGameWindow

    if (-not (Test-Path -LiteralPath $resolvedOutputPath) -or
        (Get-Item -LiteralPath $resolvedOutputPath).Length -eq 0) {
        throw "The bundled runtime did not produce an obtainability benchmark report."
    }

    $report = Get-Content -LiteralPath $resolvedOutputPath -Raw | ConvertFrom-Json
    $closures = $report.benchmarks.normal_evolution_closure.modes
    $itemSupply = $report.benchmarks.item_supply_enumeration
    Write-Output "Obtainability foundation benchmark completed."
    foreach ($closure in $closures) {
        Write-Output "Normal closure ($($closure.mode), $($closure.policy)): $($closure.seed_species) material seeds to $($closure.reachable_species) reachable species."
    }
    Write-Output "Randomized ground supply: $($itemSupply.slot_count) slots, $($itemSupply.available_required_item_types)/$($itemSupply.required_item_types) required item types present for this seed; $($itemSupply.candidate_required_item_types) including authored gift candidates."
    foreach ($scenario in $report.benchmarks.player_fusion_material_mapping) {
        Write-Output "Fusion mapping: $($scenario.material_species) materials, $($scenario.unordered_pairs) pairs in $($scenario.elapsed_milliseconds) ms."
    }
    Write-Output "Report: $resolvedOutputPath"
}
finally {
    Remove-Item -LiteralPath $errorPath, $progressPath, "$resolvedOutputPath.tmp" -Force -ErrorAction SilentlyContinue
}
