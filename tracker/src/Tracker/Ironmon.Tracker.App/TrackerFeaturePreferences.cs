using System.Text.Json;

namespace Ironmon.Tracker.App;

/// <summary>
/// Persists the independent game feature switches for future connections.
/// </summary>
public static class TrackerFeaturePreferences
{
    private const string PreferenceKey = "feature_settings";

    /// <summary>
    /// Reads saved switches, enabling all features for an absent or invalid preference.
    /// </summary>
    /// <returns>The saved feature switches with enabled defaults for missing fields.</returns>
    public static TrackerFeatureSettings Load()
    {
        string saved = Preferences.Default.Get(PreferenceKey, string.Empty);
        if (string.IsNullOrEmpty(saved))
            return new();

        try
        {
            return JsonSerializer.Deserialize<TrackerFeatureSettings>(saved, TrackerJson.Options) ?? new();
        }
        catch (Exception error) when (error is JsonException or TrackerProtocolException)
        {
            return new();
        }
    }

    /// <summary>
    /// Saves one complete set of switches without changing feature configuration.
    /// </summary>
    /// <param name="features">The switches to retain across tracker restarts.</param>
    public static void Save(TrackerFeatureSettings features)
        => Preferences.Default.Set(PreferenceKey, TrackerJson.SerializePayload(features).GetRawText());
}
