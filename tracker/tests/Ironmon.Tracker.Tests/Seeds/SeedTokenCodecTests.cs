using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ironmon.Tracker.Tests.Seeds;

/// <summary>
/// Verifies the shareable seeded-run JWS contract and codec.
/// </summary>
public sealed class SeedTokenCodecTests
{
    private static readonly byte[] _key = [.. Enumerable.Range(1, SeedTokenConstants.MinimumKeySize).Select(value => (byte)value)];
    private static readonly DateTimeOffset _issuedAt = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private readonly SeedTokenCodec _codec = new(_key);

    /// <summary>
    /// Verifies export and import preserve exactly the normalized new-run inputs.
    /// </summary>
    [Fact]
    public async Task ValidTokenRoundTripsNormalizedRunInputs()
    {
        CompletedRunRecipePayload recipe = CreateRecipe();
        string token = _codec.Create(recipe, "seed-token-42", _issuedAt);

        SeedTokenValidationResult result = await _codec.ValidateAsync(token);

        Assert.True(result.IsValid);
        SeedTokenData data = Assert.IsType<SeedTokenData>(result.Data);
        Assert.Equal("seed-token-42", data.TokenId);
        Assert.Equal(_issuedAt, data.IssuedAt);
        Assert.Equal(recipe.Seed, data.Seed);
        Assert.Equal(recipe.GameVersion, data.GameVersion);
        Assert.Equal(recipe.IronmonVersion, data.IronmonVersion);
        Assert.Equal(recipe.DataMode, data.DataMode);
        Assert.Equal(recipe.Configuration.WildPolicy, data.Configuration.WildPolicy);
        Assert.Equal(recipe.Configuration.TrainerPolicy, data.Configuration.TrainerPolicy);
        Assert.Equal(recipe.Configuration.UnfusionSetting, data.Configuration.UnfusionSetting);
        Assert.True(data.Configuration.AutomaticReset);
        Assert.Equal(RunCompatibilityFingerprint.Create(recipe), data.CompatibilityFingerprint);
    }

    /// <summary>
    /// Verifies independently created ordinary trackers share the public token-family integrity material.
    /// </summary>
    [Fact]
    public async Task SharedApplicationMaterialInteroperatesAcrossTrackers()
    {
        SeedTokenCodec exporter = new(SeedTokenSharedKey.Material);
        SeedTokenCodec importer = new(SeedTokenSharedKey.Material);
        string token = exporter.Create(CreateRecipe(), "shared-seed-token", _issuedAt);

        SeedTokenValidationResult result = await importer.ValidateAsync(token);

        Assert.True(result.IsValid);
        Assert.Equal("shared-seed-token", result.Data?.TokenId);
    }

    /// <summary>
    /// Verifies a persisted completed-run recipe can create the same portable token contract without a game connection.
    /// </summary>
    [Fact]
    public async Task ArchivedCompletedRunCreatesPortableToken()
    {
        Dictionary<string, string> itemMappings = new() { ["POTION"] = "ETHER" };
        CompletedRunRecipePayload recipe = CreateRecipe(result: "lost", itemMappings: itemMappings);

        string token = _codec.Create(recipe, "archived-run-token", _issuedAt);
        SeedTokenValidationResult result = await _codec.ValidateAsync(token);

        Assert.True(result.IsValid);
        Assert.Equal(recipe.Seed, result.Data?.Seed);
        Assert.Equal(RunCompatibilityFingerprint.Create(recipe), result.Data?.CompatibilityFingerprint);
    }

    /// <summary>
    /// Verifies diagnostic and generic JWT types cannot enter the seeded-run token family.
    /// </summary>
    [Theory]
    [InlineData(DiagnosticAccessTokenConstants.TokenType)]
    [InlineData("JWT")]
    public async Task OtherTokenFamiliesAreRejected(string tokenType)
    {
        string token = CreateRawToken(tokenType: tokenType);

        SeedTokenValidationResult result = await _codec.ValidateAsync(token);

        Assert.Equal(SeedTokenValidationStatus.UnsupportedType, result.Status);
        Assert.Null(result.Data);
    }

