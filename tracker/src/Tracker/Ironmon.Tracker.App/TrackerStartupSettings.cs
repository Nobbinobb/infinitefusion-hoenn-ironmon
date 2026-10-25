namespace Ironmon.Tracker.App;

/// <summary>
/// Stores tracker-local startup and live gameplay preferences.
/// </summary>
public static class TrackerStartupSettings
{
    private const string AutomaticResetPreferenceKey = "automatic_reset";

    /// <summary>
    /// Gets or sets whether the next tracker launch begins minimized.
    /// </summary>
    public static bool StartMinimized
    {
        get => Preferences.Default.Get(TrackerApplicationConstants.StartMinimizedPreferenceKey, false);
        set => Preferences.Default.Set(TrackerApplicationConstants.StartMinimizedPreferenceKey, value);
    }

    /// <summary>
    /// Gets or sets whether a lost attempt resets automatically.
    /// </summary>
    public static bool AutomaticReset
    {
        get => Preferences.Default.Get(AutomaticResetPreferenceKey, true);
        set => Preferences.Default.Set(AutomaticResetPreferenceKey, value);
    }
}
