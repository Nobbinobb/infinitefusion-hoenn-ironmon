namespace Ironmon.Tracker.App;

/// <summary>
/// Defines the localization resource layout used by the tracker application.
/// </summary>
public static class TrackerLocalizationConstants
{
    /// <summary>
    /// Gets the project-relative directory containing tracker text resources.
    /// </summary>
    public const string ResourcesPath = "Resources/Localization";

    /// <summary>
    /// Gets the known resource used to verify localization discovery at startup.
    /// </summary>
    public const string StartupProbeKey = "App.Native.AppTitle";

    /// <summary>
    /// Gets the error raised when the compiled localization resource cannot be discovered.
    /// </summary>
    public const string MissingResourceMessage = "The tracker localization resource could not be loaded.";
}
