namespace Ironmon.Tracker.App.Components.Debug;

/// <summary>
/// Identifies the selected page within the authorized debug inspector.
/// </summary>
public enum DebugInspectorPage
{
    /// <summary>
    /// Shows the shared tabbed information card for the current player or enemy Pokemon.
    /// </summary>
    Pokemon = 0,

    /// <summary>
    /// Searches the shared tabbed information card for any Pokemon in the active run.
    /// </summary>
    Lookup = 1,

    /// <summary>
    /// Shows game-owned run and randomizer diagnostics.
    /// </summary>
    Diagnostics = 2,

    /// <summary>
    /// Shows tracker-owned state and protocol diagnostics.
    /// </summary>
    Protocol = 3
}
