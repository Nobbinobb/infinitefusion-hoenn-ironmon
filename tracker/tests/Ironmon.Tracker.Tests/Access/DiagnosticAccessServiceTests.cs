using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

namespace Ironmon.Tracker.Tests.Access;

/// <summary>
/// Verifies tracker-owned diagnostic-access activation and lifecycle behavior.
/// </summary>
public sealed class DiagnosticAccessServiceTests : IDisposable
{
    private static readonly DateTimeOffset _now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ironmon-access-{Guid.NewGuid():N}");
    private readonly ECDsa _privateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _publicKey;
    private readonly DiagnosticAccessSigningKey _signingKey;
    private readonly MutableTimeProvider _timeProvider = new(_now);
    private readonly DiagnosticAccessTokenValidator _validator;

    /// <summary>
    /// Initializes a matching signing and validation key pair.
    /// </summary>
    public DiagnosticAccessServiceTests()
    {
        _publicKey = ECDsa.Create(_privateKey.ExportParameters(false));
        string privatePem = _privateKey.ExportPkcs8PrivateKeyPem();
        _signingKey = DiagnosticAccessSigningKey.Import(privatePem, "test-private.pem");
        ECDsaSecurityKey verificationKey = new(_publicKey) { KeyId = _signingKey.KeyId };
        _validator = new(new DiagnosticAccessKeyring([verificationKey]), _timeProvider);
    }

    /// <summary>
    /// Verifies activation persistence and restart restoration without retaining a raw token in the snapshot.
    /// </summary>
    [Fact]
    public async Task ValidTokenActivatesAndSurvivesRestart()
    {
        string token = CreateToken("first", _now.AddDays(1), [DiagnosticCapabilities.PokemonAllActive]);
        TrackerKnowledgeOptions options = new(_root);
        using (DiagnosticAccessService service = new(options, _validator, timeProvider: _timeProvider))
        {
            DiagnosticAccessValidationResult result = await service.ActivateAsync($"  {token}\r\n");

            Assert.True(result.IsValid);
            Assert.Equal(DiagnosticAccessState.Active, service.Snapshot.State);
            Assert.Equal("first", service.Snapshot.Grant!.TokenId);
            Assert.True(service.Snapshot.HasCapability(DiagnosticCapabilities.PokemonCurrentPlayer));
            Assert.True(service.Snapshot.HasCapability(DiagnosticCapabilities.PokemonCurrentEnemies));
        }

        using DiagnosticAccessService restored = new(options, _validator, timeProvider: _timeProvider);
        Assert.Equal(DiagnosticAccessState.Active, restored.Snapshot.State);
        Assert.Equal("first", restored.Snapshot.Grant!.TokenId);
        Assert.DoesNotContain(token, JsonSerializer.Serialize(restored.Snapshot));
    }

    /// <summary>
    /// Verifies invalid input never replaces an existing valid persisted grant.
    /// </summary>
    [Fact]
    public async Task InvalidInputDoesNotReplaceActiveToken()
    {
        TrackerKnowledgeOptions options = new(_root);
        using DiagnosticAccessService service = new(options, _validator, timeProvider: _timeProvider);
        await service.ActivateAsync(CreateToken("retained", _now.AddDays(1), [DiagnosticCapabilities.RunSeed]));

        DiagnosticAccessValidationResult rejected = await service.ActivateAsync("not-a-token");

        Assert.False(rejected.IsValid);
        Assert.Equal("retained", service.Snapshot.Grant!.TokenId);
        using DiagnosticAccessService restored = new(options, _validator, timeProvider: _timeProvider);
        Assert.Equal("retained", restored.Snapshot.Grant!.TokenId);
    }

    /// <summary>
    /// Verifies a valid replacement is atomic and removes its temporary file.
    /// </summary>
    [Fact]
    public async Task ValidReplacementUpdatesPersistedGrantAtomically()
    {
        TrackerKnowledgeOptions options = new(_root);
        using DiagnosticAccessService service = new(options, _validator, timeProvider: _timeProvider);
        await service.ActivateAsync(CreateToken("first", _now.AddDays(1), [DiagnosticCapabilities.RunSeed]));
        await service.ActivateAsync(CreateToken("second", null, [DiagnosticCapabilities.TrackerRawState]));

        Assert.Equal("second", service.Snapshot.Grant!.TokenId);
        Assert.True(service.Snapshot.IsLifetime);
        string tokenPath = Path.Combine(_root, TrackerStorageNames.SettingsDirectory, TrackerStorageNames.DiagnosticAccessTokenFile);
        Assert.False(File.Exists($"{tokenPath}{TrackerStorageNames.TemporaryExtension}"));
        using DiagnosticAccessService restored = new(options, _validator, timeProvider: _timeProvider);
        Assert.Equal("second", restored.Snapshot.Grant!.TokenId);
    }

