namespace Ironmon.Tracker.Tests.Connection;

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
    /// Verifies newest-four projection, encounter PP updates, player assistance, and reload.
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
            store.StartBattle("battle-1");
            store.ObservePlayer(CreatePlayer([CreateMove("MOVE1", 1, 0), CreateMove("MOVE2", 5, 1)]));
            ObservedMoveSnapshot[] enemyMoves = [CreateMove("MOVE3", 10, 2), CreateMove("MOVE4", 15, 3), CreateMove("MOVE5", 20, 4)];
            foreach (ObservedMoveSnapshot move in enemyMoves)
                store.ObserveEnemyMove("battle-1", CreateEnemyUse(move));

            ObservedMoveSnapshot repeated = CreateMove("MOVE5", 20, 4, 7);
            store.ObserveEnemyMove("battle-1", CreateEnemyUse(repeated));

            IReadOnlyList<ObservedMoveSnapshot> displayed = store.GetDisplayedMoves("BELLOSSOM:0", 25, "battle-1", "enemy-1", 1, 0);
            Assert.Equal(["MOVE2", "MOVE3", "MOVE4", "MOVE5"], displayed.Select(move => move.Id));
            Assert.Equal(7, displayed[^1].PpAfterUse);
            Assert.All(store.GetDisplayedMoves("BELLOSSOM:0", 25), move => Assert.Null(move.PpAfterUse));
            TrackerKnowledgeSnapshot snapshot = store.GetDiagnosticSnapshot();
            Assert.Equal("run-knowledge", snapshot.RunId);
            Assert.Equal(6, snapshot.Moves["BELLOSSOM:0"].Count);
            Assert.All(snapshot.Moves["BELLOSSOM:0"], move => Assert.Null(move.PpAfterUse));

            TrackerKnowledgeStore restored = new(options);
            restored.SelectRun("run-knowledge");
            Assert.Equal(displayed.Select(move => move.Id), restored.GetDisplayedMoves("BELLOSSOM:0", 25).Select(move => move.Id));
            Assert.All(restored.GetDisplayedMoves("BELLOSSOM:0", 25), move => Assert.Null(move.PpAfterUse));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Verifies a transformed player does not assign original learnset knowledge to the copied species.
    /// </summary>
    [Fact]
    public void TransformedPlayerKeepsKnowledgeUnderItsOwningSpecies()
    {
        const string RunId = "run-transform-knowledge";
        const string PokemonId = "player-ditto";
        const string OriginalSpeciesId = "DITTO:0";
        const string CopiedSpeciesId = "PIKACHU:0";
        const string OriginalSpeciesName = "Ditto";
        const string CopiedSpeciesName = "Pikachu";
        const string Gender = "genderless";
        const string Status = "NONE";
        const string CopiedAbilityId = "STATIC";
        const string CopiedAbilityName = "Static";
        const string TransientAbilityId = "MUMMY";
        const string TransientAbilityName = "Mummy";
        const string OriginalAbilityId = "IMPOSTER";
        const string OriginalAbilityName = "Imposter";
        const string OriginalMoveId = "TRANSFORM";
        string root = CreateRoot();
        try
        {
            TrackerKnowledgeStore store = new(new TrackerKnowledgeOptions(root));
            store.SelectRun(RunId);
            PlayerPokemonSnapshot player = new()
            {
                PokemonId = PokemonId,
                SpeciesId = CopiedSpeciesId,
                OriginalSpeciesId = OriginalSpeciesId,
                Nickname = OriginalSpeciesName,
                SpeciesName = CopiedSpeciesName,
                Transformed = true,
                Gender = Gender,
                Level = 25,
                Status = Status,
                Ability = CopiedAbilityName,
                AbilityDetails = CreateAbility(TransientAbilityId, TransientAbilityName),
                CopiedAbilityDetails = CreateAbility(CopiedAbilityId, CopiedAbilityName),
                StoredAbilityDetails = CreateAbility(OriginalAbilityId, OriginalAbilityName),
                LevelUpMoves = [CreateMove(OriginalMoveId, 1, 0)],
                Healing = new HealingInventorySnapshot()
            };

            store.ObservePlayer(player);

            Assert.Equal(OriginalMoveId, Assert.Single(store.GetDisplayedMoves(OriginalSpeciesId, 25)).Id);
            Assert.Empty(store.GetDisplayedMoves(CopiedSpeciesId, 25));
            Assert.Equal(OriginalAbilityName, Assert.Single(store.GetAbilities(OriginalSpeciesId)).Name);
            Assert.Equal(CopiedAbilityName, Assert.Single(store.GetAbilities(CopiedSpeciesId)).Name);
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
            store.StartBattle("battle-observed-move");
            ObservedMoveSnapshot move = CreateMove("UNKNOWN_SOURCE", 0, 0, 9, "unknown");

            store.ObserveEnemyMove("battle-observed-move", CreateEnemyUse(move));
            Assert.Equal("UNKNOWN_SOURCE", Assert.Single(store.GetDisplayedMoves("BELLOSSOM:0", 25)).Id);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Verifies highest-level and multiple-ability discovery persistence.
    /// </summary>
    [Fact]
    public void StorePersistsHighestLevelAndMultipleAbilities()
    {
        string root = CreateRoot();
        try
        {
            TrackerKnowledgeOptions options = new(root);
            TrackerKnowledgeStore store = new(options);
            store.SelectRun("run-enemy-knowledge");
            store.ObserveEnemy(CreateEnemy(18, CreateAbility("FRISK", "Frisk")));
            store.ObserveEnemy(CreateEnemy(12, CreateAbility("INFILTRATOR", "Infiltrator")));

            Assert.Equal(18, store.GetHighestLevel("NOIVERN:0"));
            Assert.Equal(["Frisk", "Infiltrator"], store.GetAbilities("NOIVERN:0").Select(ability => ability.Name));

            TrackerKnowledgeStore restored = new(options);
            restored.SelectRun("run-enemy-knowledge");
            Assert.Equal(18, restored.GetHighestLevel("NOIVERN:0"));
            Assert.Equal(2, restored.GetAbilities("NOIVERN:0").Count);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Verifies equivalent observations are ignored while changed persisted metadata is published.
    /// </summary>
    [Fact]
    public void EquivalentKnowledgeObservationsDoNotPublishChanges()
    {
        string root = CreateRoot();
        try
        {
            TrackerKnowledgeStore store = new(new TrackerKnowledgeOptions(root));
            store.SelectRun("run-equivalence");
            store.StartBattle("battle-equivalence");
            int changes = 0;
            store.Changed += (_, _) => changes++;

            store.ObserveEnemy(CreateEnemy(18, CreateAbility("FRISK", "Frisk")));
            Assert.Equal(1, changes);
            store.ObserveEnemy(CreateEnemy(18, CreateAbility("FRISK", "Frisk")));
            Assert.Equal(1, changes);
            store.ObserveEnemy(CreateEnemy(18, CreateAbility("FRISK", "Frisk", "Updated description")));
            Assert.Equal(2, changes);

            store.ObserveEnemyMove("battle-equivalence", CreateEnemyUse(CreateMove("MOVE1", 10, 1, 8)));
            Assert.Equal(3, changes);
            store.ObserveEnemyMove("battle-equivalence", CreateEnemyUse(CreateMove("MOVE1", 10, 1, 8)));
            Assert.Equal(3, changes);
            store.ObserveEnemyMove("battle-equivalence", CreateEnemyUse(CreateMove("MOVE1", 10, 1, 8, description: "Updated description")));
            Assert.Equal(4, changes);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Verifies remaining PP is isolated by battle, battler position, and trainer-party slot.
    /// </summary>
    [Fact]
    public void StoreScopesEnemyPpToEncounterAndPartySlot()
    {
        string root = CreateRoot();
        try
        {
            TrackerKnowledgeStore store = new(new TrackerKnowledgeOptions(root));
            store.SelectRun("run-pp-scope");
            store.StartBattle("battle-1");
            EnemyMoveUsedPayload firstSlot = CreateEnemyUse(CreateMove("MOVE1", 10, 1, 8));
            store.ObserveEnemyMove("battle-1", firstSlot);

            Assert.Equal(8, Assert.Single(store.GetDisplayedMoves("BELLOSSOM:0", 25, "battle-1", "enemy-1", 1, 0)).PpAfterUse);
            Assert.Null(Assert.Single(store.GetDisplayedMoves("BELLOSSOM:0", 25, "battle-1", "enemy-2", 1, 1)).PpAfterUse);

            store.StartBattle("battle-2");
            Assert.Null(Assert.Single(store.GetDisplayedMoves("BELLOSSOM:0", 25, "battle-2", "enemy-1", 1, 0)).PpAfterUse);

            EnemyMoveUsedPayload secondSlot = CreateEnemyUse(CreateMove("MOVE1", 10, 1, 14), "enemy-2", 1);
            store.ObserveEnemyMove("battle-2", secondSlot);
            Assert.Equal(14, Assert.Single(store.GetDisplayedMoves("BELLOSSOM:0", 25, "battle-2", "enemy-2", 1, 1)).PpAfterUse);
            Assert.Null(Assert.Single(store.GetDisplayedMoves("BELLOSSOM:0", 25, "battle-2", "enemy-1", 1, 0)).PpAfterUse);
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
    private static string CreateRoot()
        => Path.Combine(Path.GetTempPath(), "IronmonTrackerTests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Creates a representative enemy snapshot.
    /// </summary>
    /// <param name="level">The visible enemy level.</param>
    /// <param name="ability">The most recently revealed ability.</param>
    /// <returns>The enemy snapshot.</returns>
    private static EnemyPokemonSnapshot CreateEnemy(int level, AbilitySnapshot ability) => new()
    {
        EnemyId = "enemy-1",
        Position = 1,
        SpeciesId = "NOIVERN:0",
        SpeciesName = "Noivern",
        Level = level,
        LastAbility = ability
    };

    /// <summary>
    /// Creates a representative ability snapshot.
    /// </summary>
    /// <param name="id">The ability identifier.</param>
    /// <param name="name">The localized ability name.</param>
    /// <param name="description">The localized ability description.</param>
    /// <returns>The ability snapshot.</returns>
    private static AbilitySnapshot CreateAbility(string id, string name, string? description = null) => new()
    {
        Id = id,
        Name = name,
        Description = description ?? $"{name} description"
    };

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
        Gender = "female",
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
    /// <param name="enemyId">The battle-stable enemy identifier.</param>
    /// <param name="partyIndex">The position in the opposing trainer's party.</param>
    /// <returns>The enemy move-use payload.</returns>
    private static EnemyMoveUsedPayload CreateEnemyUse(ObservedMoveSnapshot move, string enemyId = "enemy-1", int partyIndex = 0) => new()
    {
        EnemyId = enemyId,
        Position = 1,
        PartyIndex = partyIndex,
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
    /// <param name="description">The localized move description.</param>
    /// <returns>The move observation.</returns>
    private static ObservedMoveSnapshot CreateMove(string id, int level, int order, int? ppAfterUse = null, string source = "level_up", string description = "") => new()
    {
        Id = id,
        Name = id,
        LearnedLevel = level,
        LearnOrder = order,
        Source = source,
        Origin = ppAfterUse is null ? "player_initial" : "enemy_use",
        Type = "GRASS",
        Description = description,
        Power = 40,
        Accuracy = 100,
        TotalPp = 15,
        PpAfterUse = ppAfterUse
    };
}
