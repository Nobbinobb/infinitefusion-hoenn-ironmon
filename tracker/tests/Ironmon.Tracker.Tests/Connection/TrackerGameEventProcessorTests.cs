namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies focused inbound game-state and event processing.
/// </summary>
public sealed class TrackerGameEventProcessorTests
{
    private const string _preparedRunId = "prepared-run";
    private const string _testGenerationProfileId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    /// <summary>
    /// Initializes inbound game event processor tests.
    /// </summary>
    public TrackerGameEventProcessorTests()
    {
    }

    /// <summary>
    /// Verifies recovery and live events update their owning stores without socket lifecycle involvement.
    /// </summary>
    [Fact]
    public async Task ProcessorRecoversAndRoutesLiveState()
    {
        string root = Path.Combine(Path.GetTempPath(), "IronmonTrackerTests", Guid.NewGuid().ToString("N"));
        try
        {
            TrackerKnowledgeOptions storage = new(root);
            TrackerDiagnosticsStore diagnostics = new();
            TrackerConnectionState connectionState = new();
            TrackerRunState runState = new();
            TrackerKnowledgeStore knowledge = new(storage);
            AreaDiscoveryStore areaDiscoveries = new(storage);
            CompletedRunArchive completedRuns = new(storage);
            TrackerConnectionOptions options = new(0, "0.7.8", false, TimeSpan.FromSeconds(2));
            using TrackerRequestSession requestSession = new(diagnostics);
            TrackerRequestClient requestClient = new(requestSession, options, connectionState, areaDiscoveries);
            TrackerGameEventProcessor processor = new(connectionState, runState, knowledge, areaDiscoveries, completedRuns, requestSession, requestClient);
            GameHandshakePayload game = new("6.8.0", "0.7.8", true, false, TrackerTestPaths.GameRoot, "run-1", "battle-1");
            ObservedMoveSnapshot move = new()
            {
                Id = "ABSORB",
                Name = "Absorb",
                Source = "unknown",
                Origin = "enemy_use",
                Type = "GRASS",
                Power = 20,
                Accuracy = 100,
                TotalPp = 25,
                PpAfterUse = 24
            };

            EnemyPokemonSnapshot enemy = new()
            {
                EnemyId = "enemy-1",
                Position = 1,
                SpeciesId = "BELLOSSOM:0",
                SpeciesName = "Bellossom",
                Level = 12,
                LastMove = move
            };

            GameCurrentStatePayload recovered = new(true, "run-1", "battle-1", 4, new BattleSnapshot { BattleId = "battle-1" }, enemies: [enemy]);
            requestClient.ObtainabilityProgress.Begin("previous-run", TrackerObtainabilityProgressScope.ActiveRun);
            processor.RecoverCurrentState(game, recovered);

            Assert.Same(recovered, connectionState.Snapshot.CurrentState);
            Assert.Same(TrackerObtainabilityProgressSnapshot.Idle, requestClient.ObtainabilityProgress.Snapshot);
            Assert.Same(enemy, Assert.Single(runState.Snapshot.Enemies));
            Assert.Equal(12, knowledge.GetHighestLevel("BELLOSSOM:0"));
            Assert.Equal("ABSORB", Assert.Single(knowledge.GetDisplayedMoves("BELLOSSOM:0", 12)).Id);

            requestClient.ObtainabilityProgress.Begin(recovered.RunId, TrackerObtainabilityProgressScope.ActiveRun);
            requestClient.ObtainabilityProgress.Report(recovered.RunId, TrackerObtainabilityProgressScope.ActiveRun, new PokemonObtainabilityResponsePayload { BackgroundComplete = true });
            processor.RecoverCurrentState(game, recovered);
            Assert.Equal(TrackerObtainabilityProgressStatus.Complete, requestClient.ObtainabilityProgress.GetActiveRunSnapshot(recovered.RunId).Status);
            processor.RecoverCurrentState(game, new GameCurrentStatePayload(true, _preparedRunId, null, 4));
            Assert.Same(TrackerObtainabilityProgressSnapshot.Idle, requestClient.ObtainabilityProgress.GetActiveRunSnapshot(_preparedRunId));
            Assert.Same(TrackerObtainabilityProgressSnapshot.Idle, requestClient.ObtainabilityProgress.GetActiveRunSnapshot(recovered.RunId));
            processor.RecoverCurrentState(game, recovered);

            FusionAssignmentRecipePayload assignmentRecipe = new()
            {
                SourceFingerprint = string.Empty,
                TaxonomyFingerprint = string.Empty,
                MethodFingerprint = string.Empty,
                BaseStatSourceFingerprint = string.Empty,
                TargetPoolFingerprint = string.Empty
            };
            GameCurrentStatePayload prematureAssignments = new(true, game.RunId, null, 4, fusionAssignments: assignmentRecipe);
            GameCurrentStatePayload readyAssignments = new(true, game.RunId, null, 4, fusionAssignments: assignmentRecipe, activeRunPreparationReady: true);
            Assert.False(TrackerRequestClient.IsActiveFusionAssignmentPreparationEligible(prematureAssignments));
            Assert.True(TrackerRequestClient.IsActiveFusionAssignmentPreparationEligible(readyAssignments));

            SeededRunImportStatusPayload? publishedStatus = null;
            requestClient.SeededRunImportStatusChanged += status => publishedStatus = status;
            SeededRunImportStatusPayload status = new() { TokenId = "token-1", Status = SeededRunImportStatus.Queued, Message = "Queued" };
            await processor.ProcessAsync(TrackerMessageFactory.CreateEvent(TrackerEvents.SeededRunImportStatus, 5, status, "run-1"), game, CancellationToken.None);
            Assert.Equal(status.TokenId, publishedStatus?.TokenId);

            int completedRunSelections = 0;
            completedRuns.SelectionRequested += (_, _) => completedRunSelections++;
            CompletedRunRecipePayload completedRecipe = CreateCompletedRecipe("run-1");
            RunCompletedEventPayload completion = new() { Recipe = completedRecipe, RequestArchiveSelection = false };
            await processor.ProcessAsync(TrackerMessageFactory.CreateEvent(TrackerEvents.RunCompleted, 6, completion, "run-1"), game, CancellationToken.None);
            Assert.Equal("run-1", Assert.Single(completedRuns.Recipes).RunId);
            Assert.Equal(0, completedRunSelections);
            Assert.Equal(completedRecipe.Statistics?.SaveSlot, connectionState.Snapshot.CurrentState?.AttemptStatistics?.SaveSlot);
            Assert.Equal(1, connectionState.Snapshot.CurrentState?.AttemptStatistics?.AttemptsLost);

            GameCurrentStatePayload preparationReady = new(true, "run-1", null, 7, activeRunPreparationReady: true);
            await processor.ProcessAsync(TrackerMessageFactory.CreateEvent(TrackerEvents.ActiveRunPreparationReady, 7, preparationReady, "run-1"), game, CancellationToken.None);
            Assert.True(connectionState.Snapshot.CurrentState?.ActiveRunPreparationReady);

            EnemyAbilityRevealedPayload ability = new()
            {
                EnemyId = "enemy-1",
                SpeciesId = "BELLOSSOM:0",
                Ability = new AbilitySnapshot { Id = "CHLOROPHYLL", Name = "Chlorophyll", Description = "Boosts Speed in sunshine." }
            };
            await processor.ProcessAsync(TrackerMessageFactory.CreateEvent(TrackerEvents.EnemyAbilityRevealed, 7, ability, "run-1"), game, CancellationToken.None);
            Assert.Equal("CHLOROPHYLL", Assert.Single(knowledge.GetAbilities("BELLOSSOM:0")).Id);

            BattleSnapshot activeBattle = new() { BattleId = "battle-1" };
            await processor.ProcessAsync(TrackerMessageFactory.CreateEvent(TrackerEvents.BattleStarted, 8, activeBattle, "run-1", "battle-1"), game, CancellationToken.None);

            PlayerMoveMenuOpenedPayload moveMenu = new() { PokemonId = "player-1" };
            await processor.ProcessAsync(TrackerMessageFactory.CreateEvent(TrackerEvents.PlayerMoveMenuOpened, 9, moveMenu, "run-1", "battle-1"), game, CancellationToken.None);
            Assert.Equal("player-1", runState.Snapshot.MoveMenuPokemonId);

            PlayerTargetChangedPayload target = new() { Position = 3 };
            await processor.ProcessAsync(TrackerMessageFactory.CreateEvent(TrackerEvents.PlayerTargetChanged, 10, target, "run-1", "battle-1"), game, CancellationToken.None);
            Assert.Equal(3, runState.Snapshot.Battle?.SelectedTargetPosition);

            await processor.ProcessAsync(TrackerMessageFactory.CreateEvent(TrackerEvents.PlayerTargetChanged, 11, new PlayerTargetChangedPayload(), "run-1", "battle-1"), game, CancellationToken.None);
            Assert.Null(runState.Snapshot.Battle?.SelectedTargetPosition);

            await processor.ProcessAsync(TrackerMessageFactory.CreateEvent(TrackerEvents.EnemyStateChanged, 12, enemy, "run-1", "battle-1"), game, CancellationToken.None);
            Assert.Equal("player-1", runState.Snapshot.MoveMenuPokemonId);
            EnemyPokemonSnapshot replacement = new()
            {
                EnemyId = "enemy-2",
                Position = 1,
                SpeciesId = "VILEPLUME:0",
                SpeciesName = "Vileplume",
                Level = 14
            };

            await processor.ProcessAsync(TrackerMessageFactory.CreateEvent(TrackerEvents.EnemySentOut, 13, replacement, "run-1", "battle-1"), game, CancellationToken.None);
            Assert.Null(runState.Snapshot.MoveMenuPokemonId);
            Assert.Equal("enemy-2", Assert.Single(runState.Snapshot.Enemies).EnemyId);

            await processor.ProcessAsync(TrackerMessageFactory.CreateEvent(TrackerEvents.PlayerMoveMenuOpened, 14, moveMenu, "run-1", "battle-1"), game, CancellationToken.None);
            Assert.Equal("player-1", runState.Snapshot.MoveMenuPokemonId);

            Dictionary<string, object?> requestPayload = [];
            TrackerMessage request = TrackerMessageFactory.CreateRequest("request-1", TrackerCommands.CurrentState, requestPayload, "run-1");
            await Assert.ThrowsAsync<ArgumentException>(() => processor.ProcessAsync(request, game, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Creates a minimal valid completed-run recipe for event routing.
    /// </summary>
    /// <param name="runId">The completed run identifier.</param>
    /// <returns>The completed-run recipe.</returns>
    private static CompletedRunRecipePayload CreateCompletedRecipe(string runId) => new()
    {
        SchemaVersion = 1,
        RunId = runId,
        Seed = 123,
        Result = "lost",
        GenerationProfileId = _testGenerationProfileId,
        GameVersion = "6.8.0",
        IronmonVersion = "0.7.8",
        Configuration = new RunConfigurationPayload { SchemaVersion = 1, WildPolicy = "mixed", TrainerPolicy = "mixed", UnfusionSetting = "random_component" },
        SpeciesGenerator = new SpeciesGeneratorRecipePayload { Version = 1, PoolFingerprint = "species" },
        AbilityGenerator = new AbilityGeneratorRecipePayload { Version = 1, PoolSize = 100, PoolFingerprint = "abilities" },
        BaseStatGenerator = new BaseStatGeneratorRecipePayload { Version = 1, SourceFingerprint = "base-stats" },
        PlayerFusionGenerator = new PlayerFusionGeneratorRecipePayload { Version = 1, PoolSize = 100, PoolFingerprint = "fusions" },
        ItemGenerator = new ItemGeneratorRecipePayload
        {
            Version = 1,
            RulesVersion = 1,
            GroundPoolSize = 600,
            GroundTotalWeight = 600,
            GroundPoolFingerprint = "ground-items",
            TmPoolSize = 124,
            TmPoolFingerprint = "tm-items",
            ResultBans = ["DNASPLICERS"],
            ResultBanFingerprint = "item-bans",
            ShopPolicyVersion = 1
        },
        Statistics = new RunStatisticsPayload
        {
            SchemaVersion = 1,
            AttemptNumber = 1,
            SaveSlot = "File A",
            Result = "lost",
            AttemptsStarted = 1,
            AttemptsLost = 1
        }
    };
}
