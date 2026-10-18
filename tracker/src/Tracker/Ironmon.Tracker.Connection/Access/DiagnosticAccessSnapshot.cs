namespace Ironmon.Tracker.Connection.Access;

/// <summary>
/// Presents the current effective diagnostic access without retaining a raw token.
/// </summary>
public sealed class DiagnosticAccessSnapshot
{
    /// <summary>
    /// Initializes an immutable diagnostic-access snapshot.
    /// </summary>
    /// <param name="state">The lifecycle state.</param>
    /// <param name="grant">The validated signed grant when available.</param>
    /// <param name="unknownCapabilities">Signed but unsupported capability identifiers.</param>
    /// <param name="validationStatus">The token validation outcome when applicable.</param>
    /// <param name="message">The bounded state detail when applicable.</param>
    private DiagnosticAccessSnapshot(DiagnosticAccessState state, DiagnosticAccessGrant? grant, IReadOnlyList<string> unknownCapabilities, DiagnosticAccessValidationStatus? validationStatus, string? message)
    {
        State = state;
        Grant = grant;
        UnknownCapabilities = [.. unknownCapabilities];
        ValidationStatus = validationStatus;
        Message = message;
    }

    /// <summary>
    /// Gets the lifecycle state.
    /// </summary>
    public DiagnosticAccessState State { get; }

    /// <summary>
    /// Gets the validated signed grant when available.
    /// </summary>
    public DiagnosticAccessGrant? Grant { get; }

    /// <summary>
    /// Gets signed but unsupported capability identifiers.
    /// </summary>
    public IReadOnlyList<string> UnknownCapabilities { get; }

    /// <summary>
    /// Gets the persisted token validation outcome when applicable.
    /// </summary>
    public DiagnosticAccessValidationStatus? ValidationStatus { get; }

    /// <summary>
    /// Gets the bounded state detail when applicable.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// Gets whether supported diagnostic access is currently active.
    /// </summary>
    public bool IsActive => State is DiagnosticAccessState.Active or DiagnosticAccessState.DeveloperOverride;

    /// <summary>
    /// Gets whether the active signed grant never expires.
    /// </summary>
    public bool IsLifetime => State == DiagnosticAccessState.Active && Grant?.ExpiresAt is null;

    /// <summary>
    /// Gets the effective supported capability identifiers.
    /// </summary>
    public IReadOnlyList<string> EffectiveCapabilities => State switch
    {
        DiagnosticAccessState.Active => Grant?.EffectiveCapabilities ?? [],
        DiagnosticAccessState.DeveloperOverride => [.. DiagnosticCapabilityCatalog.KnownIds],
        _ => []
    };

    /// <summary>
    /// Determines whether one supported capability is currently granted.
    /// </summary>
    /// <param name="capability">The stable capability identifier.</param>
    /// <returns>Whether the capability is effective.</returns>
    public bool HasCapability(string capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        return EffectiveCapabilities.Contains(capability, StringComparer.Ordinal);
    }

    /// <summary>
    /// Creates the no-access state.
    /// </summary>
    /// <returns>The empty snapshot.</returns>
    public static DiagnosticAccessSnapshot None()
        => new(DiagnosticAccessState.None, null, [], null, null);

    /// <summary>
    /// Creates the unrestricted local developer state.
    /// </summary>
    /// <returns>The developer-override snapshot.</returns>
    public static DiagnosticAccessSnapshot DeveloperOverride()
        => new(DiagnosticAccessState.DeveloperOverride, null, [], null, null);

    /// <summary>
    /// Creates an active signed-grant state.
    /// </summary>
    /// <param name="grant">The validated grant.</param>
    /// <param name="unknownCapabilities">Signed but unsupported capability identifiers.</param>
    /// <returns>The active snapshot.</returns>
    public static DiagnosticAccessSnapshot Active(DiagnosticAccessGrant grant, IReadOnlyList<string> unknownCapabilities)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(unknownCapabilities);
        return new(DiagnosticAccessState.Active, grant, unknownCapabilities, DiagnosticAccessValidationStatus.Valid, null);
    }

    /// <summary>
    /// Creates an expired state while retaining already validated display metadata when available.
    /// </summary>
    /// <param name="grant">The previously validated grant, or null after a cold start.</param>
    /// <param name="unknownCapabilities">Signed but unsupported capability identifiers.</param>
    /// <param name="message">The bounded expiration detail.</param>
    /// <returns>The expired snapshot.</returns>
    public static DiagnosticAccessSnapshot Expired(DiagnosticAccessGrant? grant, IReadOnlyList<string> unknownCapabilities, string message)
    {
        ArgumentNullException.ThrowIfNull(unknownCapabilities);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new(DiagnosticAccessState.Expired, grant, unknownCapabilities, DiagnosticAccessValidationStatus.Expired, message);
    }

    /// <summary>
    /// Creates an invalid persisted-token state.
    /// </summary>
    /// <param name="status">The failed validation outcome.</param>
    /// <param name="message">The bounded validation or persistence detail.</param>
    /// <returns>The invalid snapshot.</returns>
    public static DiagnosticAccessSnapshot Invalid(DiagnosticAccessValidationStatus? status, string message)
    {
        if (status == DiagnosticAccessValidationStatus.Valid)
            throw new ArgumentOutOfRangeException(nameof(status), status, "An invalid snapshot cannot use the valid status.");

        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new(DiagnosticAccessState.Invalid, null, [], status, message);
    }
}
