namespace Ironmon.Tracker.Protocol;

/// <summary>
/// Describes the game runtime during the initial tracker handshake.
/// </summary>
public sealed class GameHandshakePayload
{
    /// <summary>
    /// Initializes a game handshake payload.
    /// </summary>
    /// <param name="gameVersion">The Infinite Fusion version.</param>
    /// <param name="ironmonVersion">The Ironmon version.</param>
    /// <param name="ironmonActive">Whether Ironmon mode is active.</param>
    /// <param name="debugAvailable">Whether the game authorizes development access.</param>
    /// <param name="gameRoot">The absolute game installation directory.</param>
    /// <param name="runId">The current run identifier.</param>
    /// <param name="battleId">The current battle identifier.</param>
    /// <exception cref="ArgumentException">Thrown when a required value is empty.</exception>
    public GameHandshakePayload(string gameVersion, string ironmonVersion, bool ironmonActive, bool debugAvailable, string gameRoot, string? runId, string? battleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(ironmonVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);

        GameVersion = gameVersion;
        IronmonVersion = ironmonVersion;
        IronmonActive = ironmonActive;
        DebugAvailable = debugAvailable;
        GameRoot = gameRoot;
        RunId = runId;
        BattleId = battleId;
    }

    /// <summary>
    /// Gets the Infinite Fusion version.
    /// </summary>
    public string GameVersion { get; }

    /// <summary>
    /// Gets the Ironmon version.
    /// </summary>
    public string IronmonVersion { get; }

    /// <summary>
    /// Gets whether Ironmon mode is active.
    /// </summary>
    public bool IronmonActive { get; }

    /// <summary>
    /// Gets whether the game authorizes development access.
    /// </summary>
    public bool DebugAvailable { get; }

    /// <summary>
    /// Gets the absolute game installation directory.
    /// </summary>
    public string GameRoot { get; }

    /// <summary>
    /// Gets the current run identifier when one exists.
    /// </summary>
    public string? RunId { get; }

    /// <summary>
    /// Gets the current battle identifier when one exists.
    /// </summary>
    public string? BattleId { get; }
}