    /// <summary>
    /// Verifies algorithms outside the fixed HS256 policy are rejected before claims are trusted.
    /// </summary>
    [Fact]
    public async Task UnsupportedAlgorithmIsRejected()
    {
        byte[] longerKey = [.. Enumerable.Range(1, 64).Select(value => (byte)value)];
        string token = CreateRawToken(algorithm: SecurityAlgorithms.HmacSha512, signingKey: longerKey);

        SeedTokenValidationResult result = await _codec.ValidateAsync(token);

        Assert.Equal(SeedTokenValidationStatus.UnsupportedAlgorithm, result.Status);
    }

    /// <summary>
    /// Verifies the seed-token key identity is exact and isolated from other local token uses.
    /// </summary>
    [Fact]
    public async Task UnknownKeyIdentityIsRejected()
    {
        string token = CreateRawToken(keyId: "other-local-key");

        SeedTokenValidationResult result = await _codec.ValidateAsync(token);

        Assert.Equal(SeedTokenValidationStatus.UnknownKey, result.Status);
    }

    /// <summary>
    /// Verifies payload modification after export invalidates the signature.
    /// </summary>
    [Fact]
    public async Task TamperedPayloadIsRejected()
    {
        string token = _codec.Create(CreateRecipe(), "seed-token-42", _issuedAt);
        string[] parts = token.Split('.');
        string payload = Base64UrlEncoder.Decode(parts[1]).Replace("6.7.2", "6.7.3", StringComparison.Ordinal);
        string tampered = $"{parts[0]}.{Base64UrlEncoder.Encode(payload)}.{parts[2]}";

        SeedTokenValidationResult result = await _codec.ValidateAsync(tampered);

        Assert.Equal(SeedTokenValidationStatus.InvalidSignature, result.Status);
    }

    /// <summary>
    /// Verifies a different local key cannot produce an accepted signature under the recognized key ID.
    /// </summary>
    [Fact]
    public async Task SignatureFromDifferentKeyIsRejected()
    {
        byte[] otherKey = [.. Enumerable.Range(65, SeedTokenConstants.MinimumKeySize).Select(value => (byte)value)];
        string token = CreateRawToken(signingKey: otherKey);

        SeedTokenValidationResult result = await _codec.ValidateAsync(token);

        Assert.Equal(SeedTokenValidationStatus.InvalidSignature, result.Status);
    }

    /// <summary>
    /// Verifies malformed input and plain JSON never reach signed-claim validation.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("{\"seed\":42}")]
    public async Task MalformedInputIsRejected(string? token)
    {
        SeedTokenValidationResult result = await _codec.ValidateAsync(token);

        Assert.Equal(SeedTokenValidationStatus.Malformed, result.Status);
    }

    /// <summary>
    /// Verifies signed but invalid seed and configuration values fail closed.
    /// </summary>
    [Theory]
    [InlineData(-1, 3, "mixed")]
    [InlineData(2147483647, 3, "mixed")]
    [InlineData(42, 2, "mixed")]
    [InlineData(42, 3, "unsupported")]
    public async Task InvalidNewRunInputsAreRejected(long seed, int configurationVersion, string wildPolicy)
    {
        RunConfigurationPayload configuration = CreateConfiguration(configurationVersion, wildPolicy);
        string token = CreateRawToken(seed: seed, configuration: configuration);

        SeedTokenValidationResult result = await _codec.ValidateAsync(token);

        Assert.Equal(SeedTokenValidationStatus.InvalidClaims, result.Status);
    }

