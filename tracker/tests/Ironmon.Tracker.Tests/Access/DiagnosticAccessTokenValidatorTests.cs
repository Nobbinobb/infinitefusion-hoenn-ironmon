using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

namespace Ironmon.Tracker.Tests.Access;

/// <summary>
/// Verifies strict validation of signed diagnostic-access JWTs.
/// </summary>
public sealed class DiagnosticAccessTokenValidatorTests : IDisposable
{
    private const string KeyId = "test-key-2026";
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly ECDsa _privateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _publicKey;
    private readonly DiagnosticAccessTokenValidator _validator;

    /// <summary>
    /// Initializes the token validator with an isolated P-256 test key pair and fixed UTC clock.
    /// </summary>
    public DiagnosticAccessTokenValidatorTests()
    {
        _publicKey = ECDsa.Create(_privateKey.ExportParameters(false));
        ECDsaSecurityKey verificationKey = new(_publicKey) { KeyId = KeyId };
        _validator = new(new DiagnosticAccessKeyring([verificationKey]), new FixedTimeProvider(Now));
    }

    /// <summary>
    /// Verifies a temporary token returns direct, included, effective, and descriptive claims.
    /// </summary>
    [Fact]
    public async Task TemporaryTokenReturnsEffectiveGrant()
    {
        string token = CreateToken(
        [
            DiagnosticCapabilities.EvolutionCandidates,
            DiagnosticCapabilities.PokemonAllActive,
            DiagnosticCapabilities.PokemonOverview
        ], Now.AddHours(-1), Now.AddDays(7), note: "Evolution issue");

        DiagnosticAccessValidationResult result = await _validator.ValidateAsync(token);

        Assert.True(result.IsValid);
        Assert.Equal(DiagnosticAccessValidationStatus.Valid, result.Status);
        Assert.Empty(result.UnknownCapabilities);
        DiagnosticAccessGrant grant = Assert.IsType<DiagnosticAccessGrant>(result.Grant);
        Assert.Equal(KeyId, grant.KeyId);
        Assert.Equal("support-case-42", grant.TokenId);
        Assert.Equal(Now.AddHours(-1), grant.IssuedAt);
        Assert.Equal(Now.AddDays(7), grant.ExpiresAt);
        Assert.Equal("Evolution issue", grant.Note);
        Assert.True(grant.HasCapability(DiagnosticCapabilities.PokemonCurrentPlayer));
        Assert.True(grant.HasCapability(DiagnosticCapabilities.PokemonCurrentEnemies));
        Assert.True(grant.HasCapability(DiagnosticCapabilities.PokemonAllActive));
        Assert.True(grant.HasCapability(DiagnosticCapabilities.EvolutionCandidates));
        Assert.False(grant.HasCapability(DiagnosticCapabilities.EvolutionResults));
    }

    /// <summary>
    /// Verifies omitting exp creates valid lifetime access rather than a default library lifetime.
    /// </summary>
    [Fact]
    public async Task MissingExpirationCreatesLifetimeGrant()
    {
        string token = CreateToken([DiagnosticCapabilities.TrackerProtocolHistory], Now.AddDays(-30), null);

        DiagnosticAccessValidationResult result = await _validator.ValidateAsync(token);

        Assert.True(result.IsValid);
        Assert.Null(result.Grant!.ExpiresAt);
    }

    /// <summary>
    /// Verifies access expires exactly at the signed expiration instant.
    /// </summary>
    [Fact]
    public async Task ExpirationBoundaryFailsClosed()
    {
        string token = CreateToken([DiagnosticCapabilities.RunSeed], Now.AddHours(-1), Now);

        DiagnosticAccessValidationResult result = await _validator.ValidateAsync(token);

        Assert.Equal(DiagnosticAccessValidationStatus.Expired, result.Status);
        Assert.Null(result.Grant);
    }

