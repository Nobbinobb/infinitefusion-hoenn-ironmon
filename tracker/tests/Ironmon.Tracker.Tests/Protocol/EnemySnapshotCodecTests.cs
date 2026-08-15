namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Verifies the enemy capture-chance protocol contract.
/// </summary>
public sealed class EnemySnapshotCodecTests
{
    /// <summary>
    /// Initializes the enemy snapshot codec tests.
    /// </summary>
    public EnemySnapshotCodecTests()
    {
    }

    /// <summary>
    /// Verifies a wild opponent's current Poke Ball chance survives serialization.
    /// </summary>
    [Fact]
    public void WildEnemySnapshotRoundTripsCatchChance()
    {
        EnemyPokemonSnapshot enemy = CreateSnapshot(38.7);

        string json = TrackerJson.SerializePayload(enemy).GetRawText();
        EnemyPokemonSnapshot restored = TrackerJson.DeserializePayload<EnemyPokemonSnapshot>(TrackerJson.SerializePayload(enemy));

        Assert.Contains("\"catch_chance_percent\":38.7", json, StringComparison.Ordinal);
        Assert.Equal(38.7, restored.CatchChancePercent);
    }

    /// <summary>
    /// Verifies trainer opponents omit the wild-only capture chance.
    /// </summary>
    [Fact]
    public void TrainerEnemySnapshotOmitsCatchChance()
    {
        EnemyPokemonSnapshot enemy = CreateSnapshot(null);

        string json = TrackerJson.SerializePayload(enemy).GetRawText();

        Assert.DoesNotContain("catch_chance_percent", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Creates a representative enemy snapshot.
    /// </summary>
    /// <param name="catchChancePercent">The optional current capture chance.</param>
    /// <returns>The protocol snapshot.</returns>
    private static EnemyPokemonSnapshot CreateSnapshot(double? catchChancePercent) => new()
    {
        EnemyId = "enemy-1",
        Position = 1,
        SpeciesId = "BULBASAUR:0",
        SpeciesName = "Bulbasaur",
        Level = 5,
        Types = ["GRASS", "POISON"],
        BaseStatTotal = 318,
        CatchChancePercent = catchChancePercent
    };
}
