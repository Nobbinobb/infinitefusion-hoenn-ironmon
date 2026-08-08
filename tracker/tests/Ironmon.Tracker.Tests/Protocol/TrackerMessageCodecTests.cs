using System.Text;

namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Verifies tracker message creation, validation, JSON, and NDJSON framing.
/// </summary>
public sealed class TrackerMessageCodecTests
{
    /// <summary>
    /// Initializes the tracker message codec tests.
    /// </summary>
    public TrackerMessageCodecTests()
    {
    }

    /// <summary>
    /// Verifies that event messages round-trip with canonical snake-case JSON.
    /// </summary>
    [Fact]
    public void EventRoundTripsWithCanonicalJsonNames()
    {
        Dictionary<string, object?> payload = new()
        {
            ["pokemon_name"] = "Nidoran",
            ["level"] = 5
        };

        TrackerMessage message = TrackerMessageFactory.CreateEvent("player_sent_out", 27, payload, "run-123", "battle-8");
        string json = TrackerMessageCodec.Serialize(message);
        TrackerMessage result = TrackerMessageCodec.Deserialize(json);

        Assert.Contains("\"schema_version\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"event\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", json, StringComparison.Ordinal);
        Assert.Equal("player_sent_out", result.Event);
        Assert.Equal("Nidoran", result.Payload.GetProperty("pokemon_name").GetString());
    }

    /// <summary>
    /// Verifies that requests without correlation identifiers are rejected.
    /// </summary>
    [Fact]
    public void ValidatorRejectsRequestWithoutRequestId()
    {
        Dictionary<string, object?> payload = [];
        TrackerMessage message = new()
        {
            SchemaVersion = TrackerProtocol.CurrentSchemaVersion,
            Type = TrackerMessageType.Request,
            Command = "current_state",
            SentAt = DateTimeOffset.UtcNow,
            Payload = TrackerJson.SerializePayload(payload)
        };

        TrackerProtocolException exception = Assert.Throws<TrackerProtocolException>(() => TrackerMessageValidator.Validate(message));
        Assert.Contains("request_id", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the persistent reader preserves multiple buffered messages.
    /// </summary>
    [Fact]
    public async Task ReaderReadsConsecutiveNdjsonMessages()
    {
        Dictionary<string, int> firstPayload = new() { ["value"] = 1 };
        Dictionary<string, int> secondPayload = new() { ["value"] = 2 };
        TrackerMessage first = TrackerMessageFactory.CreateEvent("first", 1, firstPayload);
        TrackerMessage second = TrackerMessageFactory.CreateEvent("second", 2, secondPayload);
        string ndjson = $"{TrackerMessageCodec.Serialize(first)}\n{TrackerMessageCodec.Serialize(second)}\n";
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(ndjson));
        using TrackerMessageReader reader = new(stream);

        TrackerMessage? firstResult = await reader.ReadAsync();
        TrackerMessage? secondResult = await reader.ReadAsync();
        TrackerMessage? endResult = await reader.ReadAsync();

        Assert.Equal("first", firstResult?.Event);
        Assert.Equal("second", secondResult?.Event);
        Assert.Null(endResult);
    }

    /// <summary>
    /// Verifies that the persistent writer uses one line-feed-delimited JSON frame.
    /// </summary>
    [Fact]
    public async Task WriterWritesOneLineFeedDelimitedMessage()
    {
        Dictionary<string, object?> payload = [];
        TrackerMessage message = TrackerMessageFactory.CreateRequest("request-1", "current_state", payload);
        using MemoryStream stream = new();
        await using TrackerMessageWriter writer = new(stream, leaveOpen: true);
        await writer.WriteAsync(message);

        string result = Encoding.UTF8.GetString(stream.ToArray());
        Assert.EndsWith("\n", result, StringComparison.Ordinal);
        Assert.False(result.EndsWith("\r\n", StringComparison.Ordinal));
        Assert.Equal(1, result.Count(character => character == '\n'));
    }

    /// <summary>
    /// Verifies that failed responses carry structured protocol errors.
    /// </summary>
    [Fact]
    public void FailedResponseRoundTripsWithError()
    {
        TrackerProtocolError error = new("incompatible_run", "The run cannot be reproduced.");
        TrackerMessage message = TrackerMessageFactory.CreateErrorResponse("request-2", error);

        string json = TrackerMessageCodec.Serialize(message);
        TrackerMessage result = TrackerMessageCodec.Deserialize(json);

        Assert.False(result.Success);
        Assert.Equal("incompatible_run", result.Error?.Code);
    }
}
