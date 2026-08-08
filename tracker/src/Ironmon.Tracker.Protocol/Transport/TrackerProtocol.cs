namespace Ironmon.Tracker.Protocol.Transport;

/// <summary>
/// Defines shared constants for the Ironmon tracker protocol.
/// </summary>
public static class TrackerProtocol
{
    /// <summary>
    /// Gets the current protocol schema version.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Gets the loopback TCP port reserved for the tracker.
    /// </summary>
    public const int Port = 38_521;

    /// <summary>
    /// Gets the maximum accepted size of one newline-delimited JSON message.
    /// </summary>
    public const int MaximumMessageCharacters = 1_048_576;
}
