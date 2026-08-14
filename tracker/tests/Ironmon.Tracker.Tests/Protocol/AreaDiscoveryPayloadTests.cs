namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Verifies acknowledged area-discovery protocol payloads.
/// </summary>
public sealed class AreaDiscoveryPayloadTests
{
    /// <summary>
    /// Initializes area-discovery payload tests.
    /// </summary>
    public AreaDiscoveryPayloadTests()
    {
    }

    /// <summary>
    /// Verifies that a discovery package round-trips with stable snake-case fields.
    /// </summary>
    [Fact]
    public void DiscoveryPackageRoundTrips()
    {
        AreaDiscoveryPackagePayload package = new()
        {
            PackageId = "run-1:area:4",
            AreaId = "area:10",
            Category = AreaContentCategory.Encounter,
            EntryKeys = ["encounter:10:0:Land:2"]
        };

        JsonElement json = TrackerJson.SerializePayload(package);
        AreaDiscoveryPackagePayload result = TrackerJson.DeserializePayload<AreaDiscoveryPackagePayload>(json);

        Assert.Equal("run-1:area:4", result.PackageId);
        Assert.Equal("area:10", result.AreaId);
        Assert.Equal(AreaContentCategory.Encounter, result.Category);
        Assert.Equal("encounter:10:0:Land:2", Assert.Single(result.EntryKeys));
        Assert.True(json.TryGetProperty("entry_keys", out _));
    }

    /// <summary>
    /// Verifies that acknowledgment identity survives protocol serialization.
    /// </summary>
    [Fact]
    public void DiscoveryAcknowledgmentRoundTrips()
    {
        AreaDiscoveryAcknowledgmentPayload acknowledgment = new() { PackageId = "package-7" };

        AreaDiscoveryAcknowledgmentPayload result = TrackerJson.DeserializePayload<AreaDiscoveryAcknowledgmentPayload>(TrackerJson.SerializePayload(acknowledgment));

        Assert.Equal("package-7", result.PackageId);
    }
}
