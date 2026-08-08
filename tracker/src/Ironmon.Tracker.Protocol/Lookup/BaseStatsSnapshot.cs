namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Describes all six base stats for generated-data inspection.
/// </summary>
public sealed class BaseStatsSnapshot
{
    /// <summary>
    /// Initializes an empty base-stat snapshot for protocol serialization.
    /// </summary>
    public BaseStatsSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes base HP.
    /// </summary>
    public int Hp { get; init; }

    /// <summary>
    /// Gets or initializes base Attack.
    /// </summary>
    public int Attack { get; init; }

    /// <summary>
    /// Gets or initializes base Defense.
    /// </summary>
    public int Defense { get; init; }

    /// <summary>
    /// Gets or initializes base Special Attack.
    /// </summary>
    public int SpecialAttack { get; init; }

    /// <summary>
    /// Gets or initializes base Special Defense.
    /// </summary>
    public int SpecialDefense { get; init; }

    /// <summary>
    /// Gets or initializes base Speed.
    /// </summary>
    public int Speed { get; init; }
}
