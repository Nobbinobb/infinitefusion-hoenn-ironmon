using System.Text;
using System.Text.Json.Nodes;
using Ironmon.Tracker.Connection.Obtainability;

namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies that stat fingerprints retain exact formula ownership across archived, active, and pinned-pool worker contexts.
/// </summary>
public sealed class PlayerFusionStatFormulaCatalogTests
{
    private const string _resourceName = "Ironmon.Tracker.Connection.Resources.player_fusion_worker_catalog.json";
    private const string _schemaProperty = "schema_version";
    private const string _formulaProperty = "base_stat_formula_version";
    private const string _fingerprintProperty = "base_stat_source_fingerprint";
    private const string _legacyFingerprintProperty = "legacy_base_stat_source_fingerprint";
    private const string _fingerprintsProperty = "base_stat_source_fingerprints";
    private const string _legacyVersionKey = "1";
    private const string _versionTwoKey = "2";
    private const string _versionThreeKey = "3";
    private const string _legacyFingerprint = "1e25ae036f296389";
    private const string _versionTwoFingerprint = "2d3777878d24e250";
    private const string _versionThreeFingerprint = "ffffffffffffffff";
    private const string _unsupportedFingerprint = "0000000000000000";

    /// <summary>
    /// Selects all three formulas from any already pinned historical context without collapsing version two into version one.
    /// </summary>
    [Fact]
    public void HistoricalClonesRetainEveryFormulaIdentity()
    {
        PlayerFusionMappingWorkerCatalog catalog = ReadCatalog(VersionThreeDocument());
        foreach ((int firstVersion, string firstFingerprint) in catalog.BaseStatSourceFingerprints)
        {
            PlayerFusionMappingWorkerCatalog first = catalog.WithBaseStatFingerprint(firstFingerprint);
            Assert.Equal(firstVersion, first.BaseStatFormulaVersion);
            foreach ((int secondVersion, string secondFingerprint) in catalog.BaseStatSourceFingerprints)
            {
                PlayerFusionMappingWorkerCatalog second = first.WithBaseStatFingerprint(secondFingerprint);
                Assert.True(first.SupportsBaseStatFingerprint(secondFingerprint));
                Assert.Equal(secondVersion, second.BaseStatFormulaVersion);
                Assert.Equal(secondFingerprint, second.BaseStatSourceFingerprint);
            }
        }
    }

    /// <summary>
    /// Preserves the requested stat formula while importing the game's pinned pool from a historical context.
    /// </summary>
    /// <param name="formulaVersion">The exact formula to bind to the imported pool.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RuntimePoolRetainsTheRequestedFormula(int formulaVersion)
    {
        PlayerFusionMappingWorkerCatalog catalog = ReadCatalog(VersionThreeDocument());
        byte[] membership = new byte[(catalog.NormalSpeciesCount * catalog.NormalSpeciesCount + 7) / 8];
        foreach (PlayerFusionTargetSpecies target in catalog.CustomFusionPool)
        {
            int body = target.PackedComponents >> 10;
            int head = target.PackedComponents & 0x3ff;
            int position = (body - 1) * catalog.NormalSpeciesCount + head - 1;
            membership[position >> 3] |= (byte)(1 << (position & 7));
        }

        PlayerFusionMappingWorkerCatalog historical = catalog.WithBaseStatFingerprint(_legacyFingerprint);
        string fingerprint = catalog.BaseStatSourceFingerprints[formulaVersion];
        PlayerFusionMappingWorkerCatalog runtime = historical.WithRuntimeFusionPool(catalog.CustomFusionPoolVersion, catalog.CustomFusionPool.Count, catalog.CustomFusionPoolFingerprint, Convert.ToBase64String(membership), fingerprint);
        Assert.Equal(formulaVersion, runtime.BaseStatFormulaVersion);
        Assert.Equal(fingerprint, runtime.BaseStatSourceFingerprint);
        Assert.Equal(catalog.CustomFusionPool.Count, runtime.CustomFusionPool.Count);
        Assert.Equal(3, runtime.BaseStatSourceFingerprints.Count);
        Assert.Equal(3, runtime.WithBaseStatFingerprint(_versionThreeFingerprint).BaseStatFormulaVersion);
    }

