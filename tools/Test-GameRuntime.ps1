param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [ValidateRange(1, 600)]
    [int]$TimeoutSeconds = 90,
    [switch]$BenchmarkFusionPredecessors,
    [switch]$ShowGameWindow
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$generationRoot = Join-Path $PSScriptRoot "generation"
$resolvedGameRoot = [IO.Path]::GetFullPath($GameRoot)
$runtimeSaveDataRoot = Join-Path `
    ([Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)) `
    "infinitefusion-hoenn"
$debugPokemonSearchTracePath = Join-Path $runtimeSaveDataRoot `
    "debug_pokemon_search_trace.log"
$playerFusionPreparationTracePath = Join-Path $runtimeSaveDataRoot `
    "player_fusion_preparation_trace.log"
$scriptLoaderPath = Join-Path $generationRoot "Script-Loader.rb"
$diagnosticTestPath = Join-Path $projectRoot "tests\runtime\Diagnostic-Access.rb"
$catchAssistanceTestPath = Join-Path $projectRoot "tests\runtime\Catch-Assistance.rb"
$battleItemTestPath = Join-Path $projectRoot "tests\runtime\Battle-Items.rb"
$battleRunHotkeyTestPath = Join-Path $projectRoot "tests\runtime\Battle-Run-Hotkey.rb"
$battleMoveTypeColorTestPath = Join-Path $projectRoot "tests\runtime\Battle-Move-Type-Colors.rb"
$movePowerPresentationTestPath = Join-Path $projectRoot "tests\runtime\Move-Power-Presentation.rb"
$trainerRematchTestPath = Join-Path $projectRoot "tests\runtime\Trainer-Rematches.rb"
$trainerBattleTestPath = Join-Path $projectRoot "tests\runtime\Trainer-Battles.rb"
$difficultyScalingTestPath = Join-Path $projectRoot "tests\runtime\Difficulty-Scaling.rb"
$newPlayerProtectionTestPath = Join-Path $projectRoot "tests\runtime\New-Player-Protections.rb"
$healingNpcTestPath = Join-Path $projectRoot "tests\runtime\Healing-NPCs.rb"
$wallyTutorialTestPath = Join-Path $projectRoot "tests\runtime\Wally-Tutorial.rb"
$repelOverlayTestPath = Join-Path $projectRoot "tests\runtime\Repel-Overlay.rb"
$itemRandomizationTestPath = Join-Path $projectRoot "tests\runtime\Item-Randomization.rb"
$seededRunImportTestPath = Join-Path $projectRoot "tests\runtime\Seeded-Run-Import.rb"
$runTransitionTestPath = Join-Path $projectRoot "tests\runtime\Run-Transitions.rb"
$earlyGameTestPath = Join-Path $projectRoot "tests\runtime\Early-Game.rb"
$runtimeHookTestPath = Join-Path $projectRoot "tests\runtime\Runtime-Hooks.rb"
$wildEncounterFusionTestPath = Join-Path $projectRoot "tests\runtime\Wild-Encounter-Fusions.rb"
$deterministicHashingTestPath = Join-Path $projectRoot "tests\runtime\Deterministic-Hashing.rb"
$generatorMetadataTestPath = Join-Path $projectRoot "tests\runtime\Generator-Metadata.rb"
$evolutionUpwardExpansionTestPath = Join-Path $projectRoot "tests\runtime\Evolution-Upward-Expansion.rb"
$fusionPredecessorBenchmarkPath = Join-Path $projectRoot "tests\runtime\Fusion-Predecessor-Benchmark.rb"
$moveAccessStructureTestPath = Join-Path $projectRoot "tests\runtime\Move-Access-Structure.rb"
$trackerStructureTestPath = Join-Path $projectRoot "tests\runtime\Tracker-Structure.rb"
$diagnosticResultPath = Join-Path $projectRoot "runtime-diagnostic-access.tests"
$catchAssistanceResultPath = Join-Path $projectRoot "runtime-catch-assistance.tests"
$battleItemResultPath = Join-Path $projectRoot "runtime-battle-items.tests"
$battleRunHotkeyResultPath = Join-Path $projectRoot "runtime-battle-run-hotkey.tests"
$battleMoveTypeColorResultPath = Join-Path $projectRoot "runtime-battle-move-type-colors.tests"
$movePowerPresentationResultPath = Join-Path $projectRoot "runtime-move-power-presentation.tests"
$trainerRematchResultPath = Join-Path $projectRoot "runtime-trainer-rematches.tests"
$trainerBattleResultPath = Join-Path $projectRoot "runtime-trainer-battles.tests"
$difficultyScalingResultPath = Join-Path $projectRoot "runtime-difficulty-scaling.tests"
$newPlayerProtectionResultPath = Join-Path $projectRoot "runtime-new-player-protections.tests"
$healingNpcResultPath = Join-Path $projectRoot "runtime-healing-npcs.tests"
$wallyTutorialResultPath = Join-Path $projectRoot "runtime-wally-tutorial.tests"
$repelOverlayResultPath = Join-Path $projectRoot "runtime-repel-overlay.tests"
$itemRandomizationResultPath = Join-Path $projectRoot "runtime-item-randomization.tests"
$seededRunImportResultPath = Join-Path $projectRoot "runtime-seeded-run-import.tests"
$runTransitionResultPath = Join-Path $projectRoot "runtime-run-transitions.tests"
$earlyGameResultPath = Join-Path $projectRoot "runtime-early-game.tests"
$runtimeHookResultPath = Join-Path $projectRoot "runtime-hooks.tests"
$wildEncounterFusionResultPath = Join-Path $projectRoot "runtime-wild-encounter-fusions.tests"
$deterministicHashingResultPath = Join-Path $projectRoot "runtime-deterministic-hashing.tests"
$generatorMetadataResultPath = Join-Path $projectRoot "runtime-generator-metadata.tests"
$evolutionUpwardExpansionResultPath = Join-Path $projectRoot "runtime-evolution-upward-expansion.tests"
$fusionPredecessorBenchmarkResultPath = Join-Path $projectRoot "runtime-fusion-predecessor.benchmark"
$moveAccessStructureResultPath = Join-Path $projectRoot "runtime-move-access-structure.tests"
$trackerStructureResultPath = Join-Path $projectRoot "runtime-tracker-structure.tests"
$diagnosticErrorPath = Join-Path $projectRoot "runtime-diagnostic-access.error"
$evolutionPredecessorDiagnostic = $null
$fusionPredecessorBenchmarkOutput = @()

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
$battleRunHotkeyTestSource = [IO.File]::ReadAllText(
    $battleRunHotkeyTestPath,
    [Text.Encoding]::UTF8
)
$battleMoveTypeColorTestSource = [IO.File]::ReadAllText(
    $battleMoveTypeColorTestPath,
    [Text.Encoding]::UTF8
)
$movePowerPresentationTestSource = [IO.File]::ReadAllText(
    $movePowerPresentationTestPath,
    [Text.Encoding]::UTF8
)
$trainerRematchTestSource = [IO.File]::ReadAllText(
    $trainerRematchTestPath,
    [Text.Encoding]::UTF8
)
$trainerBattleTestSource = [IO.File]::ReadAllText(
    $trainerBattleTestPath,
    [Text.Encoding]::UTF8
)
$difficultyScalingTestSource = [IO.File]::ReadAllText(
    $difficultyScalingTestPath,
    [Text.Encoding]::UTF8
)
$newPlayerProtectionTestSource = [IO.File]::ReadAllText(
    $newPlayerProtectionTestPath,
    [Text.Encoding]::UTF8
)
$healingNpcTestSource = [IO.File]::ReadAllText(
    $healingNpcTestPath,
    [Text.Encoding]::UTF8
)
$wallyTutorialTestSource = [IO.File]::ReadAllText(
    $wallyTutorialTestPath,
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
$runTransitionTestSource = [IO.File]::ReadAllText(
    $runTransitionTestPath,
    [Text.Encoding]::UTF8
)
$earlyGameTestSource = [IO.File]::ReadAllText(
    $earlyGameTestPath,
    [Text.Encoding]::UTF8
)
$runtimeHookTestSource = [IO.File]::ReadAllText(
    $runtimeHookTestPath,
    [Text.Encoding]::UTF8
)
$wildEncounterFusionTestSource = [IO.File]::ReadAllText(
    $wildEncounterFusionTestPath,
    [Text.Encoding]::UTF8
)
$deterministicHashingTestSource = [IO.File]::ReadAllText(
    $deterministicHashingTestPath,
    [Text.Encoding]::UTF8
)
$generatorMetadataTestSource = [IO.File]::ReadAllText(
    $generatorMetadataTestPath,
    [Text.Encoding]::UTF8
)
$evolutionUpwardExpansionTestSource = [IO.File]::ReadAllText(
    $evolutionUpwardExpansionTestPath,
    [Text.Encoding]::UTF8
)
$fusionPredecessorBenchmarkSource = [IO.File]::ReadAllText(
    $fusionPredecessorBenchmarkPath,
    [Text.Encoding]::UTF8
)
$moveAccessStructureTestSource = [IO.File]::ReadAllText(
    $moveAccessStructureTestPath,
    [Text.Encoding]::UTF8
)
$trackerStructureTestSource = [IO.File]::ReadAllText(
    $trackerStructureTestPath,
    [Text.Encoding]::UTF8
)
$rubyResultPath = $diagnosticResultPath.Replace('\', '/')
$rubyCatchAssistanceResultPath = $catchAssistanceResultPath.Replace('\', '/')
$rubyBattleItemResultPath = $battleItemResultPath.Replace('\', '/')
$rubyBattleRunHotkeyResultPath = $battleRunHotkeyResultPath.Replace('\', '/')
$rubyBattleMoveTypeColorResultPath = $battleMoveTypeColorResultPath.Replace('\', '/')
$rubyMovePowerPresentationResultPath = $movePowerPresentationResultPath.Replace('\', '/')
$rubyTrainerRematchResultPath = $trainerRematchResultPath.Replace('\', '/')
$rubyTrainerBattleResultPath = $trainerBattleResultPath.Replace('\', '/')
$rubyDifficultyScalingResultPath = $difficultyScalingResultPath.Replace('\', '/')
$rubyNewPlayerProtectionResultPath = $newPlayerProtectionResultPath.Replace('\', '/')
$rubyHealingNpcResultPath = $healingNpcResultPath.Replace('\', '/')
$rubyWallyTutorialResultPath = $wallyTutorialResultPath.Replace('\', '/')
$rubyRepelOverlayResultPath = $repelOverlayResultPath.Replace('\', '/')
$rubyItemRandomizationResultPath = $itemRandomizationResultPath.Replace('\', '/')
$rubySeededRunImportResultPath = $seededRunImportResultPath.Replace('\', '/')
$rubyRunTransitionResultPath = $runTransitionResultPath.Replace('\', '/')
$rubyEarlyGameResultPath = $earlyGameResultPath.Replace('\', '/')
$rubyRuntimeHookResultPath = $runtimeHookResultPath.Replace('\', '/')
$rubyWildEncounterFusionResultPath = $wildEncounterFusionResultPath.Replace('\', '/')
$rubyDeterministicHashingResultPath = $deterministicHashingResultPath.Replace('\', '/')
$rubyGeneratorMetadataResultPath = $generatorMetadataResultPath.Replace('\', '/')
$rubyEvolutionUpwardExpansionResultPath = $evolutionUpwardExpansionResultPath.Replace('\', '/')
$rubyFusionPredecessorBenchmarkResultPath = $fusionPredecessorBenchmarkResultPath.Replace('\', '/')
$rubyMoveAccessStructureResultPath = $moveAccessStructureResultPath.Replace('\', '/')
$rubyTrackerStructureResultPath = $trackerStructureResultPath.Replace('\', '/')
$bootstrapSource = @(
    "begin"
    "`$ironmon_diagnostic_access_test_output_path = `"$rubyResultPath`""
    "`$ironmon_catch_assistance_test_output_path = `"$rubyCatchAssistanceResultPath`""
    "`$ironmon_battle_item_test_output_path = `"$rubyBattleItemResultPath`""
    "`$ironmon_battle_run_hotkey_test_output_path = `"$rubyBattleRunHotkeyResultPath`""
    "`$ironmon_battle_move_type_color_test_output_path = `"$rubyBattleMoveTypeColorResultPath`""
    "`$ironmon_move_power_presentation_test_output_path = `"$rubyMovePowerPresentationResultPath`""
    "`$ironmon_trainer_rematch_test_output_path = `"$rubyTrainerRematchResultPath`""
    "`$ironmon_trainer_battle_test_output_path = `"$rubyTrainerBattleResultPath`""
    "`$ironmon_difficulty_scaling_test_output_path = `"$rubyDifficultyScalingResultPath`""
    "`$ironmon_new_player_protection_test_output_path = `"$rubyNewPlayerProtectionResultPath`""
    "`$ironmon_healing_npc_test_output_path = `"$rubyHealingNpcResultPath`""
    "`$ironmon_wally_tutorial_test_output_path = `"$rubyWallyTutorialResultPath`""
    "`$ironmon_repel_overlay_test_output_path = `"$rubyRepelOverlayResultPath`""
    "`$ironmon_item_randomization_test_output_path = `"$rubyItemRandomizationResultPath`""
    "`$ironmon_seeded_run_import_test_output_path = `"$rubySeededRunImportResultPath`""
    "`$ironmon_run_transition_test_output_path = `"$rubyRunTransitionResultPath`""
    "`$ironmon_early_game_test_output_path = `"$rubyEarlyGameResultPath`""
    "`$ironmon_runtime_hook_test_output_path = `"$rubyRuntimeHookResultPath`""
    "`$ironmon_wild_encounter_fusion_test_output_path = `"$rubyWildEncounterFusionResultPath`""
    "`$ironmon_deterministic_hashing_test_output_path = `"$rubyDeterministicHashingResultPath`""
    "`$ironmon_generator_metadata_test_output_path = `"$rubyGeneratorMetadataResultPath`""
    "`$ironmon_evolution_upward_expansion_test_output_path = `"$rubyEvolutionUpwardExpansionResultPath`""
    "`$ironmon_fusion_predecessor_benchmark_output_path = `"$rubyFusionPredecessorBenchmarkResultPath`""
    "`$ironmon_run_fusion_predecessor_benchmark = $($BenchmarkFusionPredecessors.IsPresent.ToString().ToLowerInvariant())"
    "`$ironmon_move_access_structure_test_output_path = `"$rubyMoveAccessStructureResultPath`""
    "`$ironmon_tracker_structure_test_output_path = `"$rubyTrackerStructureResultPath`""
    "Dir.chdir(`"$($resolvedGameRoot.Replace('\', '/'))`")"
    $scriptLoaderSource
    "IronmonScriptLoader.load_directory(`"Data/Scripts`", [/\A(?:998|999)/])"
    "GameData.load_all"
    $catchAssistanceTestSource
    $battleItemTestSource
    $battleRunHotkeyTestSource
    $battleMoveTypeColorTestSource
    $movePowerPresentationTestSource
    $trainerRematchTestSource
    $trainerBattleTestSource
    $difficultyScalingTestSource
    $newPlayerProtectionTestSource
    $healingNpcTestSource
    $wallyTutorialTestSource
    $repelOverlayTestSource
    $itemRandomizationTestSource
    $seededRunImportTestSource
    $runTransitionTestSource
    $earlyGameTestSource
    $runtimeHookTestSource
    $wildEncounterFusionTestSource
    $deterministicHashingTestSource
    $generatorMetadataTestSource
    $evolutionUpwardExpansionTestSource
    $fusionPredecessorBenchmarkSource
    $moveAccessStructureTestSource
    $trackerStructureTestSource
    $diagnosticTestSource
    "exit! 0"
    "rescue Exception => error"
    "backtrace = error.backtrace ? error.backtrace.join(`"\n`") : `"`""
    "File.binwrite(`"$rubyResultPath`", `"#{error.class}: #{error.message}\n#{backtrace}`")"
    "exit! 1"
    "end"
) -join "`n"
Remove-Item -LiteralPath $diagnosticResultPath, $catchAssistanceResultPath, $battleItemResultPath, $battleRunHotkeyResultPath, $battleMoveTypeColorResultPath, $movePowerPresentationResultPath, $trainerRematchResultPath, $trainerBattleResultPath, $difficultyScalingResultPath, $newPlayerProtectionResultPath, $healingNpcResultPath, $wallyTutorialResultPath, $repelOverlayResultPath, $itemRandomizationResultPath, $seededRunImportResultPath, $runTransitionResultPath, $earlyGameResultPath, $runtimeHookResultPath, $wildEncounterFusionResultPath, $deterministicHashingResultPath, $generatorMetadataResultPath, $evolutionUpwardExpansionResultPath, $fusionPredecessorBenchmarkResultPath, $moveAccessStructureResultPath, $trackerStructureResultPath, $diagnosticErrorPath `
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
    if (-not (Test-Path -LiteralPath $battleRunHotkeyResultPath) -or
        (Get-Content -LiteralPath $battleRunHotkeyResultPath -Raw).Trim() -ne
            "battle Run hotkey runtime tests passed") {
        throw "The bundled runtime did not complete the battle Run hotkey tests."
    }
    if (-not (Test-Path -LiteralPath $battleMoveTypeColorResultPath) -or
        (Get-Content -LiteralPath $battleMoveTypeColorResultPath -Raw).Trim() -ne
            "battle move type color runtime tests passed") {
        throw "The bundled runtime did not complete the battle move type color tests."
    }
    if (-not (Test-Path -LiteralPath $movePowerPresentationResultPath) -or
        (Get-Content -LiteralPath $movePowerPresentationResultPath -Raw).Trim() -ne
            "move-power presentation runtime tests passed") {
        throw "The bundled runtime did not complete the move-power presentation tests."
    }
    if (-not (Test-Path -LiteralPath $trainerRematchResultPath) -or
        (Get-Content -LiteralPath $trainerRematchResultPath -Raw).Trim() -ne
            "trainer rematch runtime tests passed") {
        throw "The bundled runtime did not complete the trainer rematch tests."
    }
    if (-not (Test-Path -LiteralPath $trainerBattleResultPath) -or
        (Get-Content -LiteralPath $trainerBattleResultPath -Raw).Trim() -ne
            "trainer battle runtime tests passed") {
        throw "The bundled runtime did not complete the trainer battle tests."
    }
    if (-not (Test-Path -LiteralPath $difficultyScalingResultPath) -or
        (Get-Content -LiteralPath $difficultyScalingResultPath -Raw).Trim() -ne
            "difficulty scaling runtime tests passed") {
        throw "The bundled runtime did not complete the difficulty scaling tests."
    }
    if (-not (Test-Path -LiteralPath $newPlayerProtectionResultPath) -or
        (Get-Content -LiteralPath $newPlayerProtectionResultPath -Raw).Trim() -ne
            "new-player protection runtime tests passed") {
        throw "The bundled runtime did not complete the new-player protection tests."
    }
    if (-not (Test-Path -LiteralPath $healingNpcResultPath) -or
        (Get-Content -LiteralPath $healingNpcResultPath -Raw).Trim() -ne
            "healing NPC runtime tests passed") {
        throw "The bundled runtime did not complete the healing NPC tests."
    }
    if (-not (Test-Path -LiteralPath $wallyTutorialResultPath) -or
        (Get-Content -LiteralPath $wallyTutorialResultPath -Raw).Trim() -ne
            "Wally tutorial runtime tests passed") {
        throw "The bundled runtime did not complete the Wally tutorial tests."
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
    $seededRunImportResults = @(
        if (Test-Path -LiteralPath $seededRunImportResultPath) {
            Get-Content -LiteralPath $seededRunImportResultPath
        }
    )
    $playerFusionPairingMetric = $seededRunImportResults | Where-Object {
        $_ -match '^player_fusion_pairing_milliseconds=\d+$'
    } | Select-Object -First 1
    if ($seededRunImportResults.Count -lt 2 -or
        $seededRunImportResults[0] -ne
            "seeded-run import runtime tests passed" -or
        -not $playerFusionPairingMetric) {
        throw "The bundled runtime did not complete the seeded-run import tests."
    }
    $playerFusionPairingMilliseconds = [int](
        $playerFusionPairingMetric.Split('=', 2)[1]
    )
    Write-Output (
        "Bundled-runtime global player-fusion pairing: " +
        "$playerFusionPairingMilliseconds ms."
    )
    if (-not (Test-Path -LiteralPath $runTransitionResultPath) -or
        (Get-Content -LiteralPath $runTransitionResultPath -Raw).Trim() -ne
            "run-transition runtime tests passed") {
        throw "The bundled runtime did not complete the run-transition tests."
    }
    if (-not (Test-Path -LiteralPath $earlyGameResultPath) -or
        (Get-Content -LiteralPath $earlyGameResultPath -Raw).Trim() -ne
            "early-game runtime tests passed") {
        throw "The bundled runtime did not complete the early-game tests."
    }
    if (-not (Test-Path -LiteralPath $runtimeHookResultPath) -or
        (Get-Content -LiteralPath $runtimeHookResultPath -Raw).Trim() -ne
            "runtime-hook tests passed") {
        throw "The bundled runtime did not complete the runtime-hook tests."
    }
    if (-not (Test-Path -LiteralPath $wildEncounterFusionResultPath) -or
        (Get-Content -LiteralPath $wildEncounterFusionResultPath -Raw).Trim() -ne
            "wild encounter fusion runtime tests passed") {
        throw "The bundled runtime did not complete the wild encounter fusion tests."
    }
    if (-not (Test-Path -LiteralPath $deterministicHashingResultPath) -or
        (Get-Content -LiteralPath $deterministicHashingResultPath -Raw).Trim() -ne
            "deterministic-hashing tests passed") {
        throw "The bundled runtime did not complete the deterministic-hashing tests."
    }
    if (-not (Test-Path -LiteralPath $generatorMetadataResultPath) -or
        (Get-Content -LiteralPath $generatorMetadataResultPath -Raw).Trim() -ne
            "generator-metadata tests passed") {
        throw "The bundled runtime did not complete the generator-metadata tests."
    }
    $evolutionUpwardExpansionOutput = if (
        Test-Path -LiteralPath $evolutionUpwardExpansionResultPath
    ) {
        @(Get-Content -LiteralPath $evolutionUpwardExpansionResultPath)
    } else {
        @()
    }
    if ($evolutionUpwardExpansionOutput.Count -lt 1 -or
        $evolutionUpwardExpansionOutput[0] -ne
            "evolution upward-expansion tests passed") {
        throw "The bundled runtime did not complete the evolution upward-expansion tests."
    }
    if ($evolutionUpwardExpansionOutput.Count -gt 1) {
        $evolutionPredecessorDiagnostic = $evolutionUpwardExpansionOutput[1]
    }
    if ($BenchmarkFusionPredecessors) {
        if (-not (Test-Path -LiteralPath $fusionPredecessorBenchmarkResultPath)) {
            throw "The bundled runtime did not complete the fusion predecessor benchmark."
        }
        $fusionPredecessorBenchmarkOutput = @(
            Get-Content -LiteralPath $fusionPredecessorBenchmarkResultPath
        )
        if ($fusionPredecessorBenchmarkOutput.Count -lt 1 -or
            $fusionPredecessorBenchmarkOutput[0] -ne
                "fusion predecessor benchmark passed") {
            throw "The bundled runtime did not complete the fusion predecessor benchmark."
        }
    }
    if (-not (Test-Path -LiteralPath $moveAccessStructureResultPath) -or
        (Get-Content -LiteralPath $moveAccessStructureResultPath -Raw).Trim() -ne
            "move-access structure tests passed") {
        throw "The bundled runtime did not complete the move-access structure tests."
    }
    $trackerStructureResults = @(
        if (Test-Path -LiteralPath $trackerStructureResultPath) {
            Get-Content -LiteralPath $trackerStructureResultPath
        }
    )
    $coldFusionSearchMetric = $trackerStructureResults | Where-Object {
        $_ -match '^cold_fusion_search_milliseconds=\d+$'
    } | Select-Object -First 1
    if ($trackerStructureResults.Count -lt 2 -or
        $trackerStructureResults[0] -ne "tracker structure tests passed" -or
        -not $coldFusionSearchMetric) {
        throw "The bundled runtime did not complete the tracker structure tests."
    }
    $coldFusionSearchMilliseconds = [int](
        $coldFusionSearchMetric.Split('=', 2)[1]
    )
    Write-Output (
        "Bundled-runtime cold full-pool Pokemon search: " +
        "$coldFusionSearchMilliseconds ms."
    )
}
finally {
    Remove-Item -LiteralPath $diagnosticResultPath, $catchAssistanceResultPath, $battleItemResultPath, $battleRunHotkeyResultPath, $movePowerPresentationResultPath, $battleMoveTypeColorResultPath, $trainerRematchResultPath, $trainerBattleResultPath, $difficultyScalingResultPath, $newPlayerProtectionResultPath, $healingNpcResultPath, $wallyTutorialResultPath, $repelOverlayResultPath, $itemRandomizationResultPath, $seededRunImportResultPath, $runTransitionResultPath, $earlyGameResultPath, $runtimeHookResultPath, $wildEncounterFusionResultPath, $deterministicHashingResultPath, $generatorMetadataResultPath, $evolutionUpwardExpansionResultPath, $fusionPredecessorBenchmarkResultPath, $moveAccessStructureResultPath, $trackerStructureResultPath, $diagnosticErrorPath, $debugPokemonSearchTracePath, $playerFusionPreparationTracePath `
        -Force `
        -ErrorAction SilentlyContinue
}

if ($evolutionPredecessorDiagnostic) {
    Write-Output "Fusion predecessor prototype: $evolutionPredecessorDiagnostic"
}
if ($fusionPredecessorBenchmarkOutput.Count -gt 1) {
    $fusionPredecessorBenchmarkOutput | Select-Object -Skip 1
}
Write-Output "Bundled-runtime area progress, diagnostic access, catch assistance, battle item, battle Run hotkey, battle move type color, move-power presentation, trainer rematch, trainer battle, difficulty scaling, new-player protection, healing NPC, Wally tutorial, Repel overlay, item randomization, seeded-run import, run-transition, early-game, runtime-hook, wild encounter fusion, deterministic-hashing, generator-metadata, evolution upward-expansion, move-access structure, and tracker structure tests passed."