    /// <summary>
    /// Verifies a valid token may carry future capability IDs without granting them.
    /// </summary>
    [Fact]
    public async Task UnknownCapabilitiesAreReportedButNotGranted()
    {
        string token = CreateToken(["future.capability", DiagnosticCapabilities.RunSeed], Now.AddHours(-1), Now.AddDays(1));

        DiagnosticAccessValidationResult result = await _validator.ValidateAsync(token);

        Assert.True(result.IsValid);
        Assert.Equal(["future.capability"], result.UnknownCapabilities);
        Assert.Equal([DiagnosticCapabilities.RunSeed], result.Grant!.DirectCapabilities);
        Assert.False(result.Grant.HasCapability("future.capability"));
    }

    /// <summary>
    /// Verifies duplicate or unsorted capability claims violate the compact application contract.
    /// </summary>
    [Theory]
    [InlineData(DiagnosticCapabilities.RunSeed, DiagnosticCapabilities.RunSeed)]
    [InlineData(DiagnosticCapabilities.RunSeed, DiagnosticCapabilities.PokemonOverview)]
    public async Task NonCanonicalCapabilityListsAreRejected(string first, string second)
    {
        string token = CreateToken([first, second], Now.AddHours(-1), Now.AddDays(1));

        DiagnosticAccessValidationResult result = await _validator.ValidateAsync(token);

        Assert.Equal(DiagnosticAccessValidationStatus.InvalidClaims, result.Status);
    }

    /// <summary>
    /// Verifies a signed token with the wrong application identity is rejected.
    /// </summary>
    [Theory]
    [InlineData("other-issuer", DiagnosticAccessTokenConstants.Audience, DiagnosticAccessTokenConstants.ContractVersion)]
    [InlineData(DiagnosticAccessTokenConstants.Issuer, "other-audience", DiagnosticAccessTokenConstants.ContractVersion)]
    [InlineData(DiagnosticAccessTokenConstants.Issuer, DiagnosticAccessTokenConstants.Audience, 2)]
    public async Task WrongApplicationClaimsAreRejected(string issuer, string audience, int version)
    {
        string token = CreateToken([DiagnosticCapabilities.RunSeed], Now.AddHours(-1), Now.AddDays(1), issuer: issuer, audience: audience, version: version);

        DiagnosticAccessValidationResult result = await _validator.ValidateAsync(token);

        Assert.Equal(DiagnosticAccessValidationStatus.InvalidClaims, result.Status);
    }

    /// <summary>
    /// Verifies the protected application token type is exact and case-sensitive.
    /// </summary>
    [Fact]
    public async Task WrongTokenTypeIsRejected()
    {
        string token = CreateToken([DiagnosticCapabilities.RunSeed], Now.AddHours(-1), Now.AddDays(1), tokenType: "JWT");

        DiagnosticAccessValidationResult result = await _validator.ValidateAsync(token);

        Assert.Equal(DiagnosticAccessValidationStatus.UnsupportedType, result.Status);
    }

    /// <summary>
    /// Verifies unsecured JWTs are never accepted.
    /// </summary>
    [Fact]
    public async Task UnsecuredTokenIsRejected()
    {
        string token = CreateToken([DiagnosticCapabilities.RunSeed], Now.AddHours(-1), Now.AddDays(1), signed: false);

        DiagnosticAccessValidationResult result = await _validator.ValidateAsync(token);

        Assert.Equal(DiagnosticAccessValidationStatus.UnsupportedAlgorithm, result.Status);
    }

    /// <summary>
    /// Verifies a valid signature from an unregistered key ID grants nothing.
    /// </summary>
    [Fact]
    public async Task UnknownSigningKeyIsRejected()
    {
        string token = CreateToken([DiagnosticCapabilities.RunSeed], Now.AddHours(-1), Now.AddDays(1), keyId: "unknown-key");

        DiagnosticAccessValidationResult result = await _validator.ValidateAsync(token);

        Assert.Equal(DiagnosticAccessValidationStatus.UnknownKey, result.Status);
    }