    /// <summary>
    /// Verifies issuer, audience, and contract version identities are exact.
    /// </summary>
    [Theory]
    [InlineData("other-issuer", SeedTokenConstants.Audience, SeedTokenConstants.ContractVersion)]
    [InlineData(SeedTokenConstants.Issuer, "other-audience", SeedTokenConstants.ContractVersion)]
    [InlineData(SeedTokenConstants.Issuer, SeedTokenConstants.Audience, 2)]
    public async Task WrongApplicationClaimsAreRejected(string issuer, string audience, int version)
    {
        string token = CreateRawToken(issuer: issuer, audience: audience, version: version);

        SeedTokenValidationResult result = await _codec.ValidateAsync(token);

        Assert.Equal(SeedTokenValidationStatus.InvalidClaims, result.Status);
    }

    /// <summary>
    /// Verifies compatibility fingerprints must be canonical lowercase SHA-256 text.
    /// </summary>
    [Theory]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task InvalidCompatibilityFingerprintIsRejected(string fingerprint)
    {
        string token = CreateRawToken(fingerprint: fingerprint);

        SeedTokenValidationResult result = await _codec.ValidateAsync(token);

        Assert.Equal(SeedTokenValidationStatus.InvalidClaims, result.Status);
    }

    /// <summary>
    /// Verifies the fixed claim set rejects signed extensions until a new contract version defines them.
    /// </summary>
    [Fact]
    public async Task UnexpectedSignedClaimIsRejected()
    {
        string token = CreateRawToken(addUnexpectedClaim: true);

        SeedTokenValidationResult result = await _codec.ValidateAsync(token);

        Assert.Equal(SeedTokenValidationStatus.InvalidClaims, result.Status);
    }

    /// <summary>
    /// Verifies missing and unknown nested configuration properties are rejected.
    /// </summary>
    [Fact]
    public async Task ConfigurationClaimShapeIsExact()
    {
        var missingAutomaticReset = new
        {
            SchemaVersion = 3,
            WildPolicy = "mixed",
            TrainerPolicy = "mixed",
            UnfusionSetting = "random_component"
        };

        var unexpectedProperty = new
        {
            SchemaVersion = 3,
            WildPolicy = "mixed",
            TrainerPolicy = "mixed",
            UnfusionSetting = "random_component",
            AutomaticReset = false,
            FutureSetting = true
        };

        SeedTokenValidationResult missing = await _codec.ValidateAsync(CreateRawToken(configuration: missingAutomaticReset));
        SeedTokenValidationResult unexpected = await _codec.ValidateAsync(CreateRawToken(configuration: unexpectedProperty));

        Assert.Equal(SeedTokenValidationStatus.InvalidClaims, missing.Status);
        Assert.Equal(SeedTokenValidationStatus.InvalidClaims, unexpected.Status);
    }

    /// <summary>
    /// Verifies the encoder refuses an invalid source recipe rather than producing an unusable token.
    /// </summary>
    [Fact]
    public void InvalidExportRecipeIsRejected()
    {
        CompletedRunRecipePayload recipe = CreateRecipe(seed: -1);

        Assert.Throws<ArgumentException>(() => _codec.Create(recipe, "seed-token-42", _issuedAt));
    }

