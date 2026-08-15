namespace Ironmon.Tracker.AccessGenerator;

/// <summary>
/// Describes one generated diagnostic-access JWT and its reviewed claims.
/// </summary>
/// <param name="token">The JWS Compact Serialization token.</param>
/// <param name="keyId">The derived signing-key ID.</param>
/// <param name="tokenId">The unique JWT ID.</param>
/// <param name="issuedAt">The UTC issue instant.</param>
/// <param name="expiresAt">The optional UTC expiration instant.</param>
/// <param name="note">The optional support note.</param>
/// <param name="directCapabilities">The signed direct capabilities.</param>
/// <param name="effectiveCapabilities">The effective capabilities after implication expansion.</param>
/// <exception cref="ArgumentException">Thrown when token, keyId, or tokenId is empty.</exception>
/// <exception cref="ArgumentNullException">Thrown when directCapabilities or effectiveCapabilities is null.</exception>
/// <remarks>Initializes an immutable token generation result.</remarks>
public sealed class DiagnosticAccessTokenGenerationResult(string token, string keyId, string tokenId, DateTimeOffset issuedAt, DateTimeOffset? expiresAt, string? note, IReadOnlyList<string> directCapabilities, IReadOnlyList<string> effectiveCapabilities)
{
    /// <summary>
    /// Gets the JWS Compact Serialization token.
    /// </summary>
    public string Token { get; } = string.IsNullOrWhiteSpace(token) ? throw new ArgumentException("A token is required.", nameof(token)) : token;

    /// <summary>
    /// Gets the derived signing-key ID.
    /// </summary>
    public string KeyId { get; } = string.IsNullOrWhiteSpace(keyId) ? throw new ArgumentException("A key ID is required.", nameof(keyId)) : keyId;

    /// <summary>
    /// Gets the unique JWT ID.
    /// </summary>
    public string TokenId { get; } = string.IsNullOrWhiteSpace(tokenId) ? throw new ArgumentException("A token ID is required.", nameof(tokenId)) : tokenId;

    /// <summary>
    /// Gets the UTC issue instant.
    /// </summary>
    public DateTimeOffset IssuedAt { get; } = issuedAt;

    /// <summary>
    /// Gets the optional UTC expiration instant.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; } = expiresAt;

    /// <summary>
    /// Gets the optional support note.
    /// </summary>
    public string? Note { get; } = note;

    /// <summary>
    /// Gets the signed direct capabilities.
    /// </summary>
    public IReadOnlyList<string> DirectCapabilities { get; } = [.. directCapabilities ?? throw new ArgumentNullException(nameof(directCapabilities))];

    /// <summary>
    /// Gets effective capabilities after implication expansion.
    /// </summary>
    public IReadOnlyList<string> EffectiveCapabilities { get; } = [.. effectiveCapabilities ?? throw new ArgumentNullException(nameof(effectiveCapabilities))];
}