    /// <summary>
    /// Verifies payload modification after signing invalidates the signature.
    /// </summary>
    [Fact]
    public async Task PayloadTamperingIsRejected()
    {
        string token = CreateToken([DiagnosticCapabilities.RunSeed], Now.AddHours(-1), Now.AddDays(1));
        string[] parts = token.Split('.');
        string payload = Base64UrlEncoder.Decode(parts[1]).Replace("support-case-42", "support-case-43", StringComparison.Ordinal);
        string altered = $"{parts[0]}.{Base64UrlEncoder.Encode(payload)}.{parts[2]}";

        DiagnosticAccessValidationResult result = await _validator.ValidateAsync(altered);

        Assert.Equal(DiagnosticAccessValidationStatus.InvalidSignature, result.Status);
    }

    /// <summary>
    /// Verifies a different private key cannot sign under a trusted key ID.
    /// </summary>
    [Fact]
    public async Task SignatureFromDifferentKeyIsRejected()
    {
        using ECDsa otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string token = CreateToken([DiagnosticCapabilities.RunSeed], Now.AddHours(-1), Now.AddDays(1), signingKey: otherKey);

        DiagnosticAccessValidationResult result = await _validator.ValidateAsync(token);

        Assert.Equal(DiagnosticAccessValidationStatus.InvalidSignature, result.Status);
    }

    /// <summary>
    /// Verifies malformed and oversized inputs fail before cryptographic validation.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    public async Task MalformedInputIsRejected(string? token)
    {
        DiagnosticAccessValidationResult result = await _validator.ValidateAsync(token);

        Assert.Equal(DiagnosticAccessValidationStatus.Malformed, result.Status);
    }

    /// <summary>
    /// Releases the test key pair.
    /// </summary>
    public void Dispose()
    {
        _publicKey.Dispose();
        _privateKey.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Creates one compact test JWT with independently adjustable protected and payload claims.
    /// </summary>
    /// <param name="capabilities">The capability values in serialized order.</param>
    /// <param name="issuedAt">The issue instant.</param>
    /// <param name="expiresAt">The optional expiration instant.</param>
    /// <param name="note">The optional support note.</param>
    /// <param name="issuer">The issuer claim.</param>
    /// <param name="audience">The audience claim.</param>
    /// <param name="version">The application contract version.</param>
    /// <param name="tokenType">The protected token type.</param>
    /// <param name="keyId">The protected signing-key ID.</param>
    /// <param name="signed">Whether to sign with ES256.</param>
    /// <param name="signingKey">An optional alternate signing key.</param>
    /// <returns>The compact test JWT.</returns>
    private string CreateToken(IReadOnlyList<string> capabilities, DateTimeOffset issuedAt, DateTimeOffset? expiresAt, string? note = null, string issuer = DiagnosticAccessTokenConstants.Issuer, string audience = DiagnosticAccessTokenConstants.Audience, int version = DiagnosticAccessTokenConstants.ContractVersion, string tokenType = DiagnosticAccessTokenConstants.TokenType, string keyId = KeyId, bool signed = true, ECDsa? signingKey = null)
    {
        Dictionary<string, object> claims = new(StringComparer.Ordinal)
        {
            [DiagnosticAccessTokenConstants.VersionClaim] = version,
            [JwtRegisteredClaimNames.Jti] = "support-case-42",
            [DiagnosticAccessTokenConstants.CapabilitiesClaim] = capabilities
        };

        if (note is not null)
            claims[DiagnosticAccessTokenConstants.NoteClaim] = note;

        ECDsaSecurityKey securityKey = new(signingKey ?? _privateKey) { KeyId = keyId };
        SecurityTokenDescriptor descriptor = new()
        {
            Audience = audience,
            Claims = claims,
            Expires = expiresAt?.UtcDateTime,
            IssuedAt = issuedAt.UtcDateTime,
            Issuer = issuer,
            SigningCredentials = signed ? new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256) : null,
            TokenType = tokenType
        };

        JsonWebTokenHandler handler = new() { SetDefaultTimesOnTokenCreation = false };
        return handler.CreateToken(descriptor);
    }

    /// <summary>
    /// Provides one deterministic UTC instant to token-validation tests.
    /// </summary>
    /// <param name="utcNow">The fixed UTC instant.</param>
    /// <remarks>Initializes a deterministic time provider.</remarks>
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        /// <summary>
        /// Gets the fixed UTC instant.
        /// </summary>
        /// <returns>The configured instant.</returns>
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
