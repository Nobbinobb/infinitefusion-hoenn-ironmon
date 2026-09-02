using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Ironmon.Tracker.Tests.Core;

/// <summary>
/// Verifies strict loading and population invariants for release type-coverage data.
/// </summary>
public sealed class TypeCoverageDatasetJsonTests
{
    private const string _coverageFileName = "type_coverage.json";
    private const string _expectedGameVersion = "6.8.2";
    private const string _fingerprintFormat = "x16";
    private const string _fusionBodyPrefix = "B";
    private const string _fusionComponentFileName = "generation_custom_fusion_pool.bin";
    private const string _fusionComponentName = "custom_fusion_pool";
    private const string _fusionHeadSeparator = "H";
    private const string _profileFileName = "generation_profile.json";
    private const string _profileIdPropertyName = "profile_id";
    private const string _profileManifestPropertyName = "manifest";

    /// <summary>
    /// Initializes type-coverage dataset tests.
    /// </summary>
    public TypeCoverageDatasetJsonTests()
    {
    }

    /// <summary>
    /// Verifies that the stable standard type catalog contains every ordinary type once.
    /// </summary>
    [Fact]
    public void StandardTypeCatalogContainsEighteenUniqueTypes()
    {
        Assert.Equal(18, PokemonTypeCatalog.StandardTypes.Count);
        Assert.Equal(18, PokemonTypeCatalog.StandardTypes.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("NORMAL", PokemonTypeCatalog.StandardTypes[0]);
        Assert.Equal("FAIRY", PokemonTypeCatalog.StandardTypes[^1]);
    }

    /// <summary>
    /// Verifies strict snake-case JSON loading for a valid aggregate dataset.
    /// </summary>
    [Fact]
    public void DeserializeReturnsValidatedDataset()
    {
        const string json = """
            {
              "schema_version": 1,
              "game_version": "6.8.0",
              "normal_pool_size": 2,
              "normal_pool_fingerprint": "0123456789abcdef",
              "fusion_pool_schema_version": 2,
              "fusion_pool_size": 3,
              "fusion_pool_fingerprint": "fedcba9876543210",
              "profiles": [
                { "types": ["NORMAL"], "normal_count": 2, "fusion_count": 0 },
                { "types": ["WATER", "GROUND"], "normal_count": 0, "fusion_count": 3 }
              ]
            }
            """;
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));

        TypeCoverageDataset dataset = TypeCoverageDatasetJson.Deserialize(stream);

