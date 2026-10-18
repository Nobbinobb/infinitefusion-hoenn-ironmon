namespace Ironmon.Tracker.App.Components.Navigation;

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
    /// Shows active-run world information and utility tools.
    /// </summary>
    Lookup = 2,

    /// <summary>
    /// Shows completed-run history, lookup, and analysis.
    /// </summary>
    Archive = 3
}
