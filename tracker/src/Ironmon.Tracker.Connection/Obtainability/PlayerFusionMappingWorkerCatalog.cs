using System.Reflection;
using System.Text.Json;

namespace Ironmon.Tracker.Connection.Obtainability;

/// <summary>
/// Provides the generated game-data inputs required by the parallel player-fusion mapping worker.
/// </summary>
internal sealed class PlayerFusionMappingWorkerCatalog
{
    private const string ResourceName = "Ironmon.Tracker.Connection.Resources.player_fusion_worker_catalog.json";

    /// <summary>
    /// Gets the generated catalog schema version.
    /// </summary>
    internal int SchemaVersion { get; private init; }

    /// <summary>
    /// Gets the number of normal material species represented by the catalog.
    /// </summary>
    internal int NormalSpeciesCount { get; private init; }

    /// <summary>
    /// Gets the base-stat source fingerprint represented by the catalog.
    /// </summary>
    internal required string BaseStatSourceFingerprint { get; init; }

    /// <summary>
    /// Gets the supported player-fusion generator version.
    /// </summary>
    internal int PlayerFusionGeneratorVersion { get; private init; }

    /// <summary>
    /// Gets the custom-fusion pool schema version.
    /// </summary>
    internal int CustomFusionPoolVersion { get; private init; }

    /// <summary>
    /// Gets the custom-fusion pool fingerprint represented by the catalog.
    /// </summary>
    internal required string CustomFusionPoolFingerprint { get; init; }

    /// <summary>
    /// Gets the excluded sprite-author identifiers recorded by generation.
    /// </summary>
    internal IReadOnlyList<string> ExcludedSpriteAuthors { get; private init; } = [];

    /// <summary>
    /// Gets the normal material definitions in numeric species order.
    /// </summary>
    internal IReadOnlyList<PlayerFusionNormalSpecies> NormalSpecies { get; private init; } = [];

    /// <summary>
    /// Gets the ordered custom-fusion result definitions.
    /// </summary>
    internal IReadOnlyList<PlayerFusionTargetSpecies> CustomFusionPool { get; private init; } = [];

    /// <summary>
    /// Gets the seed used by the generated cross-runtime verification cases.
    /// </summary>
    internal long VerificationSeed { get; private init; }

    /// <summary>
    /// Gets the generated Ruby-reference mapping cases.
    /// </summary>
    internal IReadOnlyList<PlayerFusionReferenceMapping> VerificationMappings { get; private init; } = [];

    /// <summary>
    /// Loads and validates the embedded generated worker catalog.
    /// </summary>
    /// <returns>The immutable worker catalog.</returns>
    internal static PlayerFusionMappingWorkerCatalog Load()
    {
        Assembly assembly = typeof(PlayerFusionMappingWorkerCatalog).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The player-fusion worker catalog resource is missing.");

        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;
        int normalSpeciesCount = root.GetProperty("normal_species_count").GetInt32();
        List<PlayerFusionNormalSpecies> normalSpecies = [];
        foreach (JsonElement row in root.GetProperty("normal_species").EnumerateArray())
        {
            int[] values = [.. row.EnumerateArray().Skip(2).Take(6).Select(value => value.GetInt32())];
            normalSpecies.Add(new PlayerFusionNormalSpecies(
                row[0].GetInt32(), row[1].GetString() ?? throw new InvalidDataException("A normal species identity is missing."),
                values, row[8].GetUInt64()));
        }

        List<PlayerFusionTargetSpecies> fusionPool = [];
        foreach (JsonElement row in root.GetProperty("custom_fusion_pool").EnumerateArray())
            fusionPool.Add(new PlayerFusionTargetSpecies(row[0].GetInt32(), row[1].GetUInt64()));

        List<PlayerFusionReferenceMapping> verificationMappings = [];
        foreach (JsonElement row in root.GetProperty("verification_mappings").EnumerateArray())
            verificationMappings.Add(new PlayerFusionReferenceMapping(row[0].GetInt32(), row[1].GetInt32(), row[2].GetInt32(), row[3].GetInt32()));

        string[] excludedAuthors = [.. root.GetProperty("excluded_sprite_authors").EnumerateArray().Select(value => value.GetString() ?? string.Empty)];
        PlayerFusionMappingWorkerCatalog result = new()
        {
            SchemaVersion = root.GetProperty("schema_version").GetInt32(),
            NormalSpeciesCount = normalSpeciesCount,
            BaseStatSourceFingerprint = root.GetProperty("base_stat_source_fingerprint").GetString() ?? string.Empty,
            PlayerFusionGeneratorVersion = root.GetProperty("player_fusion_generator_version").GetInt32(),
            CustomFusionPoolVersion = root.GetProperty("custom_fusion_pool_version").GetInt32(),
            CustomFusionPoolFingerprint = root.GetProperty("custom_fusion_pool_fingerprint").GetString() ?? string.Empty,
            ExcludedSpriteAuthors = excludedAuthors,
            NormalSpecies = normalSpecies,
            CustomFusionPool = fusionPool,
            VerificationSeed = root.GetProperty("verification_seed").GetInt64(),
            VerificationMappings = verificationMappings
        };

        result.Validate();
        return result;
    }

    /// <summary>
    /// Rejects a stale or malformed generated worker catalog.
    /// </summary>
    private void Validate()
    {
        if (SchemaVersion != 1)
            throw new InvalidDataException($"Player-fusion worker catalog schema {SchemaVersion} is unsupported.");

        if (NormalSpeciesCount <= 0 || NormalSpecies.Count != NormalSpeciesCount)
            throw new InvalidDataException("The player-fusion worker normal-species count is inconsistent.");

        if (CustomFusionPool.Count == 0 || CustomFusionPool.Count % 2 != 0)
            throw new InvalidDataException("The player-fusion worker custom pool cannot form reverse pairs.");

        if (!ExcludedSpriteAuthors.Contains("japeal", StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("The player-fusion worker catalog does not record the autogenerated-sprite exclusion.");
    }
}

/// <summary>
/// Stores one normal species' deterministic identity, original stats, and type mask.
/// </summary>
/// <param name="Id">The numeric normal species identifier.</param>
/// <param name="Identity">The stable species identity used by deterministic hashing.</param>
/// <param name="Stats">The six original base stats in Ironmon order.</param>
/// <param name="TypeMask">The catalog-wide type bit mask.</param>
internal sealed record PlayerFusionNormalSpecies(int Id, string Identity, IReadOnlyList<int> Stats, ulong TypeMask);

/// <summary>
/// Stores one ordered custom-fusion target's packed components and type mask.
/// </summary>
/// <param name="PackedComponents">The body and head identifiers packed into ten bits each.</param>
/// <param name="TypeMask">The catalog-wide type bit mask.</param>
internal sealed record PlayerFusionTargetSpecies(int PackedComponents, ulong TypeMask);

/// <summary>
/// Stores one Ruby-generated cross-runtime reference mapping.
/// </summary>
/// <param name="FirstMaterialId">The first normal material identifier.</param>
/// <param name="SecondMaterialId">The second normal material identifier.</param>
/// <param name="FirstResultId">The mapped first-orientation fusion identifier.</param>
/// <param name="SecondResultId">The mapped reverse-orientation fusion identifier.</param>
internal sealed record PlayerFusionReferenceMapping(int FirstMaterialId, int SecondMaterialId, int FirstResultId, int SecondResultId);
