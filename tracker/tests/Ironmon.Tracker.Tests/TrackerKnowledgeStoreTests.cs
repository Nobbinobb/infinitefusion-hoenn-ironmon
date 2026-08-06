using Ironmon.Tracker.Connection;
using Ironmon.Tracker.Protocol;

namespace Ironmon.Tracker.Tests;

/// <summary>
/// Verifies tracker-owned move discovery and annotation persistence.
/// </summary>
public sealed class TrackerKnowledgeStoreTests
{
    /// <summary>
    /// Initializes tracker knowledge store tests.
    /// </summary>
    public TrackerKnowledgeStoreTests()
    {
    }

    /// <summary>
    /// Verifies newest-four projection, PP updates, player assistance, and reload.
    /// </summary>
    [Fact]
    public void StorePersistsApplicableRememberedMoves()
    {
        string root = CreateRoot();
        try
        {
            TrackerKnowledgeOptions options = new(root);
            TrackerKnowledgeStore store = new(options);
            store.SelectRun("run-knowledge");
            store.ObservePlayer(CreatePlayer([CreateMove("MOVE1", 1, 0), CreateMove("MOVE2", 5, 1)]));
            ObservedMoveSnapshot[] enemyMoves =
                [CreateMove("MOVE3", 10, 2), CreateMove("MOVE4", 15, 3), CreateMove("MOVE5", 20, 4)];
            foreach (ObservedMoveSnapshot move in enemyMoves)
                store.ObserveEnemyMove(CreateEnemyUse(move));

            ObservedMoveSnapshot repeated = CreateMove("MOVE5", 20, 4, 7);
            store.ObserveEnemyMove(CreateEnemyUse(repeated));

            IReadOnlyList<ObservedMoveSnapshot> displayed = store.GetDisplayedMoves("BELLOSSOM:0", 25);
            Assert.Equal(["MOVE2", "MOVE3", "MOVE4", "MOVE5"], displayed.Select(move => move.Id));
            Assert.Equal(7, displayed[^1].PpAfterUse);

            TrackerKnowledgeStore restored = new(options);
            restored.SelectRun("run-knowledge");
            Assert.Equal(displayed.Select(move => move.Id), restored.GetDisplayedMoves("BELLOSSOM:0", 25).Select(move => move.Id));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Verifies that a directly observed enemy move remains visible when its learn-level lookup is unavailable.
    /// </summary>
    [Fact]
    public void StoreDisplaysDirectlyObservedMoveWithoutLearnLevel()
    {
        string root = CreateRoot();
        try
        {
            TrackerKnowledgeStore store = new(new TrackerKnowledgeOptions(root));
            store.SelectRun("run-observed-move");
            ObservedMoveSnapshot move = CreateMove("UNKNOWN_SOURCE", 0, 0, 9, "unknown");

            store.ObserveEnemyMove(CreateEnemyUse(move));

            Assert.Equal("UNKNOWN_SOURCE", Assert.Single(store.GetDisplayedMoves("BELLOSSOM:0", 25)).Id);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Verifies annotation cycling in both directions and persistence.
    /// </summary>
    [Fact]
    public void StoreCyclesAndPersistsAnnotations()
    {
        string root = CreateRoot();
        try
        {
            TrackerKnowledgeOptions options = new(root);
            TrackerKnowledgeStore store = new(options);
            store.SelectRun("run-annotations");
            store.CycleAnnotation("BELLOSSOM:0", EnemyStat.Speed, true);
            Assert.Equal(EnemyStatAnnotation.Plus, store.GetAnnotation("BELLOSSOM:0", EnemyStat.Speed));
            store.CycleAnnotation("BELLOSSOM:0", EnemyStat.Speed, false);
            Assert.Equal(EnemyStatAnnotation.Empty, store.GetAnnotation("BELLOSSOM:0", EnemyStat.Speed));
            store.CycleAnnotation("BELLOSSOM:0", EnemyStat.Speed, false);

            TrackerKnowledgeStore restored = new(options);
            restored.SelectRun("run-annotations");
            Assert.Equal(EnemyStatAnnotation.Minus, restored.GetAnnotation("BELLOSSOM:0", EnemyStat.Speed));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Creates one isolated persistence root.
    /// </summary>
    /// <returns>The isolated directory path.</returns>
    private static string CreateRoot() => Path.Combine(Path.GetTempPath(), "IronmonTrackerTests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Creates a minimal player snapshot containing legal move discoveries.
    /// </summary>
    /// <param name="moves">The player-assisted move discoveries.</param>
    /// <returns>The player snapshot.</returns>
    private static PlayerPokemonSnapshot CreatePlayer(IReadOnlyList<ObservedMoveSnapshot> moves) => new()
    {
        PokemonId = "player-1",
        SpeciesId = "BELLOSSOM:0",
        Nickname = "Bellossom",
        SpeciesName = "Bellossom",
        Level = 25,
        Status = "NONE",
        Ability = "Chlorophyll",
        LevelUpMoves = moves,
        Healing = new HealingInventorySnapshot()
    };

    /// <summary>
    /// Creates one enemy move-use observation.
    /// </summary>
    /// <param name="move">The observed move.</param>
    /// <returns>The enemy move-use payload.</returns>
    private static EnemyMoveUsedPayload CreateEnemyUse(ObservedMoveSnapshot move) => new()
    {
        EnemyId = "enemy-1",
        SpeciesId = "BELLOSSOM:0",
        EnemyLevel = 25,
        Move = move
    };

    /// <summary>
    /// Creates one representative level-up move observation.
    /// </summary>
    /// <param name="id">The move identifier.</param>
    /// <param name="level">The learned level.</param>
    /// <param name="order">The learnset order.</param>
    /// <param name="ppAfterUse">The observed remaining PP.</param>
    /// <param name="source">The move's learn-source identifier.</param>
    /// <returns>The move observation.</returns>
    private static ObservedMoveSnapshot CreateMove(string id, int level, int order, int? ppAfterUse = null, string source = "level_up") => new()
    {
        Id = id,
        Name = id,
        LearnedLevel = level,
        LearnOrder = order,
        Source = source,
        Origin = ppAfterUse is null ? "player_initial" : "enemy_use",
        Type = "GRASS",
        Power = 40,
        Accuracy = 100,
        TotalPp = 15,
        PpAfterUse = ppAfterUse
    };
}
