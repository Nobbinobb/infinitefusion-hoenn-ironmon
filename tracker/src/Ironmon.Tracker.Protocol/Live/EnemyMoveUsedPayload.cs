namespace Ironmon.Tracker.Protocol.Live;

/// <summary>
/// Describes a move made observable by an opposing Pokemon's use.
/// </summary>
public sealed class EnemyMoveUsedPayload
{
    /// <summary>
    /// Initializes an empty enemy move-used payload for protocol serialization.
    /// </summary>
    public EnemyMoveUsedPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the battle-stable enemy identifier.
    /// </summary>
    public required string EnemyId { get; init; }

    /// <summary>
    /// Gets or initializes the stable species and form identifier.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the enemy's current visible level.
    /// </summary>
    public int EnemyLevel { get; init; }

    /// <summary>
    /// Gets or initializes the observed move.
    /// </summary>
    public required ObservedMoveSnapshot Move { get; init; }
}
