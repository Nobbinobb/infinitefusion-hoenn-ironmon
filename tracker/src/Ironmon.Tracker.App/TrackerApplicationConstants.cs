namespace Ironmon.Tracker.App;

/// <summary>
/// Defines stable native application configuration values.
/// </summary>
internal static class TrackerApplicationConstants
{
    /// <summary>
    /// Gets the fixed tracker-window width.
    /// </summary>
    internal const double WindowWidth = 500;

    /// <summary>
    /// Gets the fixed tracker-window height.
    /// </summary>
    internal const double WindowHeight = 840;

    /// <summary>
    /// Gets the persisted automatic starter-selection preference key.
    /// </summary>
    internal const string AutoSelectStarterPreferenceKey = "auto_select_starter";

    /// <summary>
    /// Gets the persisted maximum-starter-BST preference key.
    /// </summary>
    internal const string MaximumStarterBaseStatTotalPreferenceKey = "maximum_starter_base_stat_total";

    /// <summary>
    /// Gets the persisted evolution-graph expansion-depth preference key.
    /// </summary>
    internal const string EvolutionGraphExpansionDepthPreferenceKey = "evolution_graph_expansion_depth";

    /// <summary>
    /// Gets the persisted evolution-graph nodes-per-row preference key.
    /// </summary>
    internal const string EvolutionGraphNodesPerRowPreferenceKey = "evolution_graph_nodes_per_row";

    /// <summary>
    /// Gets the fallback application version.
    /// </summary>
    internal const string DefaultVersion = "0.1.0";

    /// <summary>
    /// Gets the command-line switch that requests debug access.
    /// </summary>
    internal const string DebugArgument = "--debug";

    /// <summary>
    /// Gets the bundled primary font filename.
    /// </summary>
    internal const string FontFile = "OpenSans-Regular.ttf";

    /// <summary>
    /// Gets the primary font alias.
    /// </summary>
    internal const string FontAlias = "TrackerSans";

    /// <summary>
    /// Gets the official WebView2 Runtime download page.
    /// </summary>
    internal const string WebView2DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/#download-section";
}
