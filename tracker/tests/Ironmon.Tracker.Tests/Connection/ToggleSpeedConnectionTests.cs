using System.Text;

namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Checks live settings acknowledgements before changing the reconnect profile.
/// </summary>
public sealed class ToggleSpeedConnectionTests
{
    private const string Version = "test";
    private const string ErrorCode = "invalid_speed";
    private const string JsonProperty = "toggle_speed";

    /// <summary>
    /// Commits an accepted profile, retains rejected profiles, and ignores unrelated settings updates.
    /// </summary>
    /// <param name="responseKind">Zero accepts, one rejects, two omits support, and three updates another setting.</param>
    /// <returns>The completed protocol exchange and committed-profile checks.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ReconnectProfileChangesOnlyAfterExplicitAcceptance(int responseKind)
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        using TrackerRequestSession session = new(new TrackerDiagnosticsStore());
        using MemoryStream bytes = new();
        await using TrackerMessageWriter writer = new(bytes, leaveOpen: true);
        session.Connect(writer);
        TrackerConnectionState state = new();
        TrackerConnectionOptions options = new(0, Version, false, TimeSpan.FromSeconds(2));
        ToggleSpeedConfiguration original = options.ToggleSpeed;
        ToggleSpeedConfiguration candidate = new() { Lanes = new() { Overworld = [original.Lanes.Overworld[0], new() { Id = original.Lanes.Overworld[1].Id, Nodes = [new() { Id = original.Lanes.Overworld[1].Nodes[0].Id, Value = 10 }] }, original.Lanes.Overworld[2]], Battle = original.Lanes.Battle }, Links = original.Links };
        TrackerRequestClient client = new(session, options, state, new AreaDiscoveryStore(new TrackerKnowledgeOptions(directory)));
        Task<TrackerSettingsPayload> pending = client.UpdateSettingsAsync(new() { ToggleSpeed = responseKind == 3 ? null : candidate });
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(5));
        while (bytes.Length == 0)
            await Task.Delay(10, cancellation.Token);

        Assert.Same(original, options.ToggleSpeed);
        TrackerMessage request = TrackerMessageCodec.Deserialize(Encoding.UTF8.GetString(bytes.ToArray()).Trim());
        Assert.Equal(responseKind != 3, request.Payload.TryGetProperty(JsonProperty, out _));
        TrackerMessage response = responseKind == 1
            ? TrackerMessageFactory.CreateErrorResponse(request.RequestId!, new TrackerProtocolError(ErrorCode, "The speed profile was rejected."))
            : TrackerMessageFactory.CreateResponse(request.RequestId!, new TrackerSettingsPayload { ToggleSpeed = responseKind is 0 or 3 ? candidate : null });

        Assert.True(session.TryComplete(response));
        if (responseKind is 1 or 2)
        {
            await Assert.ThrowsAsync<TrackerProtocolException>(() => pending);
        }
        else
        {
            await pending;
        }

        if (responseKind == 0)
        {
            Assert.Equal(10, options.ToggleSpeed.Lanes.Overworld[1].Nodes[0].Value);
        }
        else
        {
            Assert.Same(original, options.ToggleSpeed);
        }
    }
}
