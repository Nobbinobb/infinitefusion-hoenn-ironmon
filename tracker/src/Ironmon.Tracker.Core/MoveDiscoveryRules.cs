namespace Ironmon.Tracker.Core;

/// <summary>
/// Applies the agreed level-up move discovery rules for an opposing Pokemon.
/// </summary>
public static class MoveDiscoveryRules
{
    /// <summary>
    /// Gets the maximum number of moves displayed for one Pokemon.
    /// </summary>
    public const int MaximumDisplayedMoves = 4;

    /// <summary>
    /// Selects the four newest applicable discoveries, including directly observed enemy moves.
    /// </summary>
    /// <param name="discoveries">All remembered discoveries for the run and species.</param>
    /// <param name="pokemonLevel">The level of the Pokemon being displayed.</param>
    /// <returns>
    /// The applicable moves in chronological moveset order from oldest to newest.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when discoveries is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the Pokemon level is not positive.
    /// </exception>
    public static IReadOnlyList<DiscoveredMove> SelectDisplayedMoves(IEnumerable<DiscoveredMove> discoveries, int pokemonLevel)
    {
        ArgumentNullException.ThrowIfNull(discoveries);
        ArgumentOutOfRangeException.ThrowIfLessThan(pokemonLevel, 1);

        return [.. discoveries
            .Where(move => IsApplicable(move, pokemonLevel))
            .GroupBy(move => move.MoveId, StringComparer.Ordinal)
            .Select(SelectNewestDiscovery)
            .OrderBy(move => move.LearnedLevel)
            .ThenBy(move => move.LearnOrder)
            .TakeLast(MaximumDisplayedMoves)];
    }

    /// <summary>
    /// Determines whether a discovered move belongs to the displayed moveset.
    /// </summary>
    /// <param name="move">The discovered move.</param>
    /// <param name="pokemonLevel">The level of the Pokemon being displayed.</param>
    /// <returns>True when the move is an applicable level-up move or was directly observed; otherwise false.</returns>
    private static bool IsApplicable(DiscoveredMove move, int pokemonLevel)
    {
        return move.LearnSource == MoveLearnSource.LevelUp && move.LearnedLevel <= pokemonLevel
            || move.LearnSource == MoveLearnSource.Unknown && move.DiscoveryOrigin == MoveDiscoveryOrigin.EnemyUse;
    }


    /// <summary>
    /// Selects the newest observed learnset entry for one move identifier.
    /// </summary>
    /// <param name="discoveries">The observations grouped by move identifier.</param>
    /// <returns>The newest observation.</returns>
    private static DiscoveredMove SelectNewestDiscovery(IGrouping<string, DiscoveredMove> discoveries)
        => discoveries.OrderByDescending(move => move.LearnedLevel).ThenByDescending(move => move.LearnOrder).First();
}