        Assert.Equal(2, dataset.Profiles.Count);
        Assert.Equal(3, dataset.FusionPoolSize);
    }

    /// <summary>
    /// Verifies that generated coverage describes the finalized generation profile.
    /// </summary>
    [Fact]
    public void GeneratedReleaseDatasetMatchesGenerationProfileFusionPool()
    {
        string coveragePath = Path.Combine(AppContext.BaseDirectory, _coverageFileName);
        string poolPath = Path.Combine(AppContext.BaseDirectory, _fusionComponentFileName);
        string profilePath = Path.Combine(AppContext.BaseDirectory, _profileFileName);
        using FileStream stream = File.OpenRead(coveragePath);

        TypeCoverageDataset dataset = TypeCoverageDatasetJson.Deserialize(stream);
        byte[] poolBytes = File.ReadAllBytes(poolPath);
        CustomFusionPoolComponent pool = CustomFusionPoolComponentCodec.Decode(poolBytes);
        using JsonDocument profileDocument = JsonDocument.Parse(File.ReadAllBytes(profilePath));
        GenerationProfilePayload profile = TrackerJson.DeserializePayload<GenerationProfilePayload>(profileDocument.RootElement.GetProperty(_profileManifestPropertyName));
        string profileId = profileDocument.RootElement.GetProperty(_profileIdPropertyName).GetString() ?? string.Empty;
        GenerationAlgorithmPayload fusionAlgorithm = profile.Algorithms.Single(algorithm => StringComparer.Ordinal.Equals(algorithm.Name, GenerationAlgorithmNames.CustomFusionEligibility));
        GenerationComponentPayload fusionComponent = profile.Components.Single(component => StringComparer.Ordinal.Equals(component.Name, _fusionComponentName));

        Assert.Equal(_expectedGameVersion, dataset.GameVersion);
        Assert.Equal(GenerationProfileFingerprint.Create(profile), profileId);
        Assert.Equal(fusionAlgorithm.Version, dataset.FusionPoolSchemaVersion);
        Assert.Equal(pool.NormalSpeciesCount, dataset.NormalPoolSize);
        Assert.Equal(pool.EligibleCount, dataset.FusionPoolSize);
        Assert.Equal(CreateFusionPoolFingerprint(pool), dataset.FusionPoolFingerprint);
        Assert.Equal(pool.SchemaVersion, fusionComponent.SchemaVersion);
        Assert.Equal(poolBytes.LongLength, fusionComponent.ByteLength);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(poolBytes)).ToLowerInvariant(), fusionComponent.Sha256);
        Assert.Equal(170, dataset.Profiles.Count);
    }

    /// <summary>
    /// Reproduces the runtime's FNV-1a fingerprint for the packed fusion pool.
    /// </summary>
    /// <param name="pool">The decoded profile fusion pool.</param>
    /// <returns>The lowercase 64-bit fingerprint.</returns>
    private static string CreateFusionPoolFingerprint(CustomFusionPoolComponent pool)
    {
        const ulong offsetBasis = 14_695_981_039_346_656_037;
        const ulong prime = 1_099_511_628_211;
        ulong value = offsetBasis;
        unchecked
        {
            foreach (CustomFusionPair pair in pool.Enumerate())
            {
                string identity = FormattableString.Invariant($"{_fusionBodyPrefix}{pair.BodyId}{_fusionHeadSeparator}{pair.HeadId}");
                foreach (byte character in Encoding.UTF8.GetBytes(identity))
                {
                    value ^= character;
                    value *= prime;
                }

                value *= prime;
            }
        }

        return value.ToString(_fingerprintFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Verifies that aggregate totals must match the declared release pools.
    /// </summary>
    [Fact]
    public void ValidateRejectsPopulationMismatch()
    {
        TypeCoverageDataset dataset = CreateDataset(
            [new() { Types = ["NORMAL"], NormalCount = 1, FusionCount = 3 }]
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => TypeCoverageDatasetJson.Validate(dataset));

        Assert.Contains("normal count", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that profile types must use stable chart order.
    /// </summary>
    [Fact]
    public void ValidateRejectsNonCanonicalTypeOrder()
    {
        TypeCoverageDataset dataset = CreateDataset(
            [new() { Types = ["GROUND", "WATER"], NormalCount = 2, FusionCount = 3 }]
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => TypeCoverageDatasetJson.Validate(dataset));

        Assert.Contains("stable chart order", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that duplicate defensive profiles cannot enter the packaged dataset.
    /// </summary>
    [Fact]
    public void ValidateRejectsDuplicateProfiles()
    {
        TypeCoverageDataset dataset = CreateDataset(
        [
            new() { Types = ["NORMAL"], NormalCount = 1, FusionCount = 1 },
            new() { Types = ["NORMAL"], NormalCount = 1, FusionCount = 2 }
        ]);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => TypeCoverageDatasetJson.Validate(dataset));

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that explicit nulls cannot bypass non-null dataset declarations.
    /// </summary>
    [Fact]
    public void DeserializeRejectsNullFingerprint()
    {
        const string json = """
            {
              "schema_version": 1,
              "game_version": "6.8.0",
              "normal_pool_size": 2,
              "normal_pool_fingerprint": null,
              "fusion_pool_schema_version": 2,
              "fusion_pool_size": 3,
              "fusion_pool_fingerprint": "fedcba9876543210",
              "profiles": [
                { "types": ["NORMAL"], "normal_count": 2, "fusion_count": 3 }
              ]
            }
            """;
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => TypeCoverageDatasetJson.Deserialize(stream));

        Assert.Contains("fingerprint", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that unexpected fields cannot silently change the release contract.
    /// </summary>
    [Fact]
    public void DeserializeRejectsUnexpectedField()
    {
        const string json = """
            {
              "schema_version": 1,
              "game_version": "6.8.0",
              "normal_pool_size": 2,
              "normal_pool_fingerprint": "0123456789abcdef",
              "fusion_pool_schema_version": 2,
              "fusion_pool_size": 3,
              "fusion_pool_fingerprint": "fedcba9876543210",
              "profiles": [
                { "types": ["NORMAL"], "normal_count": 2, "fusion_count": 3 }
              ],
              "species": []
            }
            """;
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));

        Assert.Throws<JsonException>(() => TypeCoverageDatasetJson.Deserialize(stream));
    }

    /// <summary>
    /// Creates a dataset with fixed metadata and supplied profiles.
    /// </summary>
    /// <param name="profiles">The profiles under test.</param>
    /// <returns>The dataset to validate.</returns>
    private static TypeCoverageDataset CreateDataset(IReadOnlyList<TypeCoverageProfile> profiles)
        => new()
        {
            SchemaVersion = TypeCoverageDatasetJson.SchemaVersion,
            GameVersion = "6.8.0",
            NormalPoolSize = 2,
            NormalPoolFingerprint = "0123456789abcdef",
            FusionPoolSchemaVersion = 2,
            FusionPoolSize = 3,
            FusionPoolFingerprint = "fedcba9876543210",
            Profiles = profiles
        };
}
