using System.Text.Json;

namespace Ironmon.Tracker.Protocol.Transport;

/// <summary>
/// Serializes and deserializes individual tracker protocol messages.
/// </summary>
public static class TrackerMessageCodec
{
    /// <summary>
    /// Serializes one validated message without a trailing newline.
    /// </summary>
    /// <param name="message">The message to serialize.</param>
    /// <returns>The canonical JSON representation.</returns>
    public static string Serialize(TrackerMessage message)
    {
        TrackerMessageValidator.Validate(message);
        return JsonSerializer.Serialize(message, TrackerJson.Options);
    }

    /// <summary>
    /// Deserializes and validates one message without a trailing newline.
    /// </summary>
    /// <param name="json">The JSON message.</param>
    /// <returns>The validated message.</returns>
    /// <exception cref="ArgumentException">Thrown when JSON is empty.</exception>
    /// <exception cref="TrackerProtocolException">Thrown when JSON is malformed or invalid.</exception>
    public static TrackerMessage Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        try
        {
            TrackerMessage? message = JsonSerializer.Deserialize<TrackerMessage>(json, TrackerJson.Options) ?? throw new TrackerProtocolException("The tracker message was null.");
            TrackerMessageValidator.Validate(message);
            return message;
        }
        catch (JsonException exception)
        {
            throw new TrackerProtocolException("The tracker message contains invalid JSON.", exception);
        }
    }
}
