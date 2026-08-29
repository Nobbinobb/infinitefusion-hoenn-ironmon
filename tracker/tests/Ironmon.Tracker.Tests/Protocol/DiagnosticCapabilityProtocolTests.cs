namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Verifies bounded and backward-compatible diagnostic capability negotiation payloads.
/// </summary>
public sealed class DiagnosticCapabilityProtocolTests
{
    /// <summary>
    /// Initializes the diagnostic capability protocol tests.
    /// </summary>
    public DiagnosticCapabilityProtocolTests()
    {
    }

    /// <summary>
    /// Verifies both handshake directions preserve their independent capability lists.
    /// </summary>
    [Fact]
    public void HandshakeCapabilityListsRoundTrip()
    {
        GameHandshakePayload game = new("6.8.0", "0.7.4", true, false, TrackerTestPaths.GameRoot, "run-1", null, [DiagnosticCapabilities.RunSeed]);
        TrackerHandshakePayload tracker = new("0.7.4", false, diagnosticCapabilities: [DiagnosticCapabilities.RunSeed]);

        GameHandshakePayload restoredGame = TrackerJson.DeserializePayload<GameHandshakePayload>(TrackerJson.SerializePayload(game));
        TrackerHandshakePayload restoredTracker = TrackerJson.DeserializePayload<TrackerHandshakePayload>(TrackerJson.SerializePayload(tracker));

        Assert.Equal(DiagnosticCapabilities.RunSeed, Assert.Single(restoredGame.SupportedDiagnosticCapabilities));
        Assert.Equal(DiagnosticCapabilities.RunSeed, Assert.Single(restoredTracker.DiagnosticCapabilities));
    }

    /// <summary>
    /// Verifies handshakes from 0.7.3 peers deserialize with no named grants.
    /// </summary>
    [Fact]
    public void LegacyHandshakesDefaultToEmptyCapabilityLists()
    {
        object legacyGame = new
        {
            game_version = "6.8.0",
            ironmon_version = "0.7.3",
            ironmon_active = true,
            debug_available = false,
            game_root = TrackerTestPaths.GameRoot,
            run_id = (string?)null,
            battle_id = (string?)null
        };
        object legacyTracker = new { tracker_version = "0.7.3", debug_requested = false };

        GameHandshakePayload restoredGame = TrackerJson.DeserializePayload<GameHandshakePayload>(TrackerJson.SerializePayload(legacyGame));
        TrackerHandshakePayload restoredTracker = TrackerJson.DeserializePayload<TrackerHandshakePayload>(TrackerJson.SerializePayload(legacyTracker));

        Assert.Empty(restoredGame.SupportedDiagnosticCapabilities);
        Assert.Empty(restoredTracker.DiagnosticCapabilities);
    }

    /// <summary>
    /// Verifies duplicate and oversized capability lists fail closed during construction.
    /// </summary>
    [Fact]
    public void HandshakesRejectMalformedCapabilityLists()
    {
        string[] duplicate = [DiagnosticCapabilities.RunSeed, DiagnosticCapabilities.RunSeed];
        string[] oversized = [.. Enumerable.Repeat(DiagnosticCapabilities.RunSeed, DiagnosticCapabilityProtocolConstants.MaximumCapabilityCount + 1)];
        string longIdentifier = new('x', DiagnosticCapabilityProtocolConstants.MaximumCapabilityIdLength + 1);

        Assert.Throws<ArgumentException>(() => new GameHandshakePayload("6.8.0", "0.7.4", true, false, TrackerTestPaths.GameRoot, null, null, duplicate));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrackerHandshakePayload("0.7.4", false, diagnosticCapabilities: oversized));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrackerHandshakePayload("0.7.4", false, diagnosticCapabilities: [longIdentifier]));
    }
}
