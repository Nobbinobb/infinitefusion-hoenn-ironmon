namespace Ironmon.Tracker.AccessGenerator;

/// <summary>
/// Describes one reviewed diagnostic-access token generation request.
/// </summary>
/// <param name="tokenId">The unique JWT ID.</param>
/// <param name="issuedAt">The UTC issue instant.</param>
/// <param name="expiresAt">The optional UTC expiration instant.</param>
/// <param name="note">The optional support note.</param>
/// <param name="capabilities">The direct capabilities to sign.</param>
/// <exception cref="ArgumentException">Thrown when tokenId is empty.</exception>
/// <exception cref="ArgumentNullException">Thrown when capabilities is null.</exception>
/// <remarks>Initializes an immutable token generation request.</remarks>
public sealed class DiagnosticAccessTokenGenerationRequest(string tokenId, DateTimeOffset issuedAt, DateTimeOffset? expiresAt, string? note, IReadOnlyList<string> capabilities)
{
    /// <summary>
    /// Gets the unique JWT ID.
    /// </summary>
    public string TokenId { get; } = string.IsNullOrWhiteSpace(tokenId) ? throw new ArgumentException("A token ID is required.", nameof(tokenId)) : tokenId;

    /// <summary>
    /// Gets the UTC issue instant.
    /// </summary>
    public DateTimeOffset IssuedAt { get; } = issuedAt.ToUniversalTime();

    /// <summary>
    /// Gets the optional UTC expiration instant.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; } = expiresAt?.ToUniversalTime();

    /// <summary>
    /// Gets the optional support note.
    /// </summary>
    public string? Note { get; } = note;

    /// <summary>
    /// Gets the direct capabilities to sign.
    /// </summary>
    public IReadOnlyList<string> Capabilities { get; } = [.. capabilities ?? throw new ArgumentNullException(nameof(capabilities))];
}
