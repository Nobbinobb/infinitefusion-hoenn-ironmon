namespace Ironmon.Tracker.Core.Coverage;

/// <summary>
/// Defines the standard Pokemon types in stable type-chart order.
/// </summary>
public static class PokemonTypeCatalog
{
    private static readonly IReadOnlyDictionary<string, int> _typeOrder;

    /// <summary>
    /// Gets all standard type identifiers in stable display and profile order.
    /// </summary>
    public static IReadOnlyList<string> StandardTypes { get; } =
    [
        "NORMAL", "FIRE", "WATER", "ELECTRIC", "GRASS", "ICE",
        "FIGHTING", "POISON", "GROUND", "FLYING", "PSYCHIC", "BUG",
        "ROCK", "GHOST", "DRAGON", "DARK", "STEEL", "FAIRY"
    ];

    /// <summary>
    /// Initializes the stable type-order index.
    /// </summary>
    static PokemonTypeCatalog()
    {
        _typeOrder = StandardTypes.Select((type, index) => (type, index)).ToDictionary(entry => entry.type, entry => entry.index, StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets one standard type's stable profile order.
    /// </summary>
    /// <param name="type">The normalized type identifier.</param>
    /// <returns>The zero-based stable order.</returns>
    /// <exception cref="ArgumentException">Thrown when the identifier is empty or unsupported.</exception>
    public static int GetOrder(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return _typeOrder.TryGetValue(type, out int order) ? order : throw new ArgumentException($"Unsupported Pokemon type '{type}'.", nameof(type));
    }
}
