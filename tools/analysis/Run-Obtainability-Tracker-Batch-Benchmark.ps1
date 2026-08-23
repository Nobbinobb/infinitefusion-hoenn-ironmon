param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))),
    [string]$OutputPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "simulation-results\obtainability-tracker-batch-benchmark.json"),
    [ValidateRange(0, 2147483646)]
    [int]$Seed = 1187411801,
    [ValidateRange(60, 3600)]
    [int]$TimeoutSeconds = 900
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$generationRoot = Join-Path (Split-Path -Parent $PSScriptRoot) "generation"
$resolvedGameRoot = [IO.Path]::GetFullPath($GameRoot)
$resolvedOutputPath = [IO.Path]::GetFullPath($OutputPath)
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "src"))
$manifestPath = Join-Path $sourceRoot "load_order.json"
$loaderPath = Join-Path $generationRoot "Script-Loader.rb"
$benchmarkPath = Join-Path $PSScriptRoot "Obtainability-Tracker-Batch-Benchmark.rb"
$errorPath = "$resolvedOutputPath.error"
$mappingPath = "$resolvedOutputPath.mappings.jsonl"

. (Join-Path $generationRoot "GameRuntime-Tooling.ps1")

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedOutputPath) | Out-Null
Remove-Item -LiteralPath $resolvedOutputPath, $errorPath, "$resolvedOutputPath.tmp", $mappingPath -Force -ErrorAction SilentlyContinue
$loaderSource = [IO.File]::ReadAllText($loaderPath, [Text.Encoding]::UTF8)
$rubyOutput = $resolvedOutputPath.Replace('\', '/')
$rubyGameRoot = $resolvedGameRoot.Replace('\', '/')
$rubySourceRoot = $sourceRoot.Replace('\', '/')
$rubyManifest = $manifestPath.Replace('\', '/')
$rubyBenchmark = ([IO.Path]::GetFullPath($benchmarkPath)).Replace('\', '/')
$rubyMapping = $mappingPath.Replace('\', '/')
function New-BenchmarkBootstrapSource {
    param(
        [Parameter(Mandatory)]
        [ValidateSet("prepare", "apply")]
        [string]$Mode
    )
    return @(
    "begin"
    "`$ironmon_obtainability_tracker_batch_output_path = `"$rubyOutput`""
    "`$ironmon_obtainability_tracker_batch_mapping_path = `"$rubyMapping`""
    "`$ironmon_obtainability_tracker_batch_mode = `"$Mode`""
    "`$ironmon_obtainability_tracker_batch_seed = $Seed"
    "Dir.chdir(`"$rubyGameRoot`")"
    $loaderSource
    "IronmonScriptLoader.load_directory(`"Data/Scripts`", [/\A(?:997|998|999)/])"
    "GameData.load_all"
    "`$game_temp = Game_Temp.new"
    "Game.load_sprites_list_caches"
    "IronmonScriptLoader.load_manifest(`"$rubySourceRoot`", `"$rubyManifest`")"
    "eval(File.binread(`"$rubyBenchmark`"), TOPLEVEL_BINDING, `"$rubyBenchmark`")"
    "exit! 0"
    "rescue Exception => error"
    "backtrace = error.backtrace ? error.backtrace.join(`"\n`") : `"`""
    "File.binwrite(`"$($errorPath.Replace('\', '/'))`", `"#{error.class}: #{error.message}\n#{backtrace}`")"
    "exit! 1"
    "end"
    ) -join "`n"
}

try {
    Invoke-IronmonGameRuntime `
        -GameRoot $resolvedGameRoot `
        -RubySource (New-BenchmarkBootstrapSource -Mode prepare) `
        -TimeoutSeconds $TimeoutSeconds `
        -OperationName "obtainability tracker mapping preparation" `
        -ErrorReportPath $errorPath
    Invoke-IronmonGameRuntime `
        -GameRoot $resolvedGameRoot `
        -RubySource (New-BenchmarkBootstrapSource -Mode apply) `
        -TimeoutSeconds $TimeoutSeconds `
        -OperationName "cold obtainability tracker-batch benchmark" `
        -ErrorReportPath $errorPath
    $report = Get-Content -LiteralPath $resolvedOutputPath -Raw | ConvertFrom-Json
    Write-Output "Tracker-batch obtainability benchmark completed."
    Write-Output "Excluded fixture preparation: $($report.fixture_mapping_preparation_milliseconds) ms"
    Write-Output "Game batch application: $($report.batch_application_milliseconds) ms across $($report.batch_count) batches"
    Write-Output "Game-side total excluding mapping worker: $($report.total_milliseconds_excluding_reference_mapping) ms"
    Write-Output "Report: $resolvedOutputPath"
}
finally {
    Remove-Item -LiteralPath $errorPath, "$resolvedOutputPath.tmp", $mappingPath -Force -ErrorAction SilentlyContinue
}
