namespace Ironmon.Tracker.App;

/// <summary>
/// Stores tracker-local evolution graph preferences.
/// </summary>
public sealed class EvolutionGraphSettings
{
    /// <summary>
    /// Gets the default number of relationship layers loaded per selection.
    /// </summary>
    public const int DefaultExpansionDepth = 1;

    /// <summary>
    /// Gets the greatest selectable relationship expansion depth.
    /// </summary>
    public const int MaximumExpansionDepth = 5;

    /// <summary>
    /// Gets the least selectable relationship expansion depth.
    /// </summary>
    public const int MinimumExpansionDepth = 1;

    /// <summary>
    /// Gets the default maximum number of nodes displayed in one physical graph row.
    /// </summary>
    public const int DefaultNodesPerRow = 15;

    /// <summary>
    /// Gets the greatest selectable number of nodes displayed in one physical graph row.
    /// </summary>
    public const int MaximumNodesPerRow = 30;

    /// <summary>
    /// Gets the least selectable number of nodes displayed in one physical graph row.
    /// </summary>
    public const int MinimumNodesPerRow = 5;

    /// <summary>
    /// Gets or sets how many relationship layers graph dialogs load around each selected node.
    /// </summary>
    public static int ExpansionDepth
    {
        get => Math.Clamp(Preferences.Default.Get(TrackerApplicationConstants.EvolutionGraphExpansionDepthPreferenceKey, DefaultExpansionDepth), MinimumExpansionDepth, MaximumExpansionDepth);
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, MinimumExpansionDepth);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaximumExpansionDepth);
            Preferences.Default.Set(TrackerApplicationConstants.EvolutionGraphExpansionDepthPreferenceKey, value);
        }
    }

    /// <summary>
    /// Gets or sets the maximum number of nodes displayed in one physical graph row.
    /// </summary>
    public static int NodesPerRow
    {
        get => Math.Clamp(Preferences.Default.Get(TrackerApplicationConstants.EvolutionGraphNodesPerRowPreferenceKey, DefaultNodesPerRow), MinimumNodesPerRow, MaximumNodesPerRow);
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, MinimumNodesPerRow);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaximumNodesPerRow);
            Preferences.Default.Set(TrackerApplicationConstants.EvolutionGraphNodesPerRowPreferenceKey, value);
        }
    }
}
