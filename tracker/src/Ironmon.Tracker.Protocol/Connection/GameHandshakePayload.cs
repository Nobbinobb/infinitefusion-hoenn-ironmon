namespace Ironmon.Tracker.Protocol.Connection;

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
    /// <param name="supportedDiagnosticCapabilities">The named diagnostic capabilities implemented by the game, or <see langword="null"/> for an empty list.</param>
    /// <exception cref="ArgumentException">Thrown when a required value is empty or a diagnostic capability is empty or duplicated.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the capability list or an identifier exceeds its protocol bound.</exception>
    public GameHandshakePayload(string gameVersion, string ironmonVersion, bool ironmonActive, bool debugAvailable, string gameRoot, string? runId, string? battleId, IReadOnlyList<string>? supportedDiagnosticCapabilities = null)
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
        SupportedDiagnosticCapabilities = ValidateDiagnosticCapabilities(supportedDiagnosticCapabilities);
    }

    /// <summary>
    /// Gets or initializes the game-management protocol version.
    /// </summary>
    public int ManagementProtocolVersion { get; init; }

    /// <summary>
    /// Gets or initializes the current game process session identifier.
    /// </summary>
    public string? SessionId { get; init; }

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
    /// Gets or initializes whether this runtime supports temporary tracker input overrides.
    /// </summary>
    public bool ControlOverridesSupported { get; init; }

    /// <summary>
    /// Gets or initializes the game's lowercase SHA-256 catalogue fingerprint offered for stateless move-display negotiation.
    /// </summary>
    /// <remarks>
    /// An absent fingerprint leaves the feature disabled. The tracker must confirm an exact compatible match before the game defers displays.
    /// </remarks>
    public string? SimpleMovePowerCatalogueFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes whether the game supports compact species catalogues for tracker-side name search.
    /// </summary>
    public bool PokemonNameCatalogSupported { get; init; }

    /// <summary>
    /// Gets or initializes the numeric defense-profile facts version offered by the game, or zero when unavailable.
    /// </summary>
    public int DefenseDamageProfileVersion { get; init; }

    /// <summary>
    /// Gets or initializes the trainer-location assignment version supported by the game, or zero for native Ruby lookup.
    /// </summary>
    public int TrainerLocationIndexVersion { get; init; }

    /// <summary>
    /// Gets or initializes the optional gameplay reverse-pair assignment version, or zero for Ruby preparation.
    /// </summary>
    public int GameplayFusionPairingVersion { get; init; }

    /// <summary>
    /// Gets the current run identifier when one exists.
    /// </summary>
    public string? RunId { get; }

    /// <summary>
    /// Gets the current battle identifier when one exists.
    /// </summary>
    public string? BattleId { get; }

    /// <summary>
    /// Gets the named diagnostic capabilities implemented by the game.
    /// </summary>
    public IReadOnlyList<string> SupportedDiagnosticCapabilities { get; }

    /// <summary>
    /// Validates and copies one bounded capability list.
    /// </summary>
    /// <param name="capabilities">The optional incoming identifiers.</param>
    /// <returns>A copy of the validated identifiers, or an empty list when <paramref name="capabilities"/> is <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when one identifier is malformed or duplicated.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the list or one identifier exceeds its protocol bound.</exception>
    private static IReadOnlyList<string> ValidateDiagnosticCapabilities(IReadOnlyList<string>? capabilities)
    {
        if (capabilities is null)
            return [];

        ArgumentOutOfRangeException.ThrowIfGreaterThan(capabilities.Count, DiagnosticCapabilityProtocolConstants.MaximumCapabilityCount);
        HashSet<string> unique = new(StringComparer.Ordinal);
        foreach (string capability in capabilities)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(capability);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(capability.Length, DiagnosticCapabilityProtocolConstants.MaximumCapabilityIdLength);
            if (!unique.Add(capability))
                throw new ArgumentException("Diagnostic capability identifiers must be unique.", nameof(capabilities));
        }

        return [.. capabilities];
    }
}
