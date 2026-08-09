namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Defines shared semantic UI values used by tracker components.
/// </summary>
internal static class TrackerUiConstants
{
    /// <summary>
    /// Gets the CSS class applied to a selected control.
    /// </summary>
    internal const string SelectedCssClass = "selected";

    /// <summary>
    /// Gets the maximum base-stat value represented by a full bar.
    /// </summary>
    internal const int MaximumBaseStat = 255;

    /// <summary>
    /// Gets the percentage represented by a full bar.
    /// </summary>
    internal const int FullPercentage = 100;

    /// <summary>
    /// Gets the base primary-view tab CSS class.
    /// </summary>
    internal const string ViewTabCssClass = "view-tab";

    /// <summary>
    /// Gets the selected primary-view tab CSS classes.
    /// </summary>
    internal const string SelectedViewTabCssClass = "view-tab selected";

    /// <summary>
    /// Gets the primary-view container CSS class.
    /// </summary>
    internal const string ViewTabsCssClass = "view-tabs";

    /// <summary>
    /// Gets the debug-enabled primary-view container CSS classes.
    /// </summary>
    internal const string DebugViewTabsCssClass = "view-tabs debug-enabled";

    /// <summary>
    /// Gets the base connection-indicator CSS class.
    /// </summary>
    internal const string ConnectionDotCssClass = "connection-dot";

    /// <summary>
    /// Gets the connected indicator CSS classes.
    /// </summary>
    internal const string ConnectedDotCssClass = "connection-dot connected";

    /// <summary>
    /// Gets the connection-error indicator CSS classes.
    /// </summary>
    internal const string ErrorDotCssClass = "connection-dot error";

    /// <summary>
    /// Gets the handshaking indicator CSS classes.
    /// </summary>
    internal const string HandshakingDotCssClass = "connection-dot handshaking";

    /// <summary>
    /// Gets the positive-difference CSS class.
    /// </summary>
    internal const string PositiveCssClass = "positive";

    /// <summary>
    /// Gets the negative-difference CSS class.
    /// </summary>
    internal const string NegativeCssClass = "negative";

    /// <summary>
    /// Gets the unchanged-difference CSS class.
    /// </summary>
    internal const string UnchangedCssClass = "unchanged";
}
