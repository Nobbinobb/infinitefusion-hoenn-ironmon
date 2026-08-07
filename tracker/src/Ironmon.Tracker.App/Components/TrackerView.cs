namespace Ironmon.Tracker.App.Components;

/// <summary>
/// Identifies the primary live tracker view selected by the user.
/// </summary>
public enum TrackerView
{
    /// <summary>
    /// Shows the player's active Pokemon.
    /// </summary>
    Player = 0,

    /// <summary>
    /// Shows the currently selected opposing Pokemon.
    /// </summary>
    Enemy = 1,

    /// <summary>
    /// Searches deterministic generated data for completed runs.
    /// </summary>
    Lookup = 2
}
