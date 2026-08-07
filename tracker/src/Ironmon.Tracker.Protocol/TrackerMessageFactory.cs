namespace Ironmon.Tracker.Protocol;

/// <summary>
/// Creates valid tracker protocol envelopes with canonical defaults.
/// </summary>
public static class TrackerMessageFactory
{
    /// <summary>
    /// Creates an event message.
    /// </summary>
    /// <typeparam name="TPayload">The event payload type.</typeparam>
    /// <param name="eventName">The stable event name.</param>
    /// <param name="sequence">The event sequence number.</param>
    /// <param name="payload">The event payload.</param>
    /// <param name="runId">The current run identifier.</param>
    /// <param name="battleId">The current battle identifier.</param>
    /// <returns>A validated event message.</returns>
    public static TrackerMessage CreateEvent<TPayload>(string eventName, long sequence, TPayload payload, string? runId = null, string? battleId = null)
    {
        TrackerMessage message = new()
        {
            SchemaVersion = TrackerProtocol.CurrentSchemaVersion,
            Type = TrackerMessageType.Event,
            Event = eventName,
            RunId = runId,
            BattleId = battleId,
            Sequence = sequence,
            SentAt = DateTimeOffset.UtcNow,
            Payload = TrackerJson.SerializePayload(payload)
        };

        TrackerMessageValidator.Validate(message);
        return message;
    }

    /// <summary>
    /// Creates a request message.
    /// </summary>
    /// <typeparam name="TPayload">The request payload type.</typeparam>
    /// <param name="requestId">The identifier used to correlate the response.</param>
    /// <param name="command">The stable command name.</param>
    /// <param name="payload">The request payload.</param>
    /// <param name="runId">The current run identifier.</param>
    /// <param name="battleId">The current battle identifier.</param>
    /// <returns>A validated request message.</returns>
    public static TrackerMessage CreateRequest<TPayload>(string requestId, string command, TPayload payload, string? runId = null, string? battleId = null)
    {
        TrackerMessage message = new()
        {
            SchemaVersion = TrackerProtocol.CurrentSchemaVersion,
            Type = TrackerMessageType.Request,
            Command = command,
            RequestId = requestId,
            RunId = runId,
            BattleId = battleId,
            SentAt = DateTimeOffset.UtcNow,
            Payload = TrackerJson.SerializePayload(payload)
        };

        TrackerMessageValidator.Validate(message);
        return message;
    }

    /// <summary>
    /// Creates a successful response message.
    /// </summary>
    /// <typeparam name="TPayload">The response payload type.</typeparam>
    /// <param name="requestId">The identifier of the corresponding request.</param>
    /// <param name="payload">The response payload.</param>
    /// <param name="runId">The current run identifier.</param>
    /// <param name="battleId">The current battle identifier.</param>
    /// <returns>A validated successful response.</returns>
    public static TrackerMessage CreateResponse<TPayload>(string requestId, TPayload payload, string? runId = null, string? battleId = null)
    {
        TrackerMessage message = new()
        {
            SchemaVersion = TrackerProtocol.CurrentSchemaVersion,
            Type = TrackerMessageType.Response,
            RequestId = requestId,
            RunId = runId,
            BattleId = battleId,
            SentAt = DateTimeOffset.UtcNow,
            Success = true,
            Payload = TrackerJson.SerializePayload(payload)
        };

        TrackerMessageValidator.Validate(message);
        return message;
    }

    /// <summary>
    /// Creates a failed response message.
    /// </summary>
    /// <param name="requestId">The identifier of the corresponding request.</param>
    /// <param name="error">The protocol error.</param>
    /// <param name="runId">The current run identifier.</param>
    /// <param name="battleId">The current battle identifier.</param>
    /// <returns>A validated failed response.</returns>
    public static TrackerMessage CreateErrorResponse(string requestId, TrackerProtocolError error, string? runId = null, string? battleId = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        Dictionary<string, object?> emptyPayload = [];
        TrackerMessage message = new()
        {
            SchemaVersion = TrackerProtocol.CurrentSchemaVersion,
            Type = TrackerMessageType.Response,
            RequestId = requestId,
            RunId = runId,
            BattleId = battleId,
            SentAt = DateTimeOffset.UtcNow,
            Success = false,
            Error = error,
            Payload = TrackerJson.SerializePayload(emptyPayload)
        };

        TrackerMessageValidator.Validate(message);
        return message;
    }
}
