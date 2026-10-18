using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json;

namespace Ironmon.Tracker.Access;

/// <summary>
/// Validates signed Ironmon diagnostic-access JWTs against a fixed application contract.
/// </summary>
public sealed class DiagnosticAccessTokenValidator
{
    private readonly DiagnosticAccessKeyring _keyring;
    private readonly JsonWebTokenHandler _tokenHandler = new() { MaximumTokenSizeInBytes = DiagnosticAccessTokenConstants.MaximumTokenLength };
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a diagnostic-access token validator.
    /// </summary>
    /// <param name="keyring">The trusted public verification keys.</param>
    /// <param name="timeProvider">The UTC time source, or the system source when omitted.</param>
    /// <exception cref="ArgumentNullException">Thrown when keyring is null.</exception>
    public DiagnosticAccessTokenValidator(DiagnosticAccessKeyring keyring, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(keyring);
        _keyring = keyring;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Validates one compact signed diagnostic-access token.
    /// </summary>
    /// <param name="token">The JWS Compact Serialization input.</param>
    /// <returns>The trusted grant or a bounded failure result.</returns>
    public async Task<DiagnosticAccessValidationResult> ValidateAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > DiagnosticAccessTokenConstants.MaximumTokenLength || !_tokenHandler.CanReadToken(token))
            return DiagnosticAccessValidationResult.Failure(DiagnosticAccessValidationStatus.Malformed, "Diagnostic access token is malformed.");

        JsonWebToken unvalidated;
        try
        {
            unvalidated = _tokenHandler.ReadJsonWebToken(token);
        }
        catch (Exception exception) when (exception is ArgumentException or SecurityTokenException)
        {
            return DiagnosticAccessValidationResult.Failure(DiagnosticAccessValidationStatus.Malformed, "Diagnostic access token is malformed.");
        }

        if (!string.Equals(unvalidated.Alg, SecurityAlgorithms.EcdsaSha256, StringComparison.Ordinal))
            return DiagnosticAccessValidationResult.Failure(DiagnosticAccessValidationStatus.UnsupportedAlgorithm, "Diagnostic access token uses an unsupported signing algorithm.");

        if (!string.Equals(unvalidated.Typ, DiagnosticAccessTokenConstants.TokenType, StringComparison.Ordinal))
            return DiagnosticAccessValidationResult.Failure(DiagnosticAccessValidationStatus.UnsupportedType, "Diagnostic access token has an unsupported type.");

        if (string.IsNullOrWhiteSpace(unvalidated.Kid) || unvalidated.Kid.Length > DiagnosticAccessTokenConstants.MaximumKeyIdLength || !_keyring.TryGet(unvalidated.Kid, out ECDsaSecurityKey? key))
            return DiagnosticAccessValidationResult.Failure(DiagnosticAccessValidationStatus.UnknownKey, "Diagnostic access token was signed by an unknown key.");

        TokenValidationParameters parameters = new()
        {
            IssuerSigningKey = key,
            RequireSignedTokens = true,
            TryAllIssuerSigningKeys = false,
            ValidateAudience = false,
            ValidateIssuer = false,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = false,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            ValidTypes = [DiagnosticAccessTokenConstants.TokenType]
        };

        TokenValidationResult signature = await _tokenHandler.ValidateTokenAsync(token, parameters);
        if (!signature.IsValid)
            return DiagnosticAccessValidationResult.Failure(DiagnosticAccessValidationStatus.InvalidSignature, "Diagnostic access token signature is invalid.");

