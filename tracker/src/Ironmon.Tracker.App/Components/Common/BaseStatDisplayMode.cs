namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Identifies the selected base-stat presentation.
/// </summary>
public enum BaseStatDisplayMode
{
    /// <summary>
    /// Shows original, generated, and delta values in a table.
    /// </summary>
    Table = 0,

    /// <summary>
    /// Shows only generated values as neutral bars.
    /// </summary>
    GeneratedBars = 1,

    /// <summary>
    /// Shows generated bars colored by their difference from the original values.
    /// </summary>
    DeltaBars = 2
}
