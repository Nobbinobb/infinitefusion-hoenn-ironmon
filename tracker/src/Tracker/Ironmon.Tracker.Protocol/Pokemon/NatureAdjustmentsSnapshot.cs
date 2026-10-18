namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Describes the active nature adjustment for each non-HP stat.
/// </summary>
public sealed class NatureAdjustmentsSnapshot
{
    /// <summary>
    /// Initializes neutral nature adjustments.
    /// </summary>
    public NatureAdjustmentsSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the Attack adjustment.
    /// </summary>
    public StatAdjustment Attack { get; init; }

    /// <summary>
    /// Gets or initializes the Defense adjustment.
    /// </summary>
    public StatAdjustment Defense { get; init; }

    /// <summary>
    /// Gets or initializes the Special Attack adjustment.
    /// </summary>
    public StatAdjustment SpecialAttack { get; init; }

    /// <summary>
    /// Gets or initializes the Special Defense adjustment.
    /// </summary>
    public StatAdjustment SpecialDefense { get; init; }

    /// <summary>
    /// Gets or initializes the Speed adjustment.
    /// </summary>
    public StatAdjustment Speed { get; init; }
}
