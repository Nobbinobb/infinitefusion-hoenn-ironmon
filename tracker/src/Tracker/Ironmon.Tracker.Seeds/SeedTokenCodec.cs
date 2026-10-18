using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json;

namespace Ironmon.Tracker.Seeds;

/// <summary>
/// Creates and strictly validates locally shareable seeded-run JWS tokens.
/// </summary>
/// <remarks>
/// The shared HMAC key provides token-family integrity, not maintainer authority. Every ordinary tracker must be able to create these tokens.
/// </remarks>
public sealed class SeedTokenCodec
{
    private readonly SymmetricSecurityKey _key;
    private readonly JsonWebTokenHandler _tokenHandler = new()
    {
        MaximumTokenSizeInBytes = SeedTokenConstants.MaximumTokenLength,
        SetDefaultTimesOnTokenCreation = false
    };

    /// <summary>
    /// Initializes a seeded-run token codec with the locally shared signing material.
    /// </summary>
    /// <param name="keyMaterial">At least 256 bits of application key material.</param>
    /// <exception cref="ArgumentException">Thrown when the key is too short.</exception>
    public SeedTokenCodec(ReadOnlySpan<byte> keyMaterial)
    {
        if (keyMaterial.Length < SeedTokenConstants.MinimumKeySize)
            throw new ArgumentException("Seed-token key material must contain at least 32 bytes.", nameof(keyMaterial));

        _key = new SymmetricSecurityKey(keyMaterial.ToArray()) { KeyId = SeedTokenConstants.KeyId };
    }

    /// <summary>
    /// Creates one HS256-signed seeded-run token from the shared reproduction contract.
    /// </summary>
    /// <param name="recipe">The run seed, configuration, and compatibility metadata.</param>
    /// <param name="tokenId">The new token identity.</param>
    /// <param name="issuedAt">The token issue instant.</param>
    /// <returns>The JWS Compact Serialization token.</returns>
    /// <exception cref="ArgumentNullException">Thrown when recipe is null.</exception>
    /// <exception cref="ArgumentException">Thrown when an exported claim violates the token contract.</exception>
    public string Create(RunReproductionRecipePayload recipe, string tokenId, DateTimeOffset issuedAt)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        SeedTokenData data = new()
        {
            TokenId = tokenId,
            IssuedAt = issuedAt,
            Seed = recipe.Seed,
            GameVersion = recipe.GameVersion,
            IronmonVersion = recipe.IronmonVersion,
            DataMode = recipe.DataMode,
            Configuration = recipe.Configuration,
            CompatibilityFingerprint = RunCompatibilityFingerprint.Create(recipe)
        };

        if (!TryValidateData(data, out string? error))
            throw new ArgumentException(error, nameof(recipe));

        Dictionary<string, object> claims = new(StringComparer.Ordinal)
        {
            [SeedTokenConstants.VersionClaim] = SeedTokenConstants.ContractVersion,
            [JwtRegisteredClaimNames.Jti] = data.TokenId,
            [SeedTokenConstants.SeedClaim] = data.Seed,
            [SeedTokenConstants.GameVersionClaim] = data.GameVersion,
            [SeedTokenConstants.IronmonVersionClaim] = data.IronmonVersion,
            [SeedTokenConstants.DataModeClaim] = data.DataMode,
            [SeedTokenConstants.ConfigurationClaim] = TrackerJson.SerializePayload(data.Configuration),
            [SeedTokenConstants.CompatibilityFingerprintClaim] = data.CompatibilityFingerprint
        };

