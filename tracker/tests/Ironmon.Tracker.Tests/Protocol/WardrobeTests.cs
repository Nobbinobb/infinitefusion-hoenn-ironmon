using Ironmon.Tracker.Connection.Wardrobe;
using Ironmon.Tracker.Protocol.Wardrobe;

namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Verifies wardrobe session isolation, recovery and complete native appearance serialization.
/// </summary>
public sealed class WardrobeTests
{
    private const string _game = "wardrobe-game";
    private const string _otherGame = "other-game";
    private const string _wardrobe = "11111111111111111111111111111111";
    private const string _outfit = "outfit";
    private const string _hair = "1_hair";
    private const string _key = "clothes:outfit";

    /// <summary>
    /// Verifies empty accessories remain explicit null slots in full appearance changes.
    /// </summary>
    [Fact]
    public void AppearanceIncludesEmptyAccessorySlotsAndExactIntegerDyes()
    {
        WardrobeAppearancePayload appearance = new() { Clothes = _outfit, Hair = _hair, HairColor = 137 };
        JsonElement payload = TrackerJson.SerializePayload(appearance);
        Assert.Equal(JsonValueKind.Null, payload.GetProperty(WardrobeProtocol.Hat).ValueKind);
        Assert.Equal(JsonValueKind.Null, payload.GetProperty(WardrobeProtocol.Hat2).ValueKind);
        Assert.Equal(137, payload.GetProperty(WardrobeProtocol.HairColor).GetInt32());
        Assert.Equal(appearance, payload.Deserialize<WardrobeAppearancePayload>(TrackerJson.Options));
    }

    /// <summary>
    /// Verifies edits cannot open a session and stale or foreign events cannot overwrite a live draft.
    /// </summary>
    /// <returns>Completion after rejected operations and connection transitions have been checked.</returns>
    [Fact]
    public async Task GameOwnsOpeningAndReconnectRetainsOnlyItsAcknowledgedSession()
    {
        using TrackerRequestSession requests = new(new TrackerDiagnosticsStore());
        TrackerWardrobeClient client = new(requests);
        GameHandshakePayload game = new(_game, _game, true, false, _game, null, null) { SessionId = _game, WardrobeProtocolVersion = 1 };
        client.Connect(game);
        Assert.Null(client.Snapshot);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ExecuteAsync(WardrobeAction.Change, new()));
        WardrobeSnapshotPayload current = new()
        {
            ProtocolVersion = 1, GameSessionId = _game, WardrobeId = _wardrobe, Revision = 3, ProfileRevision = 2, Open = true,
            Appearance = new() { Clothes = _outfit, Hair = _hair }, Catalog = [new() { Key = _key }]
        };
        client.Observe(current, game);
        client.Observe(current with { Revision = 2, Catalog = null }, game);
        Assert.Same(current, client.Snapshot);
        client.Observe(current with { GameSessionId = _otherGame, Revision = 9 }, game);
        Assert.Same(current, client.Snapshot);
        client.Observe(current with { Revision = 4, Catalog = null }, game);
        Assert.Single(client.Catalog);
        client.Disconnect();
        Assert.False(client.Connected);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ExecuteAsync(WardrobeAction.Confirm));
        client.Connect(game);
        Assert.True(client.Connected);
        Assert.Equal(4, client.Snapshot!.Revision);
        client.Connect(new(_game, _game, true, false, _game, null, null) { SessionId = _otherGame, WardrobeProtocolVersion = 1 });
        Assert.Null(client.Snapshot);
        Assert.Empty(client.Catalog);
    }

    /// <summary>
    /// Verifies unbounded or non-PNG data cannot become a WebView image source.
    /// </summary>
    [Fact]
    public void NativeImagesMustBeBoundedPngData()
    {
        Assert.Throws<TrackerProtocolException>(() => TrackerWardrobeClient.ImageSource(new() { Width = 513, Height = 10 }));
        Assert.Throws<TrackerProtocolException>(() => TrackerWardrobeClient.ImageSource(new() { Width = 10, Height = 10, PngBase64 = _game }));
        Assert.Throws<TrackerProtocolException>(() => TrackerWardrobeClient.ImageSource(new() { Width = 10, Height = 10, PngBase64 = Convert.ToBase64String([1, 2, 3]) }));
    }
}