    /// <summary>
    /// Verifies application keys shorter than the HS256 minimum are rejected.
    /// </summary>
    [Fact]
    public void ShortApplicationKeyIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new SeedTokenCodec(new byte[SeedTokenConstants.MinimumKeySize - 1]));
    }

    /// <summary>
    /// Creates one independently signed token whose protected header and new-run claims can be varied.
    /// </summary>
    /// <param name="tokenType">The protected token type.</param>
    /// <param name="algorithm">The signing algorithm.</param>
    /// <param name="keyId">The protected key identity.</param>
    /// <param name="signingKey">The signing key material.</param>
    /// <param name="seed">The signed run seed.</param>
    /// <param name="configuration">The signed run configuration.</param>
    /// <param name="issuer">The signed issuer.</param>
    /// <param name="audience">The signed audience.</param>
    /// <param name="version">The signed contract version.</param>
    /// <param name="fingerprint">The signed compatibility fingerprint.</param>
    /// <param name="addUnexpectedClaim">Whether to add one unsupported signed claim.</param>
    /// <returns>The compact signed token.</returns>
    private static string CreateRawToken(string tokenType = SeedTokenConstants.TokenType, string algorithm = SecurityAlgorithms.HmacSha256, string keyId = SeedTokenConstants.KeyId, byte[]? signingKey = null, long seed = 42, object? configuration = null, string issuer = SeedTokenConstants.Issuer, string audience = SeedTokenConstants.Audience, int version = SeedTokenConstants.ContractVersion, string? fingerprint = null, bool addUnexpectedClaim = false)
    {
        SymmetricSecurityKey key = new(signingKey ?? _key) { KeyId = keyId };
        Dictionary<string, object> claims = new(StringComparer.Ordinal)
        {
            [SeedTokenConstants.VersionClaim] = version,
            [JwtRegisteredClaimNames.Jti] = "seed-token-42",
            [SeedTokenConstants.SeedClaim] = seed,
            [SeedTokenConstants.GameVersionClaim] = "6.7.2",
            [SeedTokenConstants.IronmonVersionClaim] = "0.7.7",
            [SeedTokenConstants.DataModeClaim] = "classic",
            [SeedTokenConstants.ConfigurationClaim] = TrackerJson.SerializePayload(configuration ?? CreateConfiguration()),
            [SeedTokenConstants.CompatibilityFingerprintClaim] = fingerprint ?? new string('a', 64)
        };

        if (addUnexpectedClaim)
            claims["future_claim"] = true;

        SecurityTokenDescriptor descriptor = new()
        {
            Audience = audience,
            Claims = claims,
            IssuedAt = _issuedAt.UtcDateTime,
            Issuer = issuer,
            SigningCredentials = new SigningCredentials(key, algorithm),
            TokenType = tokenType
        };

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }

    /// <summary>
    /// Creates a valid reproduction recipe for token tests.
    /// </summary>
    /// <param name="seed">The deterministic run seed.</param>
    /// <param name="result">The archived attempt result.</param>
    /// <param name="itemMappings">Optional observed item mappings.</param>
    /// <returns>The complete recipe.</returns>
    private static CompletedRunRecipePayload CreateRecipe(long seed = 42, string result = "active", IReadOnlyDictionary<string, string>? itemMappings = null)
    {
        return new CompletedRunRecipePayload
        {
            RunId = "run-42",
            Seed = seed,
            Result = result,
            ItemMappings = itemMappings ?? new Dictionary<string, string>(),
            GameVersion = "6.7.2",
            IronmonVersion = "0.7.7",
            Configuration = CreateConfiguration(),
            DataMode = "classic",
            SpeciesGenerator = new SpeciesGeneratorRecipePayload { Version = 1, PoolFingerprint = "species" },
            AbilityGenerator = new AbilityGeneratorRecipePayload { Version = 3, PoolSize = 310, PoolFingerprint = "abilities" },
            PlayerFusionGenerator = new PlayerFusionGeneratorRecipePayload { Version = 2, PoolSize = 174348, PoolFingerprint = "fusions" }
        };
    }

    /// <summary>
    /// Creates a run configuration for token tests.
    /// </summary>
    /// <param name="schemaVersion">The configuration schema.</param>
    /// <param name="wildPolicy">The wild-species policy.</param>
    /// <returns>The run configuration.</returns>
    private static RunConfigurationPayload CreateConfiguration(int schemaVersion = SeedTokenConstants.ConfigurationSchemaVersion, string wildPolicy = "mixed")
    {
        return new RunConfigurationPayload
        {
            SchemaVersion = schemaVersion,
            WildPolicy = wildPolicy,
            TrainerPolicy = "custom_fusions_only",
            UnfusionSetting = "player_choice",
            AutomaticReset = true
        };
    }
}
