namespace Ironmon.Tracker.App;

/// <summary>
/// Stores tracker-local preferences for opening the game alongside the tracker.
/// </summary>
public static class TrackerStartupSettings
{
    /// <summary>
    /// Gets or sets whether the next tracker launch also starts its installed game.
    /// </summary>
    public static bool AutoStartGame
    {
        get => Preferences.Default.Get(TrackerApplicationConstants.AutoStartGamePreferenceKey, false);
        set => Preferences.Default.Set(TrackerApplicationConstants.AutoStartGamePreferenceKey, value);
    }
}
