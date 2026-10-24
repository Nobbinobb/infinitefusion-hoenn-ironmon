using System.Text.Json;

namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Checks feature defaults and independent configuration across protocol serialization.
/// </summary>
public sealed class TrackerFeatureSettingsTests
{
    private const string Version = "test";
    private const string PartialFeatures = "{\"repel_counter\":false}";
    private const string SavedFavorite = "BULBASAUR";

    /// <summary>
    /// Keeps omitted switches enabled when reading older or partial preferences.
    /// </summary>
    [Fact]
    public void MissingSwitchesRemainEnabled()
    {
        TrackerFeatureSettings restored = JsonSerializer.Deserialize<TrackerFeatureSettings>(PartialFeatures, TrackerJson.Options)!;
        Assert.Equal(new TrackerFeatureSettings() with { RepelCounter = false }, restored);
        Assert.True(new TrackerSettingsPayload().AutoSelectStarter);
        Assert.True(new TrackerHandshakePayload(Version, false).AutoSelectStarter);
    }

    /// <summary>
    /// Preserves disabled switches alongside their untouched favorite list and BST ceiling.
    /// </summary>
    [Fact]
    public void DisabledFeaturesKeepTheirConfiguration()
    {
        TrackerFeatureSettings disabled = new() { AchievementPopups = false, RepelCounter = false, SpeedIndicator = false, BattleQuickItems = false, QuickRun = false, FavoriteClause = false, EnforceBstLimit = false };
        TrackerSettingsPayload saved = new() { Features = disabled, MaximumStarterBaseStatTotal = 400, FavoriteSpeciesIds = [SavedFavorite] };
        TrackerSettingsPayload restored = TrackerJson.DeserializePayload<TrackerSettingsPayload>(TrackerJson.SerializePayload(saved));
        Assert.Equal(disabled, restored.Features);
        Assert.Equal(400, restored.MaximumStarterBaseStatTotal);
        Assert.Equal([SavedFavorite], restored.FavoriteSpeciesIds);
        TrackerHandshakePayload handshake = new(Version, false) { Features = disabled };
        TrackerHandshakePayload reconnected = TrackerJson.DeserializePayload<TrackerHandshakePayload>(TrackerJson.SerializePayload(handshake));
        Assert.Equal(disabled, reconnected.Features);
    }
}