    /// <summary>
    /// Verifies removal immediately clears access and survives restart.
    /// </summary>
    [Fact]
    public async Task RemovalClearsPersistedAndEffectiveAccess()
    {
        TrackerKnowledgeOptions options = new(_root);
        using DiagnosticAccessService service = new(options, _validator, timeProvider: _timeProvider);
        await service.ActivateAsync(CreateToken("remove", _now.AddDays(1), [DiagnosticCapabilities.RunSeed]));

        Assert.True(service.Remove());
        Assert.Equal(DiagnosticAccessState.None, service.Snapshot.State);
        Assert.Empty(service.Snapshot.EffectiveCapabilities);
        using DiagnosticAccessService restored = new(options, _validator, timeProvider: _timeProvider);
        Assert.Equal(DiagnosticAccessState.None, restored.Snapshot.State);
    }

    /// <summary>
    /// Verifies expiration removes effective capabilities and remains visible after restart.
    /// </summary>
    [Fact]
    public async Task ExpirationClearsCapabilitiesAndReportsExpiredState()
    {
        TrackerKnowledgeOptions options = new(_root);
        using DiagnosticAccessService service = new(options, _validator, timeProvider: _timeProvider);
        await service.ActivateAsync(CreateToken("temporary", _now.AddMinutes(5), [DiagnosticCapabilities.RunSeed]));
        _timeProvider.UtcNow = _now.AddMinutes(5);

        Assert.True(service.RefreshExpiration());
        Assert.Equal(DiagnosticAccessState.Expired, service.Snapshot.State);
        Assert.Empty(service.Snapshot.EffectiveCapabilities);
        Assert.Equal("temporary", service.Snapshot.Grant!.TokenId);
        using DiagnosticAccessService restored = new(options, _validator, timeProvider: _timeProvider);
        Assert.Equal(DiagnosticAccessState.Expired, restored.Snapshot.State);
        Assert.Null(restored.Snapshot.Grant);
    }

    /// <summary>
    /// Verifies Debug developer access grants every supported capability without writing a token.
    /// </summary>
    [Fact]
    public async Task DeveloperOverrideIsExplicitAndDoesNotPersistTokens()
    {
        TrackerKnowledgeOptions options = new(_root);
        using DiagnosticAccessService service = new(options, _validator, developerOverride: true, timeProvider: _timeProvider);

        DiagnosticAccessValidationResult result = await service.ActivateAsync(CreateToken("ignored", _now.AddDays(1), [DiagnosticCapabilities.RunSeed]));

        Assert.True(result.IsValid);
        Assert.Equal(DiagnosticAccessState.DeveloperOverride, service.Snapshot.State);
        Assert.Equal(DiagnosticCapabilityCatalog.KnownIds.Count, service.Snapshot.EffectiveCapabilities.Count);
        Assert.All(DiagnosticCapabilityCatalog.KnownIds, capability => Assert.True(service.Snapshot.HasCapability(capability)));
        string tokenPath = Path.Combine(_root, TrackerStorageNames.SettingsDirectory, TrackerStorageNames.DiagnosticAccessTokenFile);
        Assert.False(File.Exists(tokenPath));
        Assert.False(service.Remove());
    }

    /// <summary>
    /// Creates one validator-compatible token for lifecycle testing.
    /// </summary>
    /// <param name="tokenId">The unique token identifier.</param>
    /// <param name="expiresAt">The optional expiration instant.</param>
    /// <param name="capabilities">The direct capability grants.</param>
    /// <returns>The compact signed token.</returns>
    private string CreateToken(string tokenId, DateTimeOffset? expiresAt, IReadOnlyList<string> capabilities)
    {
        DiagnosticAccessTokenGenerator generator = new();
        DiagnosticAccessTokenGenerationRequest request = new(tokenId, _now, expiresAt, "Lifecycle test", capabilities);
        return generator.Generate(_signingKey, request).Token;
    }

    /// <summary>
    /// Releases cryptographic keys and temporary tracker-owned test data.
    /// </summary>
    public void Dispose()
    {
        _signingKey.Dispose();
        _privateKey.Dispose();
        _publicKey.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Provides mutable UTC time for deterministic lifecycle tests.
    /// </summary>
    /// <param name="utcNow">The initial UTC instant.</param>
    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        /// <summary>
        /// Gets or sets the current UTC instant.
        /// </summary>
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        /// <summary>
        /// Gets the configured UTC instant.
        /// </summary>
        /// <returns>The current test instant.</returns>
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
