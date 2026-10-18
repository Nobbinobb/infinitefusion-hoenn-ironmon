namespace Ironmon.Tracker.Protocol.Connection;

/// <summary>
/// Defines the transient input override event.
/// </summary>
public static class ControlProtocol
{
    /// <summary>
    /// Identifies a tracker-owned input state update.
    /// </summary>
    public const string StateEvent = "controls_state";
}

/// <summary>
/// Contains a complete, temporary game input state, renewed while the tracker is alive.
/// </summary>
/// <remarks>
/// Initializes a state update; disabling it restores native game input.
/// </remarks>
/// <param name="enabled">Whether the tracker temporarily owns the managed actions.</param>
/// <param name="managed">The complete set of game actions under tracker control.</param>
/// <param name="pressed">The managed actions currently held.</param>
public sealed class ControlStatePayload(bool enabled, IReadOnlyList<string> managed, IReadOnlyList<string> pressed)
{
    /// <summary>
    /// Gets whether the tracker owns managed game controls.
    /// </summary>
    public bool Enabled { get; } = enabled;

    /// <summary>
    /// Gets the complete set of overridden actions.
    /// </summary>
    public IReadOnlyList<string> Managed { get; } = managed;

    /// <summary>
    /// Gets the actions currently held.
    /// </summary>
    public IReadOnlyList<string> Pressed { get; } = pressed;
}
