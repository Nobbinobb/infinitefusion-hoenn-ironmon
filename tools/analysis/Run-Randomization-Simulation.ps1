param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))),
    [string]$OutputPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "simulation-results\randomization-simulation.json"),
    [ValidateSet("Random", "Sequential")]
    [string]$SeedMode = "Random",
    [ValidateRange(0, 2147483646)]
    [int]$StartSeed = 1,
    [ValidateRange(-1, 2147483646)]
    [long]$SelectionSeed = -1,
    [ValidateRange(1, 10000)]
    [int]$SeedCount = 25,
    [ValidateRange(1, 500)]
    [int]$FusionSamplesPerSeed = 16,
    [ValidateRange(0, 100)]
    [int]$FusionPredecessorSamples = 12,
    [ValidateRange(1, 100000)]
    [int]$ItemSlotsPerSeed = 500,
    [ValidateRange(0, 86400)]
    [int]$TimeoutSeconds = 0,
    [switch]$ShowGameWindow
)

$ErrorActionPreference = "Stop"

$seedModeProvided = $PSBoundParameters.ContainsKey("SeedMode")
$startSeedProvided = $PSBoundParameters.ContainsKey("StartSeed")
$selectionSeedProvided = $PSBoundParameters.ContainsKey("SelectionSeed")
if ($startSeedProvided -and -not $seedModeProvided) {
    $SeedMode = "Sequential"
}
elseif ($startSeedProvided -and $SeedMode -eq "Random") {
    throw "StartSeed can only be used with -SeedMode Sequential."
}

if ($SeedMode -eq "Sequential") {
    if ($selectionSeedProvided) {
        throw "SelectionSeed can only be used with -SeedMode Random."
    }
    if (($StartSeed + [long]$SeedCount - 1) -gt 2147483646) {
        throw "The sequential seed range exceeds Ironmon's maximum seed of 2147483646."
    }
}
elseif ($SelectionSeed -eq -1) {
    $randomBytes = New-Object byte[] 4
    $randomNumberGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        do {
            $randomNumberGenerator.GetBytes($randomBytes)
            $randomValue = [BitConverter]::ToUInt32($randomBytes, 0)
        } while ($randomValue -ge 4294967294)
        $SelectionSeed = [long]($randomValue % 2147483647)
    }
    finally {
        $randomNumberGenerator.Dispose()
    }
}

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$generationRoot = Join-Path (Split-Path -Parent $PSScriptRoot) "generation"
$resolvedGameRoot = [IO.Path]::GetFullPath($GameRoot)
$resolvedOutputPath = [IO.Path]::GetFullPath($OutputPath)
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "src"))
$sourceManifestPath = Join-Path $sourceRoot "load_order.json"
$loaderPath = Join-Path $generationRoot "Script-Loader.rb"
$simulatorPath = Join-Path $PSScriptRoot "Randomization-Simulator.rb"
$errorPath = "$resolvedOutputPath.error"
$progressPath = "$resolvedOutputPath.progress"

if ($TimeoutSeconds -eq 0) {
    $secondsPerSeed = 3.0 +
        ($FusionSamplesPerSeed * 0.05) +
        ($ItemSlotsPerSeed * 0.001)
    $TimeoutSeconds = [Math]::Min(
        86400,
        [Math]::Max(
            300,
            [Math]::Ceiling(
                60 +
                ($SeedCount * $secondsPerSeed) +
                ($FusionPredecessorSamples * 5)
            )
        )
    )
}

. (Join-Path $generationRoot "GameRuntime-Tooling.ps1")

foreach ($requiredPath in $sourceManifestPath, $loaderPath, $simulatorPath) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "The simulation dependency was not found at '$requiredPath'."
    }
}

& (Join-Path (Split-Path -Parent $PSScriptRoot) "Build-Distribution.ps1")

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedOutputPath) | Out-Null
Remove-Item -LiteralPath $resolvedOutputPath, $errorPath, $progressPath -Force -ErrorAction SilentlyContinue
Write-Output "Runtime timeout budget: $TimeoutSeconds seconds."
if ($SeedMode -eq "Random") {
    $selectionDescription = if ($selectionSeedProvided) {
        "reproducible unique seeds"
    }
    else {
        "fresh unique seeds"
    }
    Write-Output "Seed selection: $SeedCount $selectionDescription (selection key $SelectionSeed)."
}
else {
    Write-Output "Seed selection: sequential seeds $StartSeed-$($StartSeed + $SeedCount - 1)."
}

