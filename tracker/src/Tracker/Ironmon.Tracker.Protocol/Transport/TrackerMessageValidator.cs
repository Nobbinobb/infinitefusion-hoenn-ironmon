using System.Text.Json;

namespace Ironmon.Tracker.Protocol.Transport;

/// <summary>
/// Validates required fields and invariants in tracker protocol envelopes.
/// </summary>
public static class TrackerMessageValidator
{
    /// <summary>
    /// Validates a tracker protocol message.
    /// </summary>
    /// <param name="message">The message to validate.</param>
    /// <exception cref="ArgumentNullException">Thrown when message is null.</exception>
    /// <exception cref="TrackerProtocolException">Thrown when the message is invalid.</exception>
    public static void Validate(TrackerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.SchemaVersion != TrackerProtocol.CurrentSchemaVersion)
            throw new TrackerProtocolException($"Unsupported tracker schema version {message.SchemaVersion}.");

        if (message.SentAt == default)
            throw new TrackerProtocolException("A tracker message requires sent_at.");

        if (message.Payload.ValueKind == JsonValueKind.Undefined)
            throw new TrackerProtocolException("A tracker message requires payload.");

        Action<TrackerMessage> validation = GetValidator(message.Type);
        validation(message);
    }

    /// <summary>
    /// Gets the validator for a semantic tracker message type.
    /// </summary>
    /// <param name="messageType">The semantic message type.</param>
    /// <returns>The validator for the message type.</returns>
    /// <exception cref="TrackerProtocolException">Thrown when the message type is invalid.</exception>
    private static Action<TrackerMessage> GetValidator(TrackerMessageType messageType) => messageType switch
    {
        TrackerMessageType.Event => ValidateEvent,
        TrackerMessageType.Request => ValidateRequest,
        TrackerMessageType.Response => ValidateResponse,
        _ => throw new TrackerProtocolException("The tracker message type is invalid.")
    };

    /// <summary>
    /// Validates fields specific to an event message.
    /// </summary>
    /// <param name="message">The event message.</param>
    /// <exception cref="TrackerProtocolException">Thrown when an event field is invalid.</exception>
    private static void ValidateEvent(TrackerMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Event))
            throw new TrackerProtocolException("An event message requires event.");

        if (message.Sequence is null || message.Sequence < 0)
            throw new TrackerProtocolException("An event message requires a non-negative sequence.");
    }

    /// <summary>
    /// Validates fields specific to a request message.
    /// </summary>
    /// <param name="message">The request message.</param>
    /// <exception cref="TrackerProtocolException">Thrown when a request field is invalid.</exception>
    private static void ValidateRequest(TrackerMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Command))
            throw new TrackerProtocolException("A request message requires command.");

        if (string.IsNullOrWhiteSpace(message.RequestId))
            throw new TrackerProtocolException("A request message requires request_id.");
    }

    /// <summary>
    /// Validates fields specific to a response message.
    /// </summary>
    /// <param name="message">The response message.</param>
    /// <exception cref="TrackerProtocolException">Thrown when a response field is invalid.</exception>
    private static void ValidateResponse(TrackerMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.RequestId))
            throw new TrackerProtocolException("A response message requires request_id.");

        if (message.Success is null)
            throw new TrackerProtocolException("A response message requires success.");

        if (message.Success == false && message.Error is null)
            throw new TrackerProtocolException("A failed response requires error.");

        if (message.Success == true && message.Error is not null)
            throw new TrackerProtocolException("A successful response cannot contain error.");
    }
}