    /// <summary>
    /// Reads exports predating the retained-formula map with their original current and legacy metadata.
    /// </summary>
    [Fact]
    public void SchemaThreeWithoutFormulaMapKeepsBothHistoricalVersions()
    {
        JsonObject document = VersionThreeDocument();
        document[_schemaProperty] = 3;
        document[_formulaProperty] = 2;
        document[_fingerprintProperty] = _versionTwoFingerprint;
        document.Remove(_fingerprintsProperty);
        PlayerFusionMappingWorkerCatalog catalog = ReadCatalog(document);
        Assert.Equal(2, catalog.BaseStatFormulaVersion);
        Assert.Equal(1, catalog.WithBaseStatFingerprint(_legacyFingerprint).BaseStatFormulaVersion);
        Assert.Equal(2, catalog.WithBaseStatFingerprint(_legacyFingerprint).WithBaseStatFingerprint(_versionTwoFingerprint).BaseStatFormulaVersion);
        Assert.False(catalog.SupportsBaseStatFingerprint(_versionThreeFingerprint));
    }

    /// <summary>
    /// Rejects an unknown identity instead of interpreting it using any retained formula.
    /// </summary>
    [Fact]
    public void UnknownFingerprintCannotBecomeAHistoricalDefault()
    {
        PlayerFusionMappingWorkerCatalog catalog = ReadCatalog(VersionThreeDocument());
        Assert.False(catalog.SupportsBaseStatFingerprint(_unsupportedFingerprint));
        Assert.Throws<InvalidDataException>(() => catalog.WithBaseStatFingerprint(_unsupportedFingerprint));
        Assert.Throws<InvalidDataException>(() => catalog.WithRuntimeFusionPool(catalog.CustomFusionPoolVersion, 0, string.Empty, string.Empty, _unsupportedFingerprint));
    }

    /// <summary>
    /// Rejects a current export that would strand version-two runs after an upgrade.
    /// </summary>
    [Fact]
    public void NewCatalogRequiresVersionTwoFingerprint()
    {
        JsonObject document = VersionThreeDocument();
        document[_fingerprintsProperty]!.AsObject().Remove(_versionTwoKey);
        Assert.Throws<InvalidDataException>(() => ReadCatalog(document));
    }

    /// <summary>
    /// Rejects ambiguous identities so two formula versions cannot share a worker-context key.
    /// </summary>
    [Fact]
    public void RetainedFormulasCannotShareAFingerprint()
    {
        JsonObject document = VersionThreeDocument();
        document[_fingerprintsProperty]![_versionTwoKey] = _legacyFingerprint;
        Assert.Throws<InvalidDataException>(() => ReadCatalog(document));
    }

    /// <summary>
    /// Uses the real catalog inputs with explicit three-version metadata for compatibility-boundary tests.
    /// </summary>
    /// <returns>A mutable generated-catalog document with distinct supported stat identities.</returns>
    private static JsonObject VersionThreeDocument()
    {
        using Stream stream = typeof(PlayerFusionMappingWorkerCatalog).Assembly.GetManifestResourceStream(_resourceName)
            ?? throw new InvalidDataException("The generated worker catalog is missing.");
        JsonObject document = JsonNode.Parse(stream)?.AsObject() ?? throw new InvalidDataException("The generated worker catalog is empty.");
        document[_schemaProperty] = 4;
        document[_formulaProperty] = 3;
        document[_fingerprintProperty] = _versionThreeFingerprint;
        document[_legacyFingerprintProperty] = _legacyFingerprint;
        document[_fingerprintsProperty] = new JsonObject
        {
            [_legacyVersionKey] = _legacyFingerprint,
            [_versionTwoKey] = _versionTwoFingerprint,
            [_versionThreeKey] = _versionThreeFingerprint
        };
        return document;
    }

    /// <summary>
    /// Loads one explicit export using the production parser and validation path.
    /// </summary>
    /// <param name="document">The catalog document to validate.</param>
    /// <returns>The immutable worker catalog.</returns>
    private static PlayerFusionMappingWorkerCatalog ReadCatalog(JsonObject document)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(document.ToJsonString()));
        return PlayerFusionMappingWorkerCatalog.Load(stream);
    }
}