        return ValidatePayload(unvalidated);
    }

    /// <summary>
    /// Validates the signed Ironmon claims from a signature-verified JWT.
    /// </summary>
    /// <param name="token">The signature-verified JWT.</param>
    /// <returns>The trusted grant or claims failure.</returns>
    private DiagnosticAccessValidationResult ValidatePayload(JsonWebToken token)
    {
        JsonElement payload;
        try
        {
            using JsonDocument document = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(token.EncodedPayload));
            payload = document.RootElement.Clone();
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return InvalidClaims();
        }

        if (payload.ValueKind != JsonValueKind.Object || HasDuplicateProperties(payload))
            return InvalidClaims();

        if (!TryGetExactString(payload, JwtRegisteredClaimNames.Iss, DiagnosticAccessTokenConstants.Issuer, out _)
            || !TryGetExactString(payload, JwtRegisteredClaimNames.Aud, DiagnosticAccessTokenConstants.Audience, out _)
            || !TryGetBoundedString(payload, JwtRegisteredClaimNames.Jti, DiagnosticAccessTokenConstants.MaximumTokenIdLength, out string? tokenId)
            || !TryGetInt64(payload, JwtRegisteredClaimNames.Iat, out long issuedAtSeconds)
            || !TryGetInt32(payload, DiagnosticAccessTokenConstants.VersionClaim, out int version)
            || version != DiagnosticAccessTokenConstants.ContractVersion)
        {
            return InvalidClaims();
        }

        DateTimeOffset issuedAt;
        try
        {
            issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAtSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return InvalidClaims();
        }

        DiagnosticAccessValidationResult? expirationResult = ReadExpiration(payload, issuedAt, out DateTimeOffset? expiresAt);
        if (expirationResult is not null)
            return expirationResult;

        if (!TryGetOptionalNote(payload, out string? note) || !TryGetCapabilities(payload, out IReadOnlyList<string>? direct, out IReadOnlyList<string>? unknown))
            return InvalidClaims();

        IReadOnlyList<string> effective = DiagnosticCapabilityCatalog.Expand(direct);
        DiagnosticAccessGrant grant = new(token.Kid, tokenId!, issuedAt, expiresAt, note, direct, effective);
        return DiagnosticAccessValidationResult.Success(grant, unknown);
    }

    /// <summary>
    /// Reads and validates the optional expiration claim.
    /// </summary>
    /// <param name="payload">The signed JWT payload.</param>
    /// <param name="issuedAt">The validated issue instant.</param>
    /// <param name="expiresAt">The validated optional expiration instant.</param>
    /// <returns>A failure result, or null when expiration is valid.</returns>
    private DiagnosticAccessValidationResult? ReadExpiration(JsonElement payload, DateTimeOffset issuedAt, out DateTimeOffset? expiresAt)
    {
        expiresAt = null;
        if (!payload.TryGetProperty(JwtRegisteredClaimNames.Exp, out JsonElement element))
            return null;

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out long seconds))
            return InvalidClaims();

        try
        {
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return InvalidClaims();
        }

        if (expiresAt <= issuedAt)
            return InvalidClaims();

        return _timeProvider.GetUtcNow() >= expiresAt
            ? DiagnosticAccessValidationResult.Failure(DiagnosticAccessValidationStatus.Expired, "Diagnostic access token has expired.")
            : null;
    }

    /// <summary>
    /// Reads the signed capability list and separates supported and unknown IDs.
    /// </summary>
    /// <param name="payload">The signed JWT payload.</param>
    /// <param name="direct">The supported direct grants.</param>
    /// <param name="unknown">The signed but unsupported IDs.</param>
    /// <returns>Whether the capability claim follows the contract.</returns>
    private static bool TryGetCapabilities(JsonElement payload, out IReadOnlyList<string> direct, out IReadOnlyList<string> unknown)
    {
        direct = [];
        unknown = [];
        if (!payload.TryGetProperty(DiagnosticAccessTokenConstants.CapabilitiesClaim, out JsonElement element)
            || element.ValueKind != JsonValueKind.Array
            || element.GetArrayLength() > DiagnosticAccessTokenConstants.MaximumCapabilityCount)
        {
            return false;
        }

        List<string> supported = [];
        List<string> unsupported = [];
        string? previous = null;
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                return false;

            string capability = item.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(capability) || capability.Length > DiagnosticAccessTokenConstants.MaximumCapabilityIdLength)
                return false;

            if (previous is not null && string.CompareOrdinal(previous, capability) >= 0)
                return false;

            previous = capability;
            (DiagnosticCapabilityCatalog.IsKnown(capability) ? supported : unsupported).Add(capability);
        }

        direct = supported;
        unknown = unsupported;
        return true;
    }

    /// <summary>
    /// Reads the optional bounded support note.
    /// </summary>
    /// <param name="payload">The signed JWT payload.</param>
    /// <param name="note">The optional support note.</param>
    /// <returns>Whether the note follows the contract.</returns>
    private static bool TryGetOptionalNote(JsonElement payload, out string? note)
    {
        note = null;
        if (!payload.TryGetProperty(DiagnosticAccessTokenConstants.NoteClaim, out JsonElement element) || element.ValueKind == JsonValueKind.Null)
            return true;

        if (element.ValueKind != JsonValueKind.String)
            return false;

        note = element.GetString();
        return note is not null && note.Length <= DiagnosticAccessTokenConstants.MaximumNoteLength;
    }

    /// <summary>
    /// Reads one required string and compares it exactly.
    /// </summary>
    /// <param name="payload">The signed JWT payload.</param>
    /// <param name="name">The claim name.</param>
    /// <param name="expected">The required claim value.</param>
    /// <param name="value">The read value.</param>
    /// <returns>Whether the exact string is present.</returns>
    private static bool TryGetExactString(JsonElement payload, string name, string expected, out string? value)
    {
        value = null;
        return payload.TryGetProperty(name, out JsonElement element)
            && element.ValueKind == JsonValueKind.String
            && (value = element.GetString()) is not null
            && string.Equals(value, expected, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads one required, non-empty bounded string claim.
    /// </summary>
    /// <param name="payload">The signed JWT payload.</param>
    /// <param name="name">The claim name.</param>
    /// <param name="maximumLength">The maximum accepted length.</param>
    /// <param name="value">The read value.</param>
    /// <returns>Whether the bounded string is present.</returns>
    private static bool TryGetBoundedString(JsonElement payload, string name, int maximumLength, out string? value)
    {
        value = null;
        return payload.TryGetProperty(name, out JsonElement element)
            && element.ValueKind == JsonValueKind.String
            && (value = element.GetString()) is not null
            && !string.IsNullOrWhiteSpace(value)
            && value.Length <= maximumLength;
    }

    /// <summary>
    /// Reads one required 64-bit integer claim.
    /// </summary>
    /// <param name="payload">The signed JWT payload.</param>
    /// <param name="name">The claim name.</param>
    /// <param name="value">The read value.</param>
    /// <returns>Whether the integer is present.</returns>
    private static bool TryGetInt64(JsonElement payload, string name, out long value)
    {
        value = 0;
        return payload.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value);
    }

    /// <summary>
    /// Reads one required 32-bit integer claim.
    /// </summary>
    /// <param name="payload">The signed JWT payload.</param>
    /// <param name="name">The claim name.</param>
    /// <param name="value">The read value.</param>
    /// <returns>Whether the integer is present.</returns>
    private static bool TryGetInt32(JsonElement payload, string name, out int value)
    {
        value = 0;
        return payload.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
    }

    /// <summary>
    /// Determines whether a JSON object repeats a property name.
    /// </summary>
    /// <param name="payload">The signed JWT payload.</param>
    /// <returns>Whether any property name occurs more than once.</returns>
    private static bool HasDuplicateProperties(JsonElement payload)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        return payload.EnumerateObject().Any(property => !names.Add(property.Name));
    }

    /// <summary>
    /// Creates the standard signed-claims failure result.
    /// </summary>
    /// <returns>The invalid-claims result.</returns>
    private static DiagnosticAccessValidationResult InvalidClaims()
        => DiagnosticAccessValidationResult.Failure(DiagnosticAccessValidationStatus.InvalidClaims, "Diagnostic access token claims are invalid.");
}
