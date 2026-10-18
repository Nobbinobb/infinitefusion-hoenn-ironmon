namespace Ironmon.Tracker.Access;

/// <summary>
/// Reports one diagnostic-access token validation outcome without retaining the raw token.
/// </summary>
public sealed class DiagnosticAccessValidationResult
{
    /// <summary>
    /// Initializes a diagnostic-access validation result.
    /// </summary>
    /// <param name="status">The validation outcome.</param>
    /// <param name="message">The bounded user-facing result message.</param>
    /// <param name="grant">The trusted grant when validation succeeded.</param>
    /// <param name="unknownCapabilities">Signed but unsupported capability IDs.</param>
    private DiagnosticAccessValidationResult(DiagnosticAccessValidationStatus status, string message, DiagnosticAccessGrant? grant, IReadOnlyList<string>? unknownCapabilities)
    {
        Status = status;
        Message = message;
        Grant = grant;
        UnknownCapabilities = unknownCapabilities is null ? [] : [.. unknownCapabilities];
    }

    /// <summary>
    /// Gets the validation outcome.
    /// </summary>
    public DiagnosticAccessValidationStatus Status { get; }

    /// <summary>
    /// Gets the bounded user-facing result message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the trusted grant when validation succeeded.
    /// </summary>
    public DiagnosticAccessGrant? Grant { get; }

    /// <summary>
    /// Gets signed but unsupported capability IDs.
    /// </summary>
    public IReadOnlyList<string> UnknownCapabilities { get; }

    /// <summary>
    /// Gets whether validation succeeded.
    /// </summary>
    public bool IsValid => Status == DiagnosticAccessValidationStatus.Valid;

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    /// <param name="grant">The trusted grant.</param>
    /// <param name="unknownCapabilities">Signed but unsupported capability IDs.</param>
    /// <returns>The successful result.</returns>
    public static DiagnosticAccessValidationResult Success(DiagnosticAccessGrant grant, IReadOnlyList<string> unknownCapabilities)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(unknownCapabilities);
        return new(DiagnosticAccessValidationStatus.Valid, "Diagnostic access token is valid.", grant, unknownCapabilities);
    }

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    /// <param name="status">The non-valid outcome.</param>
    /// <param name="message">The bounded user-facing message.</param>
    /// <returns>The failed result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when status is Valid.</exception>
    public static DiagnosticAccessValidationResult Failure(DiagnosticAccessValidationStatus status, string message)
    {
        if (status == DiagnosticAccessValidationStatus.Valid)
            throw new ArgumentOutOfRangeException(nameof(status), status, "A failure cannot use the valid status.");

        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new(status, message, null, null);
    }
}
