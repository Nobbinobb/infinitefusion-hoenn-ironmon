using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ironmon.Tracker.AccessGenerator;

/// <summary>
/// Creates signed Ironmon diagnostic-access JWTs from reviewed generator input.
/// </summary>
public sealed class DiagnosticAccessTokenGenerator
{
    private readonly JsonWebTokenHandler _tokenHandler = new() { SetDefaultTimesOnTokenCreation = false };

    /// <summary>
    /// Initializes the diagnostic-access token generator.
    /// </summary>
    public DiagnosticAccessTokenGenerator()
    {
    }

    /// <summary>
    /// Creates one ES256-signed diagnostic-access JWT.
    /// </summary>
    /// <param name="key">The externally loaded private signing key.</param>
    /// <param name="request">The reviewed generation request.</param>
    /// <returns>The compact token and its trusted generation summary.</returns>
    /// <exception cref="ArgumentNullException">Thrown when key or request is null.</exception>
    /// <exception cref="ArgumentException">Thrown when a claim or capability selection violates the generator contract.</exception>
    public DiagnosticAccessTokenGenerationResult Generate(DiagnosticAccessSigningKey key, DiagnosticAccessTokenGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        string[] direct = [.. request.Capabilities.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        Dictionary<string, object> claims = new(StringComparer.Ordinal)
        {
            [DiagnosticAccessTokenConstants.VersionClaim] = DiagnosticAccessTokenConstants.ContractVersion,
            [JwtRegisteredClaimNames.Jti] = request.TokenId,
            [DiagnosticAccessTokenConstants.CapabilitiesClaim] = direct
        };

        if (!string.IsNullOrWhiteSpace(request.Note))
            claims[DiagnosticAccessTokenConstants.NoteClaim] = request.Note;

        SecurityTokenDescriptor descriptor = new()
        {
            Audience = DiagnosticAccessTokenConstants.Audience,
            Claims = claims,
            Expires = request.ExpiresAt?.UtcDateTime,
            IssuedAt = request.IssuedAt.UtcDateTime,
            Issuer = DiagnosticAccessTokenConstants.Issuer,
            SigningCredentials = key.CreateSigningCredentials(),
            TokenType = DiagnosticAccessTokenConstants.TokenType
        };
        string token = _tokenHandler.CreateToken(descriptor);
        return new(token, key.KeyId, request.TokenId, request.IssuedAt, request.ExpiresAt, request.Note, direct, DiagnosticCapabilityCatalog.Expand(direct));
    }

    /// <summary>
    /// Validates bounded token claims and generator dependency rules.
    /// </summary>
    /// <param name="request">The requested signed claims.</param>
    /// <exception cref="ArgumentException">Thrown when a claim or selection is invalid.</exception>
    private static void Validate(DiagnosticAccessTokenGenerationRequest request)
    {
        if (request.TokenId.Length > DiagnosticAccessTokenConstants.MaximumTokenIdLength)
            throw new ArgumentException("The token ID is too long.", nameof(request));

        if (request.Note?.Length > DiagnosticAccessTokenConstants.MaximumNoteLength)
            throw new ArgumentException("The support note is too long.", nameof(request));

        if (request.ExpiresAt is not null && request.ExpiresAt <= request.IssuedAt)
            throw new ArgumentException("The expiration must be later than the issue time.", nameof(request));

        if (request.Capabilities.Count > DiagnosticAccessTokenConstants.MaximumCapabilityCount)
            throw new ArgumentException("The capability selection is too large.", nameof(request));

        string? unsupported = request.Capabilities.FirstOrDefault(capability => !DiagnosticCapabilityCatalog.IsKnown(capability));
        if (unsupported is not null)
            throw new ArgumentException($"Unsupported diagnostic capability '{unsupported}'.", nameof(request));

        IReadOnlyList<string> selectionErrors = DiagnosticAccessSelectionValidator.GetErrors(request.Capabilities);
        if (selectionErrors.Count > 0)
            throw new ArgumentException(selectionErrors[0], nameof(request));
    }
}
