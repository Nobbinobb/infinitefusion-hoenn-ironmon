namespace Ironmon.Tracker.Connection;

/// <summary>
/// Configures the local tracker listener and its handshake identity.
/// </summary>
public sealed class TrackerConnectionOptions
{
    /// <summary>
    /// Initializes tracker connection options.
    /// </summary>
    /// <param name="port">The IPv4 loopback port, or zero for an ephemeral test port.</param>
    /// <param name="trackerVersion">The tracker application version.</param>
    /// <param name="debugRequested">Whether the tracker requested development access.</param>
    /// <param name="handshakeTimeout">The maximum time allowed for the game handshake.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the port or timeout is invalid.</exception>
    /// <exception cref="ArgumentException">Thrown when the tracker version is empty.</exception>
    public TrackerConnectionOptions(int port, string trackerVersion, bool debugRequested, TimeSpan handshakeTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, ushort.MaxValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(trackerVersion);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(handshakeTimeout, TimeSpan.Zero);

        Port = port;
        TrackerVersion = trackerVersion;
        DebugRequested = debugRequested;
        HandshakeTimeout = handshakeTimeout;
    }

    /// <summary>
    /// Gets the IPv4 loopback listener port.
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// Gets the tracker application version reported to the game.
    /// </summary>
    public string TrackerVersion { get; }

    /// <summary>
    /// Gets whether the tracker requested development access.
    /// </summary>
    public bool DebugRequested { get; }

    /// <summary>
    /// Gets the maximum time allowed for the game handshake.
    /// </summary>
    public TimeSpan HandshakeTimeout { get; }
}
