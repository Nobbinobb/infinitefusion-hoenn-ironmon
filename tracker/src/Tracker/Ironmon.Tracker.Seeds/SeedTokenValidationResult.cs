namespace Ironmon.Tracker.Seeds;

/// <summary>
/// Reports one seeded-run token validation outcome without retaining the raw token.
/// </summary>
public sealed class SeedTokenValidationResult
{
    /// <summary>
    /// Initializes a seeded-run token validation result.
    /// </summary>
    /// <param name="status">The validation outcome.</param>
    /// <param name="message">The bounded user-facing result message.</param>
    /// <param name="data">The normalized signed data when validation succeeds.</param>
    private SeedTokenValidationResult(SeedTokenValidationStatus status, string message, SeedTokenData? data)
    {
        Status = status;
        Message = message;
        Data = data;
    }

    /// <summary>
    /// Gets the validation outcome.
    /// </summary>
    public SeedTokenValidationStatus Status { get; }

    /// <summary>
    /// Gets the bounded user-facing result message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the normalized signed data when validation succeeds.
    /// </summary>
    public SeedTokenData? Data { get; }

    /// <summary>
    /// Gets whether validation succeeded.
    /// </summary>
    public bool IsValid => Status == SeedTokenValidationStatus.Valid;

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    /// <param name="data">The normalized signed token data.</param>
    /// <returns>The successful result.</returns>
    public static SeedTokenValidationResult Success(SeedTokenData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new(SeedTokenValidationStatus.Valid, "Seeded-run token is valid.", data);
    }

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    /// <param name="status">The non-valid outcome.</param>
    /// <param name="message">The bounded user-facing result message.</param>
    /// <returns>The failed result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when status is Valid.</exception>
    public static SeedTokenValidationResult Failure(SeedTokenValidationStatus status, string message)
    {
        if (status == SeedTokenValidationStatus.Valid)
            throw new ArgumentOutOfRangeException(nameof(status), status, "A failure cannot use the valid status.");

        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new(status, message, null);
    }
}
