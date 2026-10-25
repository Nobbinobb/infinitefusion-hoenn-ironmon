namespace Ironmon.Tracker.Connection.RunState;

/// <summary>
/// Selects unique standard attacking types from the current damaging moves.
/// </summary>
public static class TrackerTypeCoverageMoveSelector
{
    /// <summary>
    /// Selects current Physical and Special move types in stable chart order.
    /// </summary>
    /// <param name="moves">The current player move snapshots.</param>
    /// <returns>The unique supported attacking types.</returns>
    /// <exception cref="ArgumentNullException">Thrown when moves is null.</exception>
    public static IReadOnlyList<string> SelectCurrentMoveTypes(IEnumerable<PlayerMoveSnapshot> moves)
    {
        ArgumentNullException.ThrowIfNull(moves);
        HashSet<string> selected = new(StringComparer.Ordinal);
        foreach (PlayerMoveSnapshot move in moves)
        {
            if (move.Category is not (MoveCategory.Physical or MoveCategory.Special))
                continue;

            if (string.IsNullOrWhiteSpace(move.Type))
                continue;

            string type = move.Type.ToUpperInvariant();
            if (PokemonTypeCatalog.StandardTypes.Contains(type, StringComparer.Ordinal))
                selected.Add(type);
        }

        return [.. PokemonTypeCatalog.StandardTypes.Where(selected.Contains)];
    }
}
