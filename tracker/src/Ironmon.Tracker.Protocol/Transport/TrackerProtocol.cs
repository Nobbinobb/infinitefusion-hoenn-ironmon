namespace Ironmon.Tracker.Protocol.Transport;

/// <summary>
/// Defines shared constants for the Ironmon tracker protocol.
/// </summary>
public static class TrackerProtocol
{
    /// <summary>
    /// Gets the IPv4 loopback host used by both protocol peers.
    /// </summary>
    public const string LoopbackHost = "127.0.0.1";

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

    /// <summary>
    /// Gets the newline delimiter used between protocol messages.
    /// </summary>
    public const string MessageDelimiter = "\n";

    /// <summary>
    /// Gets the minimum accepted Pokemon search page size.
    /// </summary>
    public const int MinimumSearchPageSize = 1;

    /// <summary>
    /// Gets the default number of Pokemon search results requested per page.
    /// </summary>
    public const int DefaultSearchPageSize = 20;

    /// <summary>
    /// Gets the maximum number of Pokemon search results accepted per page.
    /// </summary>
    public const int MaximumSearchPageSize = 50;

    /// <summary>
    /// Gets the fixed lookup level retained for compatibility with earlier game scripts.
    /// </summary>
    public const int CompatibilityLookupLevel = 100;

    /// <summary>
    /// Gets the player-fusion generator version assumed by legacy recipe payloads.
    /// </summary>
    public const int DefaultPlayerFusionGeneratorVersion = 2;

    /// <summary>
    /// Gets the sequence number used by tracker-originated lifecycle events.
    /// </summary>
    public const long InitialEventSequence = 0;

    /// <summary>
    /// Gets the compact format used for request identifiers.
    /// </summary>
    public const string RequestIdFormat = "N";

    /// <summary>
    /// Gets the production handshake timeout in seconds.
    /// </summary>
    public const int HandshakeTimeoutSeconds = 5;

    /// <summary>
    /// Gets the correlated game-request timeout in seconds.
    /// </summary>
    public const int RequestTimeoutSeconds = 10;
}
