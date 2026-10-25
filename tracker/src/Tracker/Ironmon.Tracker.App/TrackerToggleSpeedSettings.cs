using System.Text.Json;

namespace Ironmon.Tracker.App;

/// <summary>
/// Persists only complete toggle profiles independently of game saves and hold settings.
/// </summary>
public static class TrackerToggleSpeedSettings
{
    private const string PreferenceKey = "toggle_speed_profile";

    /// <summary>
    /// Loads the saved profile, recovering defaults if an older or damaged preference is invalid.
    /// </summary>
    /// <returns>A validated independent speed profile.</returns>
    public static ToggleSpeedConfiguration Load()
    {
        try
        {
            string json = Preferences.Default.Get(PreferenceKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return ToggleSpeedConfiguration.CreateDefault();

            ToggleSpeedConfiguration profile = JsonSerializer.Deserialize<ToggleSpeedConfiguration>(json, TrackerJson.Options) ?? throw new ArgumentException("The saved speed profile is empty.");
            ToggleSpeedValidation.Validate(profile);
            return profile;
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            return ToggleSpeedConfiguration.CreateDefault();
        }
    }

    /// <summary>
    /// Validates a complete profile before replacing the persisted preference.
    /// </summary>
    /// <param name="profile">The explicitly saved configuration.</param>
    public static void Save(ToggleSpeedConfiguration profile)
    {
        ToggleSpeedValidation.Validate(profile);
        Preferences.Default.Set(PreferenceKey, JsonSerializer.Serialize(profile, TrackerJson.Options));
    }
}
