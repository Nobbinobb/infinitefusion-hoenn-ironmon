namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Verifies the complete player and healing snapshot protocol contract.
/// </summary>
public sealed class PlayerSnapshotCodecTests
{
    private const string PackedFusionPool = "AQID";
    private const string OriginalSpeciesId = "DITTO:0";
    private const string StoredAbilityName = "Imposter";
    private const string CopiedAbilityName = "Synchronize";
    /// <summary>
    /// Initializes the player snapshot codec tests.
    /// </summary>
    public PlayerSnapshotCodecTests()
    {
    }

    /// <summary>
    /// Verifies canonical player fields and nested move data survive serialization.
    /// </summary>
    [Fact]
    public void PlayerSnapshotRoundTripsCompleteLiveState()
    {
        PlayerPokemonSnapshot player = CreateSnapshot();

        string json = TrackerJson.SerializePayload(player).GetRawText();
        PlayerPokemonSnapshot restored = TrackerJson.DeserializePayload<PlayerPokemonSnapshot>(TrackerJson.SerializePayload(player));

        Assert.Contains("\"current_hp\":18", json, StringComparison.Ordinal);
        Assert.Contains("\"total_pp\":10", json, StringComparison.Ordinal);
        Assert.Equal("Psychic", Assert.Single(restored.Moves).Name);
        Assert.Equal("Psychic", Assert.Single(restored.StoredMoves).Name);
        Assert.True(restored.Transformed);
        Assert.Equal(OriginalSpeciesId, restored.OriginalSpeciesId);
        Assert.Equal(StoredAbilityName, restored.StoredAbilityDetails?.Name);
        Assert.Equal(CopiedAbilityName, restored.CopiedAbilityDetails?.Name);
        MovePowerPresentationSnapshot powerPresentation = Assert.IsType<MovePowerPresentationSnapshot>(Assert.Single(restored.Moves).PowerPresentation);
        Assert.Equal("125", powerPresentation.Display);
        Assert.Equal(MovePowerIndicator.MultiHit, powerPresentation.Indicator);
        Assert.Equal(5, Assert.Single(powerPresentation.Outcomes).Hits);
        Assert.Equal(125, restored.Healing.Percentage);
        Assert.Equal("female", restored.Gender);
        Assert.True(restored.Confused);
        Assert.Equal(2, restored.StatStages.Attack);
        Assert.Equal(-1, restored.StatStages.Speed);
        Assert.Equal(-2, restored.StatStages.Accuracy);
        Assert.Equal(1, restored.StatStages.Evasion);
        BattleItemSnapshot item = Assert.Single(restored.Healing.Items);
        Assert.Equal("Potion", item.Name);
        Assert.Equal(BattleItemCategory.Healing, item.Category);
    }

    /// <summary>
    /// Verifies Part 3 current-state payloads remain readable without Part 4 data.
    /// </summary>
    [Fact]
    public void CurrentStateAcceptsAbsentBattleAndPlayerSnapshots()
    {
        const string Json = """
            {"ironmon_active":true,"run_id":"run-1","battle_id":null,"sequence":9}
            """;

        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(Json);
        GameCurrentStatePayload state = TrackerJson.DeserializePayload<GameCurrentStatePayload>(document.RootElement);

        Assert.Null(state.Battle);
        Assert.Null(state.Player);
        Assert.Equal(9, state.Sequence);
    }

