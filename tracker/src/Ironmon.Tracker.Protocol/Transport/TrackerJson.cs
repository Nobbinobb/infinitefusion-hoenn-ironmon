using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ironmon.Tracker.Protocol.Transport;

/// <summary>
/// Provides the canonical JSON configuration for tracker protocol messages.
/// </summary>
public static class TrackerJson
{
    /// <summary>
    /// Gets the shared protocol serializer options.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>
    /// Converts a strongly typed payload into a detached JSON element.
    /// </summary>
    /// <typeparam name="TPayload">The payload type.</typeparam>
    /// <param name="payload">The payload to serialize.</param>
    /// <returns>A detached JSON representation of the payload.</returns>
    /// <exception cref="ArgumentNullException">Thrown when payload is null.</exception>
    public static JsonElement SerializePayload<TPayload>(TPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return JsonSerializer.SerializeToElement(payload, Options);
    }

    /// <summary>
    /// Converts a protocol payload into its strongly typed representation.
    /// </summary>
    /// <typeparam name="TPayload">The payload type.</typeparam>
    /// <param name="payload">The JSON payload to deserialize.</param>
    /// <returns>The deserialized payload.</returns>
    /// <exception cref="TrackerProtocolException">Thrown when the payload is malformed or null.</exception>
    public static TPayload DeserializePayload<TPayload>(JsonElement payload)
    {
        try
        {
            TPayload? result = payload.Deserialize<TPayload>(Options) ?? throw new TrackerProtocolException("The tracker payload was null.");
            return result;
        }
        catch (JsonException exception)
        {
            throw new TrackerProtocolException("The tracker payload contains invalid JSON.", exception);
        }
    }

    /// <summary>
    /// Creates the canonical serializer options.
    /// </summary>
    /// <returns>The configured serializer options.</returns>
    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}