        SecurityTokenDescriptor descriptor = new()
        {
            Audience = SeedTokenConstants.Audience,
            Claims = claims,
            IssuedAt = data.IssuedAt.UtcDateTime,
            Issuer = SeedTokenConstants.Issuer,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256),
            TokenType = SeedTokenConstants.TokenType
        };

        return _tokenHandler.CreateToken(descriptor);
    }

    /// <summary>
    /// Validates one seeded-run token and returns only normalized signed inputs.
    /// </summary>
    /// <param name="token">The JWS Compact Serialization token.</param>
    /// <returns>The validation outcome.</returns>
    public async Task<SeedTokenValidationResult> ValidateAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > SeedTokenConstants.MaximumTokenLength || !_tokenHandler.CanReadToken(token))
            return Failure(SeedTokenValidationStatus.Malformed, "Seeded-run token is malformed.");

        JsonWebToken unvalidated;
        try
        {
            unvalidated = _tokenHandler.ReadJsonWebToken(token);
        }
        catch (Exception exception) when (exception is ArgumentException or SecurityTokenException)
        {
            return Failure(SeedTokenValidationStatus.Malformed, "Seeded-run token is malformed.");
        }

        if (!string.Equals(unvalidated.Alg, SecurityAlgorithms.HmacSha256, StringComparison.Ordinal))
            return Failure(SeedTokenValidationStatus.UnsupportedAlgorithm, "Seeded-run token uses an unsupported signing algorithm.");

        if (!string.Equals(unvalidated.Typ, SeedTokenConstants.TokenType, StringComparison.Ordinal))
            return Failure(SeedTokenValidationStatus.UnsupportedType, "Token is not an Ironmon seeded-run token.");

        if (!string.Equals(unvalidated.Kid, SeedTokenConstants.KeyId, StringComparison.Ordinal))
            return Failure(SeedTokenValidationStatus.UnknownKey, "Seeded-run token uses an unsupported key identity.");

        TokenValidationParameters parameters = new()
        {
            IssuerSigningKey = _key,
            RequireSignedTokens = true,
            TryAllIssuerSigningKeys = false,
            ValidateAudience = false,
            ValidateIssuer = false,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = false,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidTypes = [SeedTokenConstants.TokenType]
        };

        TokenValidationResult signature = await _tokenHandler.ValidateTokenAsync(token, parameters);
        if (!signature.IsValid)
            return Failure(SeedTokenValidationStatus.InvalidSignature, "Seeded-run token signature is invalid.");

        return ValidatePayload(unvalidated);
    }

    /// <summary>
    /// Validates signature-verified payload claims.
    /// </summary>
    /// <param name="token">The signature-verified token.</param>
    /// <returns>The normalized data or claims failure.</returns>
    private static SeedTokenValidationResult ValidatePayload(JsonWebToken token)
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

        if (payload.ValueKind != JsonValueKind.Object || HasDuplicateOrUnknownProperties(payload))
            return InvalidClaims();

        if (!TryGetExactString(payload, JwtRegisteredClaimNames.Iss, SeedTokenConstants.Issuer, out _)
            || !TryGetExactString(payload, JwtRegisteredClaimNames.Aud, SeedTokenConstants.Audience, out _)
            || !TryGetBoundedString(payload, JwtRegisteredClaimNames.Jti, SeedTokenConstants.MaximumTokenIdLength, out string? tokenId)
            || !TryGetInt64(payload, JwtRegisteredClaimNames.Iat, out long issuedAtSeconds)
            || !TryGetInt32(payload, SeedTokenConstants.VersionClaim, out int version)
            || version != SeedTokenConstants.ContractVersion
            || !TryGetInt64(payload, SeedTokenConstants.SeedClaim, out long seed)
            || !TryGetBoundedString(payload, SeedTokenConstants.GameVersionClaim, SeedTokenConstants.MaximumIdentifierLength, out string? gameVersion)
            || !TryGetBoundedString(payload, SeedTokenConstants.IronmonVersionClaim, SeedTokenConstants.MaximumIdentifierLength, out string? ironmonVersion)
            || !TryGetBoundedString(payload, SeedTokenConstants.DataModeClaim, SeedTokenConstants.MaximumIdentifierLength, out string? dataMode)
            || !TryGetBoundedString(payload, SeedTokenConstants.CompatibilityFingerprintClaim, 64, out string? fingerprint)
            || !payload.TryGetProperty(SeedTokenConstants.ConfigurationClaim, out JsonElement configurationElement)
            || !HasExactConfigurationProperties(configurationElement))
        {
            return InvalidClaims();
        }

        DateTimeOffset issuedAt;
        RunConfigurationPayload configuration;
        try
        {
            issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAtSeconds);
            configuration = TrackerJson.DeserializePayload<RunConfigurationPayload>(configurationElement);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or TrackerProtocolException)
        {
            return InvalidClaims();
        }

        SeedTokenData data = new()
        {
            TokenId = tokenId!,
            IssuedAt = issuedAt,
            Seed = seed,
            GameVersion = gameVersion!,
            IronmonVersion = ironmonVersion!,
            DataMode = dataMode!,
            Configuration = configuration,
            CompatibilityFingerprint = fingerprint!
        };

        return TryValidateData(data, out _) ? SeedTokenValidationResult.Success(data) : InvalidClaims();
    }

    /// <summary>
    /// Validates normalized token data against bounded game inputs.
    /// </summary>
    /// <param name="data">The normalized token data.</param>
    /// <param name="error">The first validation error.</param>
    /// <returns>Whether every value follows the seeded-run contract.</returns>
    private static bool TryValidateData(SeedTokenData data, out string error)
    {
        error = "Seeded-run token claims are invalid.";
        if (string.IsNullOrWhiteSpace(data.TokenId) || data.TokenId.Length > SeedTokenConstants.MaximumTokenIdLength)
            return false;

        if (data.Seed is < SeedTokenConstants.MinimumSeed or > SeedTokenConstants.MaximumSeed)
            return false;

        if (!IsBoundedIdentifier(data.GameVersion) || !IsBoundedIdentifier(data.IronmonVersion))
            return false;

        if (data.DataMode is not ("classic" or "remix" or "expert"))
            return false;

        if (data.Configuration.SchemaVersion != SeedTokenConstants.ConfigurationSchemaVersion
            || data.Configuration.WildPolicy is not ("mixed" or "custom_fusions_only" or "normal_only")
            || data.Configuration.TrainerPolicy is not ("mixed" or "custom_fusions_only" or "normal_only")
            || data.Configuration.UnfusionSetting is not ("random_component" or "player_choice"))
        {
            return false;
        }

        return data.CompatibilityFingerprint.Length == 64
            && data.CompatibilityFingerprint.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    /// <summary>
    /// Determines whether one version-like identifier is present and bounded.
    /// </summary>
    /// <param name="value">The identifier.</param>
    /// <returns>Whether the identifier is valid.</returns>
    private static bool IsBoundedIdentifier(string value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= SeedTokenConstants.MaximumIdentifierLength;

    /// <summary>
    /// Determines whether a payload repeats a property or includes one outside the fixed claim set.
    /// </summary>
    /// <param name="payload">The signed payload.</param>
    /// <returns>Whether a duplicate or unsupported property exists.</returns>
    private static bool HasDuplicateOrUnknownProperties(JsonElement payload)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        return payload.EnumerateObject().Any(property => !names.Add(property.Name) || !IsKnownClaim(property.Name));
    }

    /// <summary>
    /// Determines whether the nested configuration has exactly the versioned contract properties.
    /// </summary>
    /// <param name="configuration">The signed configuration object.</param>
    /// <returns>Whether the property set is exact and contains no duplicates.</returns>
    private static bool HasExactConfigurationProperties(JsonElement configuration)
    {
        if (configuration.ValueKind != JsonValueKind.Object)
            return false;

        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (JsonProperty property in configuration.EnumerateObject())
        {
            if (!names.Add(property.Name) || !IsKnownConfigurationProperty(property.Name))
                return false;
        }

        return names.Count == 5;
    }

    /// <summary>
    /// Determines whether a property belongs to the exact nested configuration claim set.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <returns>Whether the property is supported.</returns>
    private static bool IsKnownConfigurationProperty(string name)
    {
        return name is SeedTokenConstants.ConfigurationVersionProperty
            or SeedTokenConstants.WildPolicyProperty
            or SeedTokenConstants.TrainerPolicyProperty
            or SeedTokenConstants.UnfusionSettingProperty
            or SeedTokenConstants.AutomaticResetProperty;
    }

    /// <summary>
    /// Determines whether a property belongs to the exact seeded-run claim set.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <returns>Whether the property is supported.</returns>
    private static bool IsKnownClaim(string name)
    {
        return name is JwtRegisteredClaimNames.Iss
            or JwtRegisteredClaimNames.Aud
            or JwtRegisteredClaimNames.Jti
            or JwtRegisteredClaimNames.Iat
            or SeedTokenConstants.VersionClaim
            or SeedTokenConstants.SeedClaim
            or SeedTokenConstants.GameVersionClaim
            or SeedTokenConstants.IronmonVersionClaim
            or SeedTokenConstants.DataModeClaim
            or SeedTokenConstants.ConfigurationClaim
            or SeedTokenConstants.CompatibilityFingerprintClaim;
    }

    /// <summary>
    /// Reads one exact required string.
    /// </summary>
    /// <param name="payload">The signed payload.</param>
    /// <param name="name">The claim name.</param>
    /// <param name="expected">The required value.</param>
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
    /// Reads one required bounded string.
    /// </summary>
    /// <param name="payload">The signed payload.</param>
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
    /// Reads one required 64-bit integer.
    /// </summary>
    /// <param name="payload">The signed payload.</param>
    /// <param name="name">The claim name.</param>
    /// <param name="value">The read value.</param>
    /// <returns>Whether the integer is present.</returns>
    private static bool TryGetInt64(JsonElement payload, string name, out long value)
    {
        value = 0;
        return payload.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value);
    }

    /// <summary>
    /// Reads one required 32-bit integer.
    /// </summary>
    /// <param name="payload">The signed payload.</param>
    /// <param name="name">The claim name.</param>
    /// <param name="value">The read value.</param>
    /// <returns>Whether the integer is present.</returns>
    private static bool TryGetInt32(JsonElement payload, string name, out int value)
    {
        value = 0;
        return payload.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
    }

    /// <summary>
    /// Creates a validation failure.
    /// </summary>
    /// <param name="status">The failure status.</param>
    /// <param name="message">The user-facing message.</param>
    /// <returns>The failure result.</returns>
    private static SeedTokenValidationResult Failure(SeedTokenValidationStatus status, string message)
        => SeedTokenValidationResult.Failure(status, message);

    /// <summary>
    /// Creates the standard claims failure.
    /// </summary>
    /// <returns>The invalid-claims result.</returns>
    private static SeedTokenValidationResult InvalidClaims()
        => Failure(SeedTokenValidationStatus.InvalidClaims, "Seeded-run token claims are invalid.");
}