$loaderSource = [IO.File]::ReadAllText($loaderPath, [Text.Encoding]::UTF8)
$simulatorSource = [IO.File]::ReadAllText($simulatorPath, [Text.Encoding]::UTF8)
$rubyOutput = $resolvedOutputPath.Replace('\', '/')
$rubyGameRoot = $resolvedGameRoot.Replace('\', '/')
$rubySourceRoot = $sourceRoot.Replace('\', '/')
$rubyManifest = $sourceManifestPath.Replace('\', '/')
$bootstrapSource = @(
    "begin"
    "`$ironmon_simulation_output_path = `"$rubyOutput`""
    "`$ironmon_simulation_game_root = `"$rubyGameRoot`""
    "`$ironmon_simulation_source_root = `"$rubySourceRoot`""
    "`$ironmon_simulation_manifest_path = `"$rubyManifest`""
    "`$ironmon_simulation_seed_mode = `"$($SeedMode.ToLowerInvariant())`""
    "`$ironmon_simulation_start_seed = $StartSeed"
    "`$ironmon_simulation_selection_seed = $SelectionSeed"
    "`$ironmon_simulation_seed_count = $SeedCount"
    "`$ironmon_simulation_fusion_samples_per_seed = $FusionSamplesPerSeed"
    "`$ironmon_simulation_fusion_predecessor_samples = $FusionPredecessorSamples"
    "`$ironmon_simulation_item_slots_per_seed = $ItemSlotsPerSeed"
    "Dir.chdir(`"$rubyGameRoot`")"
    $loaderSource
    "IronmonScriptLoader.load_directory(`"Data/Scripts`", [/\A(?:997|998|999)/])"
    "GameData.load_all"
    "`$game_temp = Game_Temp.new"
    "Game.load_sprites_list_caches"
    "IronmonScriptLoader.load_manifest(`"$rubySourceRoot`", `"$rubyManifest`")"
    $simulatorSource
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
        -OperationName "randomization batch simulation" `
        -ErrorReportPath $errorPath `
        -ProgressPath $progressPath `
        -ProgressActivity "Ironmon randomization simulation" `
        -ShowGameWindow:$ShowGameWindow

    if (-not (Test-Path -LiteralPath $resolvedOutputPath) -or
        (Get-Item -LiteralPath $resolvedOutputPath).Length -eq 0) {
        throw "The bundled runtime did not produce a simulation report."
    }

    $report = Get-Content -LiteralPath $resolvedOutputPath -Raw | ConvertFrom-Json
    Write-Output "Randomization simulation: $($report.status)"
    if ($report.seed_selection.mode -eq "random") {
        Write-Output "Seeds: $($report.seed_selection.count) unique random seeds (selection key $($report.seed_selection.selection_seed))"
    }
    else {
        Write-Output "Seeds: $($report.seed_selection.start)-$($report.seed_selection.end) ($($report.seed_selection.count) total)"
    }
    Write-Output "Normal species checks: $($report.families.species.normal_observations)"
    Write-Output "Fusion samples: $($report.families.species.fusion_samples)"
    Write-Output "Fusion predecessor pages: $($report.families.evolutions.fusion_predecessor_page_observations)"
    Write-Output "Item slots: $($report.families.items.ground_observations)"
    Write-Output "Hard failures: $($report.hard_failures.Count); statistical alerts: $($report.statistical_alerts.Count)"
    Write-Output "Report: $resolvedOutputPath"

    if ($report.status -ne "passed") {
        throw "The randomization simulation found $($report.hard_failures.Count) hard failure(s). See '$resolvedOutputPath'."
    }
}
finally {
    Remove-Item -LiteralPath $errorPath, $progressPath -Force -ErrorAction SilentlyContinue
}