    /// <summary>
    /// Verifies current-state recovery preserves the authorized native fusion assignment recipe.
    /// </summary>
    [Fact]
    public void CurrentStateRoundTripsFusionAssignmentRecipe()
    {
        FusionAssignmentRecipePayload recipe = new()
        {
            Seed = 42,
            PlayerFusionGeneratorVersion = 1,
            GeneratorVersion = 1,
            RulesVersion = 1,
            SourceFingerprint = "sources",
            TaxonomyFingerprint = "taxonomy",
            MethodFingerprint = "methods",
            BaseStatSourceFingerprint = "stats",
            TargetPoolVersion = 1,
            TargetPoolSize = 10,
            TargetPoolFingerprint = "targets",
            PackedCustomFusionPool = PackedFusionPool
        };
        GameCurrentStatePayload state = new(true, "run-1", null, 9, fusionAssignments: recipe, activeRunPreparationReady: true);

        GameCurrentStatePayload restored = TrackerJson.DeserializePayload<GameCurrentStatePayload>(TrackerJson.SerializePayload(state));

        Assert.Equal(42, restored.FusionAssignments?.Seed);
        Assert.Equal(1, restored.FusionAssignments?.PlayerFusionGeneratorVersion);
        Assert.Equal("targets", restored.FusionAssignments?.TargetPoolFingerprint);
        Assert.Equal(PackedFusionPool, restored.FusionAssignments?.PackedCustomFusionPool);
        Assert.True(restored.ActiveRunPreparationReady);
    }

    /// <summary>
    /// Verifies evolution requirements cannot reveal the destination species through the protocol.
    /// </summary>
    [Fact]
    public void EvolutionSnapshotContainsOnlyRequirementData()
    {
        EvolutionSnapshot evolution = new()
        {
            Kind = EvolutionRequirementKind.Level,
            Level = 20,
            Requirement = "Level 20"
        };

        string json = TrackerJson.SerializePayload(evolution).GetRawText();

        Assert.DoesNotContain("species", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"requirement\":\"Level 20\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Creates the representative complete player payload.
    /// </summary>
    /// <returns>The complete player payload.</returns>
    private static PlayerPokemonSnapshot CreateSnapshot()
    {
        PlayerMoveSnapshot move = new()
        {
            Id = "PSYCHIC",
            Name = "Psychic",
            Type = "PSYCHIC",
            CurrentPp = 8,
            TotalPp = 10,
            Power = 90,
            Accuracy = 100,
            PowerPresentation = new MovePowerPresentationSnapshot
            {
                Display = "125",
                Indicator = MovePowerIndicator.MultiHit,
                DetailsKind = MovePowerDetailsKind.Outcomes,
                Outcomes =
                [
                    new MovePowerOutcomeSnapshot
                    {
                        Kind = MovePowerOutcomeKind.Hits,
                        Hits = 5,
                        Power = 125,
                        ChancePercent = 100
                    }
                ]
            }
        };

        return new PlayerPokemonSnapshot
        {
            PokemonId = "42",
            SpeciesId = "ESPEON:0",
            OriginalSpeciesId = OriginalSpeciesId,
            Nickname = "Espeon",
            SpeciesName = "Espeon",
            Transformed = true,
            Gender = "female",
            SpritePath = "Graphics/Battlers/196.png",
            Level = 5,
            CurrentHp = 18,
            MaximumHp = 24,
            Status = "NONE",
            Confused = true,
            Types = ["PSYCHIC"],
            Ability = "Synchronize",
            StoredAbilityDetails = new AbilitySnapshot { Id = "IMPOSTER", Name = StoredAbilityName, Description = "Copies the opposing Pokemon." },
            CopiedAbilityDetails = new AbilitySnapshot { Id = "SYNCHRONIZE", Name = CopiedAbilityName, Description = "Passes status conditions back." },
            Attack = 14,
            Defense = 16,
            SpecialAttack = 21,
            SpecialDefense = 18,
            Speed = 15,
            StatStages = new BattleStatStagesSnapshot { Attack = 2, Speed = -1, Accuracy = -2, Evasion = 1 },
            BaseStatTotal = 525,
            Nature = "Hardy",
            Moves = [move],
            StoredMoves = [move],
            Healing = new HealingInventorySnapshot
            {
                ItemCount = 3,
                PotentialHp = 30,
                Percentage = 125,
                Items =
                [
                    new BattleItemSnapshot
                    {
                        Id = "POTION",
                        Name = "Potion",
                        Description = "Restores HP.",
                        Quantity = 3,
                        Category = BattleItemCategory.Healing
                    }
                ]
            }
        };
    }
}
