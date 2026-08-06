using System.Text.Json;

namespace Ironmon.Tracker.Protocol;

/// <summary>
/// Represents the common envelope of one tracker protocol message.
/// </summary>
public sealed class TrackerMessage
{
    /// <summary>
    /// Initializes an empty message for protocol deserialization.
    /// </summary>
    public TrackerMessage()
    {
    }

    /// <summary>
    /// Gets or initializes the protocol schema version.
    /// </summary>
    public int SchemaVersion { get; init; }

    /// <summary>
    /// Gets or initializes the semantic message type.
    /// </summary>
    public TrackerMessageType Type { get; init; }

    /// <summary>
    /// Gets or initializes the event name for an event message.
    /// </summary>
    public string? Event { get; init; }

    /// <summary>
    /// Gets or initializes the command name for a request message.
    /// </summary>
    public string? Command { get; init; }

    /// <summary>
    /// Gets or initializes the identifier shared by a request and its response.
    /// </summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// Gets or initializes the current run identifier when one exists.
    /// </summary>
    public string? RunId { get; init; }

    /// <summary>
    /// Gets or initializes the current battle identifier when one exists.
    /// </summary>
    public string? BattleId { get; init; }

    /// <summary>
    /// Gets or initializes the event sequence number.
    /// </summary>
    public long? Sequence { get; init; }

    /// <summary>
    /// Gets or initializes the UTC time at which the message was created.
    /// </summary>
    public DateTimeOffset SentAt { get; init; }

    /// <summary>
    /// Gets or initializes whether a response completed successfully.
    /// </summary>
    public bool? Success { get; init; }

    /// <summary>
    /// Gets or initializes the error returned by a failed response.
    /// </summary>
    public TrackerProtocolError? Error { get; init; }

    /// <summary>
    /// Gets or initializes the type-specific message payload.
    /// </summary>
    public JsonElement Payload { get; init; }
}
