namespace Ironmon.Tracker.Protocol.Live;

/// <summary>
/// Describes a change to the opposing battler highlighted in the battle target menu.
/// </summary>
public sealed class PlayerTargetChangedPayload
{
    /// <summary>
    /// Initializes an empty target-change payload for protocol serialization.
    /// </summary>
    public PlayerTargetChangedPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the highlighted opposing battler position, or null outside target selection.
    /// </summary>
    public int? Position { get; init; }
}
