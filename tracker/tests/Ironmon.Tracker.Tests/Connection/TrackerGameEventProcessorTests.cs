namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies focused inbound game-state and event processing.
/// </summary>
public sealed class TrackerGameEventProcessorTests
{
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
            GameHandshakePayload game = new("6.8.0", "0.7.8", true, false, @"C:\Game", "run-1", "battle-1");
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
            processor.RecoverCurrentState(game, recovered);

            Assert.Same(recovered, connectionState.Snapshot.CurrentState);
            Assert.Same(enemy, Assert.Single(runState.Snapshot.Enemies));
            Assert.Equal(12, knowledge.GetHighestLevel("BELLOSSOM:0"));
            Assert.Equal("ABSORB", Assert.Single(knowledge.GetDisplayedMoves("BELLOSSOM:0", 12)).Id);

            SeededRunImportStatusPayload? publishedStatus = null;
            requestClient.SeededRunImportStatusChanged += status => publishedStatus = status;
            SeededRunImportStatusPayload status = new() { TokenId = "token-1", Status = SeededRunImportStatus.Queued, Message = "Queued" };
            await processor.ProcessAsync(TrackerMessageFactory.CreateEvent(TrackerEvents.SeededRunImportStatus, 5, status, "run-1"), game, CancellationToken.None);
            Assert.Equal(status.TokenId, publishedStatus?.TokenId);

            EnemyAbilityRevealedPayload ability = new()
            {
                EnemyId = "enemy-1",
                SpeciesId = "BELLOSSOM:0",
                Ability = new AbilitySnapshot { Id = "CHLOROPHYLL", Name = "Chlorophyll", Description = "Boosts Speed in sunshine." }
            };
            await processor.ProcessAsync(TrackerMessageFactory.CreateEvent(TrackerEvents.EnemyAbilityRevealed, 6, ability, "run-1"), game, CancellationToken.None);
            Assert.Equal("CHLOROPHYLL", Assert.Single(knowledge.GetAbilities("BELLOSSOM:0")).Id);

            PlayerMoveMenuOpenedPayload moveMenu = new() { PokemonId = "player-1" };
            await processor.ProcessAsync(TrackerMessageFactory.CreateEvent(TrackerEvents.PlayerMoveMenuOpened, 7, moveMenu, "run-1", "battle-1"), game, CancellationToken.None);
            Assert.Equal("player-1", runState.Snapshot.MoveMenuPokemonId);

            await processor.ProcessAsync(TrackerMessageFactory.CreateEvent(TrackerEvents.EnemyStateChanged, 8, enemy, "run-1", "battle-1"), game, CancellationToken.None);
            Assert.Equal("player-1", runState.Snapshot.MoveMenuPokemonId);
            EnemyPokemonSnapshot replacement = new()
            {
                EnemyId = "enemy-2",
                Position = 1,
                SpeciesId = "VILEPLUME:0",
                SpeciesName = "Vileplume",
                Level = 14
            };

            await processor.ProcessAsync(TrackerMessageFactory.CreateEvent(TrackerEvents.EnemySentOut, 9, replacement, "run-1", "battle-1"), game, CancellationToken.None);
            Assert.Null(runState.Snapshot.MoveMenuPokemonId);
            Assert.Equal("enemy-2", Assert.Single(runState.Snapshot.Enemies).EnemyId);

            await processor.ProcessAsync(TrackerMessageFactory.CreateEvent(TrackerEvents.PlayerMoveMenuOpened, 10, moveMenu, "run-1", "battle-1"), game, CancellationToken.None);
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
}
