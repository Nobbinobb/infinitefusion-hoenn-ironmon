using System.Text;

namespace Ironmon.Tracker.Tests.Core;

/// <summary>
/// Verifies strict loading and population invariants for release type-coverage data.
/// </summary>
public sealed class TypeCoverageDatasetJsonTests
{
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
    /// Verifies the generated release artifact through the same strict dataset reader.
    /// </summary>
    [Fact]
    public void GeneratedReleaseDatasetMatchesAuditedPopulation()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "type_coverage.json");
        using FileStream stream = File.OpenRead(path);

        TypeCoverageDataset dataset = TypeCoverageDatasetJson.Deserialize(stream);

        Assert.Equal("6.8.0", dataset.GameVersion);
        Assert.Equal(576, dataset.NormalPoolSize);
        Assert.Equal(174_348, dataset.FusionPoolSize);
        Assert.Equal(170, dataset.Profiles.Count);
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
