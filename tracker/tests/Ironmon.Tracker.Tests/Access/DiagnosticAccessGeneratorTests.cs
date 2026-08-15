using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

namespace Ironmon.Tracker.Tests.Access;

/// <summary>
/// Verifies generator selection rules, key import, and validator-compatible token creation.
/// </summary>
public sealed class DiagnosticAccessGeneratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Gets every supported capability with the minimum direct prerequisites needed to generate it.
    /// </summary>
    public static TheoryData<string, string[]> ValidCapabilitySelections
    {
        get
        {
            TheoryData<string, string[]> selections = [];
            foreach (string capability in DiagnosticCapabilityCatalog.KnownIds.Order(StringComparer.Ordinal))
                selections.Add(capability, GetMinimumValidSelection(capability));

            return selections;
        }
    }

    /// <summary>
    /// Gets every Pokemon-bound information capability that must fail without Pokemon availability.
    /// </summary>
    public static TheoryData<string> PokemonBoundCapabilities
        =>
        [
            DiagnosticCapabilities.EvolutionCandidates,
            DiagnosticCapabilities.EvolutionResults,
            DiagnosticCapabilities.FusionMaterialPairs,
            DiagnosticCapabilities.PokemonAbilities,
            DiagnosticCapabilities.PokemonBaseStats,
            DiagnosticCapabilities.PokemonMoveAccess,
            DiagnosticCapabilities.PokemonOverview
        ];

    /// <summary>
    /// Verifies All Active Pokemon owns and locks both quick-access availability grants.
    /// </summary>
    [Fact]
    public void AllActiveSelectionNormalizesQuickAccessGrants()
    {
        DiagnosticAccessSelection selection = new();
        selection.Set(DiagnosticCapabilities.PokemonCurrentPlayer, true);
        selection.Set(DiagnosticCapabilities.PokemonCurrentEnemies, true);
        selection.Set(DiagnosticCapabilities.PokemonAllActive, true);

        Assert.Equal([DiagnosticCapabilities.PokemonAllActive], selection.DirectCapabilities);
        Assert.True(selection.IsIncluded(DiagnosticCapabilities.PokemonCurrentPlayer));
        Assert.True(selection.IsIncluded(DiagnosticCapabilities.PokemonCurrentEnemies));

        selection.Set(DiagnosticCapabilities.PokemonCurrentPlayer, false);

        Assert.True(selection.IsSelected(DiagnosticCapabilities.PokemonCurrentPlayer));
    }

    /// <summary>
    /// Verifies applying a preset copies its grants so later edits cannot mutate the catalog.
    /// </summary>
    [Fact]
    public void PresetSelectionRemainsEditable()
    {
        DiagnosticAccessPreset preset = DiagnosticAccessPresetCatalog.All.First();
        DiagnosticAccessSelection selection = new();
        selection.Replace(preset.Capabilities);

        string removed = selection.DirectCapabilities[0];
        selection.Set(removed, false);

        Assert.DoesNotContain(removed, selection.DirectCapabilities);
        Assert.Contains(removed, preset.Capabilities);
    }

    /// <summary>
    /// Verifies Pokemon-bound information requires at least one Pokemon availability scope.
    /// </summary>
    [Theory]
    [MemberData(nameof(PokemonBoundCapabilities))]
    public void PokemonInformationWithoutAvailabilityIsRejected(string capability)
    {
        IReadOnlyList<string> errors = DiagnosticAccessSelectionValidator.GetErrors([capability]);

        Assert.Contains(errors, error => error.Contains("Pokemon information", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies each supported capability and its minimum dependencies produce a validator-compatible token.
    /// </summary>
    /// <param name="capability">The capability expected in the effective grant.</param>
    /// <param name="selection">The minimum valid direct selection.</param>
    [Theory]
    [MemberData(nameof(ValidCapabilitySelections))]
    public async Task EveryCapabilitySelectionGeneratesValidToken(string capability, string[] selection)
    {
        using ECDsa privateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using DiagnosticAccessSigningKey signingKey = DiagnosticAccessSigningKey.Import(privateKey.ExportPkcs8PrivateKeyPem(), "matrix.pem");
        DiagnosticAccessTokenGenerationRequest request = new($"matrix-{capability}", Now, Now.AddDays(1), null, selection);
        DiagnosticAccessTokenGenerationResult generated = new DiagnosticAccessTokenGenerator().Generate(signingKey, request);
        using ECDsa publicKey = ECDsa.Create(signingKey.ExportPublicParameters());
        ECDsaSecurityKey verificationKey = new(publicKey) { KeyId = signingKey.KeyId };
        DiagnosticAccessTokenValidator validator = new(new DiagnosticAccessKeyring([verificationKey]), new FixedTimeProvider(Now.AddMinutes(1)));

        DiagnosticAccessValidationResult validation = await validator.ValidateAsync(generated.Token);

        Assert.True(validation.IsValid);
        Assert.True(validation.Grant!.HasCapability(capability));
        Assert.Equal(selection.Order(StringComparer.Ordinal), validation.Grant.DirectCapabilities);
    }

    /// <summary>
    /// Verifies fusion previews reject a narrower availability selection.
    /// </summary>
    [Fact]
    public void FusionPreviewWithoutAllActiveIsRejected()
    {
        IReadOnlyList<string> errors = DiagnosticAccessSelectionValidator.GetErrors(
        [
            DiagnosticCapabilities.FusionPreviewResults,
            DiagnosticCapabilities.PokemonCurrentPlayer
        ]);

        Assert.Contains(errors, error => error.Contains("All Active Pokemon", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies an empty direct grant cannot produce an ineffective token.
    /// </summary>
    [Fact]
    public void EmptySelectionIsRejected()
    {
        using ECDsa privateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using DiagnosticAccessSigningKey signingKey = DiagnosticAccessSigningKey.Import(privateKey.ExportPkcs8PrivateKeyPem(), "empty.pem");
        DiagnosticAccessTokenGenerationRequest request = new("empty-selection", Now, Now.AddDays(1), null, []);

        Assert.Throws<ArgumentException>(() => new DiagnosticAccessTokenGenerator().Generate(signingKey, request));
    }

    /// <summary>
    /// Verifies a temporary token created from PKCS#8 validates against its exported public key.
    /// </summary>
    [Fact]
    public async Task TemporaryGeneratedTokenValidates()
    {
        using ECDsa privateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using DiagnosticAccessSigningKey signingKey = DiagnosticAccessSigningKey.Import(privateKey.ExportPkcs8PrivateKeyPem(), "support-key.pem");
        DiagnosticAccessTokenGenerationRequest request = new(
            "support-case-74",
            Now,
            Now.AddDays(7),
            "Ability mismatch",
            [DiagnosticCapabilities.PokemonCurrentPlayer, DiagnosticCapabilities.PokemonAbilities]);

        DiagnosticAccessTokenGenerationResult generated = new DiagnosticAccessTokenGenerator().Generate(signingKey, request);
        using ECDsa publicKey = ECDsa.Create(signingKey.ExportPublicParameters());
        ECDsaSecurityKey verificationKey = new(publicKey) { KeyId = signingKey.KeyId };
        DiagnosticAccessTokenValidator validator = new(new DiagnosticAccessKeyring([verificationKey]), new FixedTimeProvider(Now.AddMinutes(1)));

        DiagnosticAccessValidationResult validation = await validator.ValidateAsync(generated.Token);

        Assert.True(validation.IsValid);
        Assert.Equal(signingKey.KeyId, generated.KeyId);
        Assert.Equal("support-key.pem", signingKey.SourceName);
        Assert.Equal(Now.AddDays(7), validation.Grant!.ExpiresAt);
        Assert.Equal("Ability mismatch", validation.Grant.Note);
        Assert.Equal(request.Capabilities.Order(StringComparer.Ordinal), validation.Grant.DirectCapabilities);
    }

    /// <summary>
    /// Verifies omitting expiration creates a validator-compatible lifetime token.
    /// </summary>
    [Fact]
    public async Task LifetimeGeneratedTokenValidates()
    {
        using ECDsa privateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using DiagnosticAccessSigningKey signingKey = DiagnosticAccessSigningKey.Import(privateKey.ExportPkcs8PrivateKeyPem(), "lifetime.pem");
        DiagnosticAccessTokenGenerationRequest request = new("lifetime-case", Now, null, null, [DiagnosticCapabilities.TrackerProtocolHistory]);
        DiagnosticAccessTokenGenerationResult generated = new DiagnosticAccessTokenGenerator().Generate(signingKey, request);
        using ECDsa publicKey = ECDsa.Create(signingKey.ExportPublicParameters());
        ECDsaSecurityKey verificationKey = new(publicKey) { KeyId = signingKey.KeyId };
        DiagnosticAccessTokenValidator validator = new(new DiagnosticAccessKeyring([verificationKey]), new FixedTimeProvider(Now.AddYears(10)));

        DiagnosticAccessValidationResult validation = await validator.ValidateAsync(generated.Token);

        Assert.True(validation.IsValid);
        Assert.Null(generated.ExpiresAt);
        Assert.Null(validation.Grant!.ExpiresAt);
    }

    /// <summary>
    /// Verifies the same public key always derives the same bounded key identifier.
    /// </summary>
    [Fact]
    public void KeyIdentifierIsDeterministic()
    {
        using ECDsa privateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string pem = privateKey.ExportPkcs8PrivateKeyPem();
        using DiagnosticAccessSigningKey first = DiagnosticAccessSigningKey.Import(pem, "first.pem");
        using DiagnosticAccessSigningKey second = DiagnosticAccessSigningKey.Import(pem, "second.pem");

        Assert.Equal(first.KeyId, second.KeyId);
        Assert.StartsWith(DiagnosticAccessGeneratorConstants.KeyIdPrefix, first.KeyId, StringComparison.Ordinal);
        Assert.Equal(DiagnosticAccessGeneratorConstants.KeyIdPrefix.Length + DiagnosticAccessGeneratorConstants.KeyIdFingerprintBytes * 2, first.KeyId.Length);
    }

    /// <summary>
    /// Verifies only PKCS#8 P-256 private keys are accepted for signing.
    /// </summary>
    [Fact]
    public void UnsupportedPrivateKeyFormatsAreRejected()
    {
        using ECDsa p256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        Assert.Throws<ArgumentException>(() => DiagnosticAccessSigningKey.Import(p256.ExportECPrivateKeyPem(), "sec1.pem"));
        Assert.Throws<ArgumentException>(() => DiagnosticAccessSigningKey.Import(p384.ExportPkcs8PrivateKeyPem(), "p384.pem"));
        Assert.Throws<ArgumentException>(() => DiagnosticAccessSigningKey.Import("not a key", "invalid.pem"));
    }

    /// <summary>
    /// Gets the minimum valid direct selection for one supported capability.
    /// </summary>
    /// <param name="capability">The supported capability identifier.</param>
    /// <returns>The stable minimum valid direct selection.</returns>
    private static string[] GetMinimumValidSelection(string capability)
    {
        if (capability == DiagnosticCapabilities.FusionPreviewResults)
            return [DiagnosticCapabilities.FusionPreviewResults, DiagnosticCapabilities.PokemonAllActive];

        if (PokemonBoundCapabilities.Contains(capability))
            return [capability, DiagnosticCapabilities.PokemonCurrentPlayer];

        return [capability];
    }

    /// <summary>
    /// Provides one deterministic UTC instant to generator validation tests.
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
