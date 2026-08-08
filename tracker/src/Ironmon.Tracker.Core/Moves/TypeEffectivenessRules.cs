namespace Ironmon.Tracker.Core.Moves;

/// <summary>
/// Calculates standard type-chart effectiveness without hidden ability effects.
/// </summary>
public static class TypeEffectivenessRules
{
    private static readonly IReadOnlyDictionary<string, TypeMatchup> _matchups = new Dictionary<string, TypeMatchup>(StringComparer.Ordinal)
    {
        ["NORMAL"] = new([], ["ROCK", "STEEL"], ["GHOST"]),
        ["FIRE"] = new(["GRASS", "ICE", "BUG", "STEEL"], ["FIRE", "WATER", "ROCK", "DRAGON"]),
        ["WATER"] = new(["FIRE", "GROUND", "ROCK"], ["WATER", "GRASS", "DRAGON"]),
        ["ELECTRIC"] = new(["WATER", "FLYING"], ["ELECTRIC", "GRASS", "DRAGON"], ["GROUND"]),
        ["GRASS"] = new(["WATER", "GROUND", "ROCK"], ["FIRE", "GRASS", "POISON", "FLYING", "BUG", "DRAGON", "STEEL"]),
        ["ICE"] = new(["GRASS", "GROUND", "FLYING", "DRAGON"], ["FIRE", "WATER", "ICE", "STEEL"]),
        ["FIGHTING"] = new(["NORMAL", "ICE", "ROCK", "DARK", "STEEL"], ["POISON", "FLYING", "PSYCHIC", "BUG", "FAIRY"], ["GHOST"]),
        ["POISON"] = new(["GRASS", "FAIRY"], ["POISON", "GROUND", "ROCK", "GHOST"], ["STEEL"]),
        ["GROUND"] = new(["FIRE", "ELECTRIC", "POISON", "ROCK", "STEEL"], ["GRASS", "BUG"], ["FLYING"]),
        ["FLYING"] = new(["GRASS", "FIGHTING", "BUG"], ["ELECTRIC", "ROCK", "STEEL"]),
        ["PSYCHIC"] = new(["FIGHTING", "POISON"], ["PSYCHIC", "STEEL"], ["DARK"]),
        ["BUG"] = new(["GRASS", "PSYCHIC", "DARK"], ["FIRE", "FIGHTING", "POISON", "FLYING", "GHOST", "STEEL", "FAIRY"]),
        ["ROCK"] = new(["FIRE", "ICE", "FLYING", "BUG"], ["FIGHTING", "GROUND", "STEEL"]),
        ["GHOST"] = new(["PSYCHIC", "GHOST"], ["DARK"], ["NORMAL"]),
        ["DRAGON"] = new(["DRAGON"], ["STEEL"], ["FAIRY"]),
        ["DARK"] = new(["PSYCHIC", "GHOST"], ["FIGHTING", "DARK", "FAIRY"]),
        ["STEEL"] = new(["ICE", "ROCK", "FAIRY"], ["FIRE", "WATER", "ELECTRIC", "STEEL"]),
        ["FAIRY"] = new(["FIGHTING", "DRAGON", "DARK"], ["FIRE", "POISON", "STEEL"])
    };

    /// <summary>
    /// Calculates combined effectiveness for a move type against one or two target types.
    /// </summary>
    /// <param name="moveType">The attacking move type identifier.</param>
    /// <param name="targetTypes">The defending type identifiers.</param>
    /// <returns>The combined type effectiveness.</returns>
    /// <exception cref="ArgumentException">Thrown when the move type is empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when target types is null.</exception>
    public static MoveEffectiveness Calculate(string moveType, IEnumerable<string> targetTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moveType);
        ArgumentNullException.ThrowIfNull(targetTypes);
        decimal multiplier = 1m;
        foreach (string targetType in targetTypes.Distinct(StringComparer.OrdinalIgnoreCase))
            multiplier *= GetSingleMultiplier(moveType.ToUpperInvariant(), targetType.ToUpperInvariant());

        return GetEffectiveness(multiplier);
    }

    /// <summary>
    /// Converts a combined multiplier into its tracker representation.
    /// </summary>
    /// <param name="multiplier">The combined type multiplier.</param>
    /// <returns>The tracker effectiveness representation.</returns>
    private static MoveEffectiveness GetEffectiveness(decimal multiplier) => multiplier switch
    {
        0m => MoveEffectiveness.Immune,
        0.25m => MoveEffectiveness.Quarter,
        0.5m => MoveEffectiveness.Half,
        2m => MoveEffectiveness.Double,
        4m => MoveEffectiveness.Quadruple,
        _ => MoveEffectiveness.Neutral
    };

    /// <summary>
    /// Gets one attacking-type and defending-type multiplier.
    /// </summary>
    /// <param name="attack">The normalized attacking type.</param>
    /// <param name="defense">The normalized defending type.</param>
    /// <returns>The single-type multiplier.</returns>
    private static decimal GetSingleMultiplier(string attack, string defense)
    {
        if (!_matchups.TryGetValue(attack, out TypeMatchup? matchup))
            return 1m;

        if (matchup.Immune.Contains(defense))
            return 0m;

        if (matchup.SuperEffective.Contains(defense))
            return 2m;

        return matchup.Resisted.Contains(defense) ? 0.5m : 1m;
    }

    /// <summary>
    /// Groups one attacking type's defending-type matchups in chart order.
    /// </summary>
    /// <remarks>
    /// Initializes an attacking type's matchup row.
    /// </remarks>
    /// <param name="superEffective">The defending types that receive double damage.</param>
    /// <param name="resisted">The defending types that receive half damage.</param>
    /// <param name="immune">The defending types that receive no damage.</param>
    private sealed class TypeMatchup(IEnumerable<string> superEffective, IEnumerable<string> resisted, IEnumerable<string>? immune = null)
    {
        /// <summary>
        /// Gets the defending types that receive double damage.
        /// </summary>
        public IReadOnlySet<string> SuperEffective { get; } = new HashSet<string>(superEffective, StringComparer.Ordinal);

        /// <summary>
        /// Gets the defending types that receive half damage.
        /// </summary>
        public IReadOnlySet<string> Resisted { get; } = new HashSet<string>(resisted, StringComparer.Ordinal);

        /// <summary>
        /// Gets the defending types that receive no damage.
        /// </summary>
        public IReadOnlySet<string> Immune { get; } = new HashSet<string>(immune ?? [], StringComparer.Ordinal);
    }
}
