using Ironmon.Tracker.Core;

namespace Ironmon.Tracker.Tests;

/// <summary>
/// Verifies the deterministic projection of remembered enemy moves.
/// </summary>
public sealed class MoveDiscoveryRulesTests
{
    /// <summary>
    /// Initializes the move discovery rule tests.
    /// </summary>
    public MoveDiscoveryRulesTests()
    {
    }

    /// <summary>
    /// Verifies that only the four newest applicable moves are displayed.
    /// </summary>
    [Fact]
    public void SelectDisplayedMovesReturnsFourNewestMovesInChronologicalOrder()
    {
        DiscoveredMove[] discoveries =
        [
            new("MOVE_A", 5, 0, MoveLearnSource.LevelUp, MoveDiscoveryOrigin.EnemyUse),
            new("MOVE_B", 10, 1, MoveLearnSource.LevelUp, MoveDiscoveryOrigin.PlayerInitial),
            new("MOVE_C", 15, 2, MoveLearnSource.LevelUp, MoveDiscoveryOrigin.EnemyUse),
            new("MOVE_D", 20, 3, MoveLearnSource.LevelUp, MoveDiscoveryOrigin.PlayerLevelUp),
            new("MOVE_E", 25, 4, MoveLearnSource.LevelUp, MoveDiscoveryOrigin.EnemyUse)
        ];

        IReadOnlyList<DiscoveredMove> result = MoveDiscoveryRules.SelectDisplayedMoves(discoveries, 25);
        Assert.Equal(["MOVE_B", "MOVE_C", "MOVE_D", "MOVE_E"], result.Select(move => move.MoveId));
        Assert.Equal(5, discoveries.Length);
    }

    /// <summary>
    /// Verifies that retained older discoveries return for a lower-level enemy.
    /// </summary>
    [Fact]
    public void SelectDisplayedMovesRestoresOlderMovesForLowerLevelEnemy()
    {
        DiscoveredMove[] discoveries =
        [
            new("MOVE_A", 5, 0, MoveLearnSource.LevelUp, MoveDiscoveryOrigin.EnemyUse),
            new("MOVE_B", 10, 1, MoveLearnSource.LevelUp, MoveDiscoveryOrigin.EnemyUse),
            new("MOVE_C", 15, 2, MoveLearnSource.LevelUp, MoveDiscoveryOrigin.EnemyUse),
            new("MOVE_D", 20, 3, MoveLearnSource.LevelUp, MoveDiscoveryOrigin.EnemyUse),
            new("MOVE_E", 25, 4, MoveLearnSource.LevelUp, MoveDiscoveryOrigin.EnemyUse)
        ];

        IReadOnlyList<DiscoveredMove> result = MoveDiscoveryRules.SelectDisplayedMoves(discoveries, 15);
        Assert.Equal(["MOVE_A", "MOVE_B", "MOVE_C"], result.Select(move => move.MoveId));
    }

    /// <summary>
    /// Verifies that learn order breaks ties between moves learned at one level.
    /// </summary>
    [Fact]
    public void SelectDisplayedMovesUsesLearnOrderForSameLevelMoves()
    {
        DiscoveredMove[] discoveries =
        [
            new("MOVE_A", 12, 1, MoveLearnSource.LevelUp, MoveDiscoveryOrigin.EnemyUse),
            new("MOVE_B", 12, 2, MoveLearnSource.LevelUp, MoveDiscoveryOrigin.EnemyUse),
            new("MOVE_C", 12, 3, MoveLearnSource.LevelUp, MoveDiscoveryOrigin.EnemyUse),
            new("MOVE_D", 12, 4, MoveLearnSource.LevelUp, MoveDiscoveryOrigin.EnemyUse),
            new("MOVE_E", 12, 5, MoveLearnSource.LevelUp, MoveDiscoveryOrigin.EnemyUse)
        ];

        IReadOnlyList<DiscoveredMove> result = MoveDiscoveryRules.SelectDisplayedMoves(discoveries, 12);
        Assert.Equal(["MOVE_B", "MOVE_C", "MOVE_D", "MOVE_E"], result.Select(move => move.MoveId));
    }

    /// <summary>
    /// Verifies that non-level-up player moves do not reveal enemy moves.
    /// </summary>
    [Fact]
    public void SelectDisplayedMovesExcludesNonLevelUpSources()
    {
        DiscoveredMove[] discoveries =
        [
            new("LEVEL_MOVE", 10, 1, MoveLearnSource.LevelUp, MoveDiscoveryOrigin.PlayerLevelUp),
            new("MACHINE_MOVE", 10, 2, MoveLearnSource.Machine, MoveDiscoveryOrigin.PlayerInitial),
            new("TUTOR_MOVE", 10, 3, MoveLearnSource.Tutor, MoveDiscoveryOrigin.PlayerInitial)
        ];

        IReadOnlyList<DiscoveredMove> result = MoveDiscoveryRules.SelectDisplayedMoves(discoveries, 10);
        Assert.Equal("LEVEL_MOVE", Assert.Single(result).MoveId);
    }

    /// <summary>
    /// Verifies that duplicate observations retain the newest applicable learnset entry.
    /// </summary>
    [Fact]
    public void SelectDisplayedMovesDeduplicatesRepeatedObservations()
    {
        DiscoveredMove[] discoveries =
        [
            new("MOVE_A", 5, 1, MoveLearnSource.LevelUp, MoveDiscoveryOrigin.PlayerInitial),
            new("MOVE_A", 10, 2, MoveLearnSource.LevelUp, MoveDiscoveryOrigin.EnemyUse)
        ];

        DiscoveredMove result = Assert.Single(MoveDiscoveryRules.SelectDisplayedMoves(discoveries, 10));
        Assert.Equal(10, result.LearnedLevel);
        Assert.Equal(MoveDiscoveryOrigin.EnemyUse, result.DiscoveryOrigin);
    }
}
