using System.Text.Json;

namespace Ironmon.Tracker.App;

/// <summary>
/// Stores reusable local rule templates independently of game saves.
/// </summary>
public static class RunProfileStore
{
    private const string PreferenceKey = "ironmon_rule_profiles";
    private const int MaximumProfiles = 32;

    /// <summary>
    /// Loads saved rule profiles, or a standard profile when the stored collection is missing or invalid.
    /// </summary>
    /// <returns>A new list containing the saved profiles or the standard profile.</returns>
    public static List<IronmonRuleProfile> Load()
    {
        try
        {
            string json = Preferences.Default.Get(PreferenceKey, string.Empty);
            List<IronmonRuleProfile>? profiles = string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<List<IronmonRuleProfile>>(json);
            if (profiles is { Count: > 0 and <= MaximumProfiles } && profiles.All(profile => !string.IsNullOrWhiteSpace(profile.Name) && profile.Rules is not null))
                return profiles;
        }
        catch (JsonException)
        {
        }

        return [new() { Name = "Standard", Rules = new() }];
    }

    /// <summary>
    /// Persists the local rule profiles without changing existing game saves.
    /// </summary>
    /// <param name="profiles">The complete collection of local profiles, containing at most 32 entries.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the collection contains more than 32 profiles.</exception>
    public static void Save(IReadOnlyList<IronmonRuleProfile> profiles)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(profiles.Count, MaximumProfiles);
        Preferences.Default.Set(PreferenceKey, JsonSerializer.Serialize(profiles));
    }
}

/// <summary>
/// Describes a named rule profile that can be copied into future Ironmon runs.
/// </summary>
public sealed record IronmonRuleProfile
{
    /// <summary>
    /// Gets or initializes the player-visible template name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the rules copied into future saves.
    /// </summary>
    public IronmonRunRules Rules { get; init; } = new();
}
