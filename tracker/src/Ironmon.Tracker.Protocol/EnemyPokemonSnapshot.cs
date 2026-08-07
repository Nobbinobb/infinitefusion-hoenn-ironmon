namespace Ironmon.Tracker.Protocol;

/// <summary>
/// Describes the legally visible identity of one active opposing Pokemon.
/// </summary>
public sealed class EnemyPokemonSnapshot
{
    /// <summary>
    /// Initializes an empty enemy Pokemon snapshot for protocol serialization.
    /// </summary>
    public EnemyPokemonSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the battle-stable enemy identifier.
    /// </summary>
    public required string EnemyId { get; init; }

    /// <summary>
    /// Gets or initializes the opposing battler position.
    /// </summary>
    public int Position { get; init; }

    /// <summary>
    /// Gets or initializes the stable species and form identifier.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the localized species name.
    /// </summary>
    public required string SpeciesName { get; init; }

    /// <summary>
    /// Gets or initializes the resolved sprite path when available.
    /// </summary>
    public string? SpritePath { get; init; }

    /// <summary>
    /// Gets or initializes the visible level.
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    /// Gets or initializes the visible type identifiers.
    /// </summary>
    public IReadOnlyList<string> Types { get; init; } = [];

    /// <summary>
    /// Gets or initializes the visible base-stat total.
    /// </summary>
    public int BaseStatTotal { get; init; }

    /// <summary>
    /// Gets or initializes the most recently used regular move when one has become observable.
    /// </summary>
    public ObservedMoveSnapshot? LastMove { get; init; }

    /// <summary>
    /// Gets or initializes the most recently revealed ability when one exists.
    /// </summary>
    public AbilitySnapshot? LastAbility { get; init; }
}
