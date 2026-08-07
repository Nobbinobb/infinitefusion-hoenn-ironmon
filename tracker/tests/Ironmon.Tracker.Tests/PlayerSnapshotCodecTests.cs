using Ironmon.Tracker.Protocol;

namespace Ironmon.Tracker.Tests;

/// <summary>
/// Verifies the complete player and healing snapshot protocol contract.
/// </summary>
public sealed class PlayerSnapshotCodecTests
{
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
        Assert.Equal(125, restored.Healing.Percentage);
        Assert.True(restored.Confused);
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
            Accuracy = 100
        };

        return new PlayerPokemonSnapshot
        {
            PokemonId = "42",
            SpeciesId = "ESPEON:0",
            Nickname = "Espeon",
            SpeciesName = "Espeon",
            SpritePath = "Graphics/Battlers/196.png",
            Level = 5,
            CurrentHp = 18,
            MaximumHp = 24,
            Status = "NONE",
            Confused = true,
            Types = ["PSYCHIC"],
            Ability = "Synchronize",
            Attack = 14,
            Defense = 16,
            SpecialAttack = 21,
            SpecialDefense = 18,
            Speed = 15,
            BaseStatTotal = 525,
            Nature = "Hardy",
            Moves = [move],
            Healing = new HealingInventorySnapshot { ItemCount = 3, PotentialHp = 30, Percentage = 125 }
        };
    }
}
