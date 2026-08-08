namespace Ironmon.Tracker.App.Components.Debug;

/// <summary>
/// Identifies the selected page within the authorized debug inspector.
/// </summary>
public enum DebugInspectorPage
{
    /// <summary>
    /// Shows inspected Pokemon identity and generator metadata.
    /// </summary>
    Overview = 0,

    /// <summary>
    /// Shows current, original, and generated ability slots.
    /// </summary>
    Abilities = 1,

    /// <summary>
    /// Shows game-owned run and randomizer diagnostics.
    /// </summary>
    Diagnostics = 2,

    /// <summary>
    /// Searches complete generated data for the active run.
    /// </summary>
    Lookup = 3,

    /// <summary>
    /// Shows tracker-owned state and protocol diagnostics.
    /// </summary>
    Protocol = 4
}
