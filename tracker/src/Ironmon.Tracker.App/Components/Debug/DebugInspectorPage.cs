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
    /// Shows final original and generated base stats.
    /// </summary>
    Stats = 2,

    /// <summary>
    /// Shows generated Learnset, Egg, TM, and Tutor access.
    /// </summary>
    Moves = 6,

    /// <summary>
    /// Shows game-owned run and randomizer diagnostics.
    /// </summary>
    Diagnostics = 3,

    /// <summary>
    /// Searches complete generated data for the active run.
    /// </summary>
    Lookup = 4,

    /// <summary>
    /// Shows tracker-owned state and protocol diagnostics.
    /// </summary>
    Protocol = 5
}
