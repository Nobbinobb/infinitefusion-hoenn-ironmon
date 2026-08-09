namespace Ironmon.Tracker.App;

/// <summary>
/// Defines stable native application configuration values.
/// </summary>
internal static class TrackerApplicationConstants
{
    /// <summary>
    /// Gets the initial window width.
    /// </summary>
    internal const double DefaultWindowWidth = 500;

    /// <summary>
    /// Gets the initial window height.
    /// </summary>
    internal const double DefaultWindowHeight = 860;

    /// <summary>
    /// Gets the minimum supported window width.
    /// </summary>
    internal const double MinimumWindowWidth = 360;

    /// <summary>
    /// Gets the minimum supported window height.
    /// </summary>
    internal const double MinimumWindowHeight = 520;

    /// <summary>
    /// Gets the persisted window-width preference key.
    /// </summary>
    internal const string WindowWidthPreferenceKey = "tracker_window_width";

    /// <summary>
    /// Gets the persisted window-height preference key.
    /// </summary>
    internal const string WindowHeightPreferenceKey = "tracker_window_height";

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
