<#
.SYNOPSIS
Measures cooperative obtainability slices and a large native-result decoding probe in the bundled game.
.DESCRIPTION
Run Build-Distribution.ps1 first. The benchmark warms the pools and text that
gameplay has already used before the first rival victory. It uses the current
pinned profile and reports result fingerprints, slice lengths, checkpoint gaps,
and the first worker-job response time. It does not measure rendered frames,
tracker transport or serialization, or the native worker's calculation time.
.PARAMETER SourceRoot
Optional baseline source snapshot containing load_order.json. Keep its sibling
resources directory available when comparing a prior revision.
#>
param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))),
    [string]$OutputPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'simulation-results/obtainability-frames.json'),
    [int]$Seed = 1187411801,
    [ValidateRange(0.1, 250.0)]
    [double]$BudgetMilliseconds = 6.0,
    [string]$SourceRoot,
    [ValidateRange(30, 600)]
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$resolvedGameRoot = [IO.Path]::GetFullPath($GameRoot)
$resolvedOutputPath = [IO.Path]::GetFullPath($OutputPath)
. (Join-Path $projectRoot 'tools/generation/GameRuntime-Tooling.ps1')
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedOutputPath) | Out-Null
$loader = [IO.File]::ReadAllText((Join-Path $projectRoot 'tools/generation/Script-Loader.rb'))
$benchmark = (Join-Path $PSScriptRoot 'Obtainability-Frame-Benchmark.rb').Replace('\', '/')
if (-not $SourceRoot) { $SourceRoot = Join-Path $projectRoot 'src' }
$rubySourceRoot = [IO.Path]::GetFullPath($SourceRoot).Replace('\', '/')
$rubyGameRoot = $resolvedGameRoot.Replace('\', '/')
$rubyOutput = $resolvedOutputPath.Replace('\', '/')
$rubyBudget = $BudgetMilliseconds.ToString([Globalization.CultureInfo]::InvariantCulture)
$bootstrap = @'
begin
Dir.chdir("__GAME_ROOT__")
__LOADER__
IronmonScriptLoader.load_directory("Data/Scripts", [/\A(?:997|998|999)/])
GameData.load_all
$game_temp = Game_Temp.new
Game.load_sprites_list_caches
IronmonScriptLoader.load_manifest("__SOURCE__", "__SOURCE__/load_order.json")
$ironmon_frame_benchmark_output = "__OUTPUT__"
$ironmon_frame_benchmark_seed = __SEED__
$ironmon_frame_benchmark_budget = __BUDGET__
eval(File.binread("__BENCHMARK__"), TOPLEVEL_BINDING, "__BENCHMARK__")
exit! 0
rescue Exception => error
File.binwrite("__OUTPUT__.error", "#{error.class}: #{error.message}\n#{error.backtrace.join("\n")}")
exit! 1
end
'@
$bootstrap = $bootstrap.Replace('__GAME_ROOT__', $rubyGameRoot).Replace('__LOADER__', $loader).Replace('__SOURCE__', $rubySourceRoot).Replace('__OUTPUT__', $rubyOutput).Replace('__SEED__', $Seed.ToString()).Replace('__BUDGET__', $rubyBudget).Replace('__BENCHMARK__', $benchmark)

Remove-Item -LiteralPath $resolvedOutputPath, "$resolvedOutputPath.error" -Force -ErrorAction SilentlyContinue
Invoke-IronmonGameRuntime -GameRoot $resolvedGameRoot -RubySource $bootstrap -TimeoutSeconds $TimeoutSeconds -OperationName 'obtainability frame benchmark' -ErrorReportPath "$resolvedOutputPath.error"
$report = Get-Content -LiteralPath $resolvedOutputPath -Raw | ConvertFrom-Json
$report | Select-Object seed, slice_count, total_work_milliseconds, max_slice_milliseconds, p95_slice_milliseconds, slices_over_16_67_milliseconds, snapshot_milliseconds | Format-List
$report.native_result_probe | Format-List
Write-Output "Report: $resolvedOutputPath"
