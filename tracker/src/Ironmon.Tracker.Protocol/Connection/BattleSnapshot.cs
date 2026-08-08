namespace Ironmon.Tracker.Protocol.Connection;

/// <summary>
/// Describes the active battle visible to the tracker.
/// </summary>
public sealed class BattleSnapshot
{
    /// <summary>
    /// Initializes an empty battle snapshot for protocol serialization.
    /// </summary>
    public BattleSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the stable identifier for this battle.
    /// </summary>
    public required string BattleId { get; init; }
}
