namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Verifies tracker-owned starter setting serialization and validation.
/// </summary>
public sealed class TrackerSettingsPayloadTests
{
    /// <summary>
    /// Initializes the tracker-settings protocol tests.
    /// </summary>
    public TrackerSettingsPayloadTests()
    {
    }

    /// <summary>
    /// Verifies the optional inclusive maximum BST survives payload serialization.
    /// </summary>
    [Fact]
    public void MaximumStarterBaseStatTotalRoundTrips()
    {
        TrackerSettingsPayload settings = new() { AutoSelectStarter = true, MaximumStarterBaseStatTotal = 525 };

        TrackerSettingsPayload restored = TrackerJson.DeserializePayload<TrackerSettingsPayload>(TrackerJson.SerializePayload(settings));

        Assert.True(restored.AutoSelectStarter);
        Assert.Equal(525, restored.MaximumStarterBaseStatTotal);
    }

    /// <summary>
    /// Verifies handshake construction rejects an impossible generated BST ceiling.
    /// </summary>
    [Fact]
    public void HandshakeRejectsMaximumOutsideGeneratedRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrackerHandshakePayload("0.7.3", false, true, 29));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrackerHandshakePayload("0.7.3", false, true, 1531));
    }
}
