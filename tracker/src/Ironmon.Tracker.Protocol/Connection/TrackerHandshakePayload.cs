namespace Ironmon.Tracker.Protocol.Connection;

/// <summary>
/// Describes the tracker application during the initial handshake.
/// </summary>
public sealed class TrackerHandshakePayload
{
    /// <summary>
    /// Gets or sets the live automatic-reset preference.
    /// </summary>
    public bool AutomaticReset { get; set; } = true;

    /// <summary>
    /// Initializes a tracker handshake payload.
    /// </summary>
    /// <param name="trackerVersion">The tracker application version.</param>
    /// <param name="debugRequested">Whether the tracker was launched in debug mode.</param>
    /// <param name="autoSelectStarter">Whether starter selection is controlled automatically.</param>
    /// <param name="maximumStarterBaseStatTotal">The inclusive generated-BST ceiling for automatic starter selection.</param>
    /// <param name="favoriteSpeciesIds">The normal species covered by the Favorite Clause, or <see langword="null"/> for an empty list.</param>
    /// <param name="diagnosticCapabilities">The effective named capabilities granted for this connection, or <see langword="null"/> for an empty list.</param>
    /// <exception cref="ArgumentException">Thrown when the tracker version or a capability identifier is empty, or a capability identifier is duplicated.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the maximum starter BST, capability count, or capability identifier length exceeds its supported bounds.</exception>
    public TrackerHandshakePayload(string trackerVersion, bool debugRequested, bool autoSelectStarter = false, int? maximumStarterBaseStatTotal = null, IReadOnlyList<string>? favoriteSpeciesIds = null, IReadOnlyList<string>? diagnosticCapabilities = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackerVersion);
        if (maximumStarterBaseStatTotal is not null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maximumStarterBaseStatTotal.Value, StarterSelectionConstants.MinimumBaseStatTotal);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumStarterBaseStatTotal.Value, StarterSelectionConstants.MaximumBaseStatTotal);
        }

        TrackerVersion = trackerVersion;
        DebugRequested = debugRequested;
        AutoSelectStarter = autoSelectStarter;
        MaximumStarterBaseStatTotal = maximumStarterBaseStatTotal;
        FavoriteSpeciesIds = favoriteSpeciesIds ?? [];
        DiagnosticCapabilities = ValidateDiagnosticCapabilities(diagnosticCapabilities);
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
    /// Gets or initializes whether this connection has negotiated an exact catalogue match for stateless move-display completion.
    /// </summary>
    public bool SimpleMovePowerSupported { get; init; }

    /// <summary>
    /// Gets or initializes the accepted numeric defense-profile facts version, or zero to retain native profiles.
    /// </summary>
    public int DefenseDamageProfileVersion { get; init; }

    /// <summary>
    /// Gets or initializes the exact catalogue fingerprint echoed when stateless display completion is negotiated.
    /// </summary>
    /// <remarks>
    /// The connection advertises no fingerprint when stateless display support is disabled.
    /// </remarks>
    public string? SimpleMovePowerCatalogueFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the installation echoed from the game handshake.
    /// </summary>
    public string? GameRoot { get; init; }

    /// <summary>
    /// Gets the tracker application version.
    /// </summary>
    public string TrackerVersion { get; }

    /// <summary>
    /// Gets whether the tracker was launched in debug mode.
    /// </summary>
    public bool DebugRequested { get; }

    /// <summary>
    /// Gets whether starter selection is controlled automatically.
    /// </summary>
    public bool AutoSelectStarter { get; }

    /// <summary>
    /// Gets the inclusive generated-BST ceiling for automatic starter selection.
    /// </summary>
    public int? MaximumStarterBaseStatTotal { get; }

    /// <summary>
    /// Gets the stable normal-species identifiers covered by the Favorite Clause.
    /// </summary>
    public IReadOnlyList<string> FavoriteSpeciesIds { get; }

    /// <summary>
    /// Gets the effective named capabilities granted for this game connection.
    /// </summary>
    public IReadOnlyList<string> DiagnosticCapabilities { get; }

    /// <summary>
    /// Validates and copies one bounded capability list.
    /// </summary>
    /// <param name="capabilities">The optional granted identifiers.</param>
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
