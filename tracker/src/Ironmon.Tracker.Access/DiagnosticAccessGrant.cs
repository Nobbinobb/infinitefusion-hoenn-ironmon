namespace Ironmon.Tracker.Access;

/// <summary>
/// Describes the trusted claims and effective rights from a valid diagnostic-access token.
/// </summary>
/// <param name="keyId">The trusted signing-key ID.</param>
/// <param name="tokenId">The unique JWT ID.</param>
/// <param name="issuedAt">The UTC issue instant.</param>
/// <param name="expiresAt">The optional UTC expiration instant.</param>
/// <param name="note">The optional support note.</param>
/// <param name="directCapabilities">The supported capabilities explicitly granted by the token.</param>
/// <param name="effectiveCapabilities">The supported capabilities after implication expansion.</param>
/// <remarks>Initializes an immutable validated diagnostic-access grant.</remarks>
public sealed class DiagnosticAccessGrant(string keyId, string tokenId, DateTimeOffset issuedAt, DateTimeOffset? expiresAt, string? note, IReadOnlyList<string> directCapabilities, IReadOnlyList<string> effectiveCapabilities)
{
    /// <summary>
    /// Gets the trusted signing-key ID.
    /// </summary>
    public string KeyId { get; } = keyId;

    /// <summary>
    /// Gets the unique JWT ID.
    /// </summary>
    public string TokenId { get; } = tokenId;

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
    /// Gets supported capabilities explicitly granted by the token.
    /// </summary>
    public IReadOnlyList<string> DirectCapabilities { get; } = [.. directCapabilities];

    /// <summary>
    /// Gets supported capabilities after implication expansion.
    /// </summary>
    public IReadOnlyList<string> EffectiveCapabilities { get; } = [.. effectiveCapabilities];

    /// <summary>
    /// Determines whether the grant includes one effective capability.
    /// </summary>
    /// <param name="capability">The stable capability identifier.</param>
    /// <returns>Whether the capability is effective.</returns>
    public bool HasCapability(string capability) => EffectiveCapabilities.Contains(capability, StringComparer.Ordinal);
}
