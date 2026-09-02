using System.Reflection;
using System.Text;
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
    /// Gets the supported fusion-evolution generator version.
    /// </summary>
    internal int FusionEvolutionGeneratorVersion { get; private init; }

    /// <summary>
    /// Gets the supported fusion-evolution rules version.
    /// </summary>
    internal int FusionEvolutionRulesVersion { get; private init; }

    /// <summary>
    /// Gets the evolution source fingerprint represented by the catalog.
    /// </summary>
    internal required string EvolutionSourceFingerprint { get; init; }

    /// <summary>
    /// Gets the evolution taxonomy fingerprint represented by the catalog.
    /// </summary>
    internal required string EvolutionTaxonomyFingerprint { get; init; }

    /// <summary>
    /// Gets the evolution method fingerprint represented by the catalog.
    /// </summary>
    internal required string EvolutionMethodFingerprint { get; init; }

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
    /// Gets the type names in catalog bit order.
    /// </summary>
    internal IReadOnlyList<string> TypeNames { get; private init; } = [];

    /// <summary>
    /// Gets the normal material definitions in numeric species order.
    /// </summary>
    internal IReadOnlyList<PlayerFusionNormalSpecies> NormalSpecies { get; private init; } = [];

    /// <summary>
    /// Gets the ordered custom-fusion result definitions.
    /// </summary>
    internal IReadOnlyList<PlayerFusionTargetSpecies> CustomFusionPool { get; private init; } = [];

    /// <summary>
    /// Gets the ordered conceptual normal-species evolution branches.
    /// </summary>
    internal IReadOnlyList<PlayerFusionEvolutionBranch> EvolutionBranches { get; private init; } = [];

    /// <summary>
    /// Gets the seed used by the generated cross-runtime verification cases.
    /// </summary>
    internal long VerificationSeed { get; private init; }

    /// <summary>
    /// Gets the generated Ruby-reference mapping cases.
    /// </summary>
    internal IReadOnlyList<PlayerFusionReferenceMapping> VerificationMappings { get; private init; } = [];

    /// <summary>
    /// Gets the generated Ruby-reference fusion-evolution assignments.
    /// </summary>
    internal IReadOnlyList<PlayerFusionEvolutionReferenceMapping> EvolutionVerificationMappings { get; private init; } = [];

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
                values, row[8].GetUInt64(), row[9].GetInt32() - 1, row[10].GetInt32() - 1,
                (PlayerFusionEvolutionRole)row[11].GetInt32(), row[12].GetInt32()));
        }

        List<PlayerFusionEvolutionBranch> evolutionBranches = [];
        foreach (JsonElement row in root.GetProperty("evolution_branches").EnumerateArray())
        {
            evolutionBranches.Add(new PlayerFusionEvolutionBranch(
                row[0].GetInt32(), row[1].GetString() ?? throw new InvalidDataException("An evolution branch identity is missing."),
                row[2].GetInt32(), (PlayerFusionEvolutionRole)row[3].GetInt32(), row[4].GetUInt64(),
                [.. row[5].EnumerateArray().Select(value => value.GetString() ?? string.Empty)]));
        }

        List<PlayerFusionTargetSpecies> fusionPool = [];
        foreach (JsonElement row in root.GetProperty("custom_fusion_pool").EnumerateArray())
            fusionPool.Add(new PlayerFusionTargetSpecies(row[0].GetInt32(), row[1].GetUInt64()));

        List<PlayerFusionReferenceMapping> verificationMappings = [];
        foreach (JsonElement row in root.GetProperty("verification_mappings").EnumerateArray())
            verificationMappings.Add(new PlayerFusionReferenceMapping(row[0].GetInt32(), row[1].GetInt32(), row[2].GetInt32(), row[3].GetInt32()));

        List<PlayerFusionEvolutionReferenceMapping> evolutionVerificationMappings = [];
        foreach (JsonElement row in root.GetProperty("evolution_verification_mappings").EnumerateArray())
        {
            List<PlayerFusionEvolutionReferenceBranch> branches = [];
            foreach (JsonElement branch in row[2].EnumerateArray())
                branches.Add(new PlayerFusionEvolutionReferenceBranch(branch[0].GetInt32(), branch[1].GetString() ?? throw new InvalidDataException("An evolution reference branch identity is missing."), branch[2].GetInt32()));

            evolutionVerificationMappings.Add(new PlayerFusionEvolutionReferenceMapping(row[0].GetInt64(), row[1].GetInt32(), branches));
        }

        string[] excludedAuthors = [.. root.GetProperty("excluded_sprite_authors").EnumerateArray().Select(value => value.GetString() ?? string.Empty)];
        string[] typeNames = [.. root.GetProperty("type_names").EnumerateArray().Select(value => value.GetString() ?? string.Empty)];
        PlayerFusionMappingWorkerCatalog result = new()
        {
            SchemaVersion = root.GetProperty("schema_version").GetInt32(),
            NormalSpeciesCount = normalSpeciesCount,
            BaseStatSourceFingerprint = root.GetProperty("base_stat_source_fingerprint").GetString() ?? string.Empty,
            PlayerFusionGeneratorVersion = root.GetProperty("player_fusion_generator_version").GetInt32(),
            FusionEvolutionGeneratorVersion = root.GetProperty("evolution_generator_version").GetInt32(),
            FusionEvolutionRulesVersion = root.GetProperty("evolution_rules_version").GetInt32(),
            EvolutionSourceFingerprint = root.GetProperty("evolution_source_fingerprint").GetString() ?? string.Empty,
            EvolutionTaxonomyFingerprint = root.GetProperty("evolution_taxonomy_fingerprint").GetString() ?? string.Empty,
            EvolutionMethodFingerprint = root.GetProperty("evolution_method_fingerprint").GetString() ?? string.Empty,
            CustomFusionPoolVersion = root.GetProperty("custom_fusion_pool_version").GetInt32(),
            CustomFusionPoolFingerprint = root.GetProperty("custom_fusion_pool_fingerprint").GetString() ?? string.Empty,
            ExcludedSpriteAuthors = excludedAuthors,
            TypeNames = typeNames,
            NormalSpecies = normalSpecies,
            CustomFusionPool = fusionPool,
            EvolutionBranches = evolutionBranches,
            VerificationSeed = root.GetProperty("verification_seed").GetInt64(),
            VerificationMappings = verificationMappings,
            EvolutionVerificationMappings = evolutionVerificationMappings
        };

        result.Validate();
        return result;
    }

    /// <summary>
    /// Replaces only the custom-fusion target pool with membership supplied
    /// by the game's pinned generation-profile index. All release-stable normal and
    /// evolution catalogs remain embedded and independently validated.
    /// </summary>
    internal PlayerFusionMappingWorkerCatalog WithRuntimeFusionPool(int version, int expectedSize, string expectedFingerprint, string packedMembership)
    {
        byte[] membership;
        try
        {
            membership = Convert.FromBase64String(packedMembership);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The runtime custom-fusion membership is not valid base64.", exception);
        }

        int bitCount = checked(NormalSpeciesCount * NormalSpeciesCount);
        int expectedBytes = checked((bitCount + 7) / 8);
        if (membership.Length != expectedBytes)
            throw new InvalidDataException("The runtime custom-fusion membership has an invalid size.");

        PlayerFusionNormalSpecies[] normal = [.. NormalSpecies.OrderBy(species => species.Id)];
        string[] typeNames = [.. TypeNames];
        int normalType = Array.IndexOf(typeNames, "NORMAL");
        int flyingType = Array.IndexOf(typeNames, "FLYING");
        if (normalType < 0 || flyingType < 0)
            throw new InvalidDataException("The worker type catalog is missing Normal or Flying.");

        List<PlayerFusionTargetSpecies> fusionPool = [];
        ulong fingerprint = 14_695_981_039_346_656_037;
        const ulong fnvPrime = 1_099_511_628_211;
        for (int position = 0; position < bitCount; position++)
        {
            if ((membership[position >> 3] & (1 << (position & 7))) == 0)
                continue;

            int bodyId = position / NormalSpeciesCount + 1;
            int headId = position % NormalSpeciesCount + 1;
            int packed = checked(bodyId << 10 | headId);
            ulong types = FusionTypeMask(normal[bodyId - 1], normal[headId - 1], normalType, flyingType);
            fusionPool.Add(new PlayerFusionTargetSpecies(packed, types));
            foreach (byte value in Encoding.UTF8.GetBytes($"B{bodyId}H{headId}"))
            {
                fingerprint ^= value;
                fingerprint = unchecked(fingerprint * fnvPrime);
            }

            fingerprint = unchecked(fingerprint * fnvPrime);
        }

        string actualFingerprint = fingerprint.ToString("x16");
        if (fusionPool.Count != expectedSize || !string.Equals(actualFingerprint, expectedFingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("The runtime custom-fusion membership does not match its pinned metadata.");

        PlayerFusionMappingWorkerCatalog result = new()
        {
            SchemaVersion = SchemaVersion,
            NormalSpeciesCount = NormalSpeciesCount,
            BaseStatSourceFingerprint = BaseStatSourceFingerprint,
            PlayerFusionGeneratorVersion = PlayerFusionGeneratorVersion,
            FusionEvolutionGeneratorVersion = FusionEvolutionGeneratorVersion,
            FusionEvolutionRulesVersion = FusionEvolutionRulesVersion,
            EvolutionSourceFingerprint = EvolutionSourceFingerprint,
            EvolutionTaxonomyFingerprint = EvolutionTaxonomyFingerprint,
            EvolutionMethodFingerprint = EvolutionMethodFingerprint,
            CustomFusionPoolVersion = version,
            CustomFusionPoolFingerprint = expectedFingerprint,
            ExcludedSpriteAuthors = ExcludedSpriteAuthors,
            TypeNames = TypeNames,
            NormalSpecies = NormalSpecies,
            CustomFusionPool = fusionPool,
            EvolutionBranches = EvolutionBranches,
            VerificationSeed = VerificationSeed,
            VerificationMappings = VerificationMappings,
            EvolutionVerificationMappings = EvolutionVerificationMappings
        };

        result.Validate();
        return result;
    }

    /// <summary>
    /// Calculates Infinite Fusion's ordered displayed type mask for one fusion.
    /// </summary>
    /// <param name="body">The normal species used as the fusion body.</param>
    /// <param name="head">The normal species used as the fusion head.</param>
    /// <param name="normalType">The Normal type index in the worker type catalog.</param>
    /// <param name="flyingType">The Flying type index in the worker type catalog.</param>
    /// <returns>The bit mask containing the fusion's effective displayed types.</returns>
    private static ulong FusionTypeMask(PlayerFusionNormalSpecies body, PlayerFusionNormalSpecies head, int normalType, int flyingType)
    {
        int first = head.PrimaryType == normalType && head.SecondaryType == flyingType
            ? head.SecondaryType
            : head.PrimaryType;

        int second = body.SecondaryType == first ? body.PrimaryType : body.SecondaryType;
        ulong result = 1UL << first;
        if (second >= 0)
            result |= 1UL << second;

        return result;
    }

    /// <summary>
    /// Rejects a stale or malformed generated worker catalog.
    /// </summary>
    private void Validate()
    {
        if (SchemaVersion != 3)
            throw new InvalidDataException($"Player-fusion worker catalog schema {SchemaVersion} is unsupported.");

        if (NormalSpeciesCount <= 0 || NormalSpecies.Count != NormalSpeciesCount)
            throw new InvalidDataException("The player-fusion worker normal-species count is inconsistent.");

        if (TypeNames.Count == 0 || TypeNames.Count > 64)
            throw new InvalidDataException("The player-fusion worker type catalog is malformed.");

        if (CustomFusionPool.Count == 0 || CustomFusionPool.Count % 2 != 0)
            throw new InvalidDataException("The player-fusion worker custom pool cannot form reverse pairs.");

        if (EvolutionBranches.Count == 0 || EvolutionBranches.Any(branch => branch.ItemOptions.Count == 0))
            throw new InvalidDataException("The player-fusion worker evolution branch catalog is empty.");

        if (EvolutionVerificationMappings.Count == 0)
            throw new InvalidDataException("The player-fusion worker evolution verification catalog is empty.");

        if (NormalSpecies.Any(species => !Enum.IsDefined(species.Role) || species.FamilyId < 0))
            throw new InvalidDataException("The player-fusion worker evolution taxonomy is malformed.");

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
/// <param name="PrimaryType">The zero-based primary type code.</param>
/// <param name="SecondaryType">The zero-based secondary type code, or negative one when absent.</param>
/// <param name="Role">The normal evolution taxonomy role.</param>
/// <param name="FamilyId">The stable normal evolution family identifier.</param>
internal sealed record PlayerFusionNormalSpecies(int Id, string Identity, IReadOnlyList<int> Stats, ulong TypeMask, int PrimaryType, int SecondaryType, PlayerFusionEvolutionRole Role, int FamilyId);

/// <summary>
/// Stores one conceptual normal-species evolution branch used by complete-fusion evolution generation.
/// </summary>
/// <param name="SourceId">The evolving normal component identifier.</param>
/// <param name="Identity">The stable branch identity used by deterministic hashing.</param>
/// <param name="DestinationId">The native destination component identifier used for the reference BST.</param>
/// <param name="DestinationRole">The native destination taxonomy role.</param>
/// <param name="RequiredTypeMask">The target types accepted by this branch.</param>
/// <param name="ItemOptions">The alternative consumed item identifiers, with an empty value for item-free methods.</param>
internal sealed record PlayerFusionEvolutionBranch(int SourceId, string Identity, int DestinationId, PlayerFusionEvolutionRole DestinationRole, ulong RequiredTypeMask, IReadOnlyList<string> ItemOptions);

/// <summary>
/// Identifies one normal species' position in its native evolution family.
/// </summary>
internal enum PlayerFusionEvolutionRole
{
    /// <summary>No native incoming or outgoing branch.</summary>
    Standalone = 0,

    /// <summary>Has outgoing branches and no incoming branch.</summary>
    FirstStage = 1,

    /// <summary>Has both incoming and outgoing branches.</summary>
    Intermediate = 2,

    /// <summary>Has incoming branches and no outgoing branch.</summary>
    Final = 3
}

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

/// <summary>
/// Stores one Ruby-generated fusion-evolution assignment reference.
/// </summary>
/// <param name="Seed">The run seed.</param>
/// <param name="PackedSourceComponents">The body and head identifiers packed into ten bits each.</param>
/// <param name="Branches">The exact assigned conceptual branches.</param>
internal sealed record PlayerFusionEvolutionReferenceMapping(long Seed, int PackedSourceComponents, IReadOnlyList<PlayerFusionEvolutionReferenceBranch> Branches);

/// <summary>
/// Stores one assigned branch in a Ruby-generated fusion-evolution reference.
/// </summary>
/// <param name="Side">Zero for body and one for head.</param>
/// <param name="ComponentBranchIdentity">The conceptual normal branch identity.</param>
/// <param name="TargetId">The numeric custom-fusion target identifier.</param>
internal sealed record PlayerFusionEvolutionReferenceBranch(int Side, string ComponentBranchIdentity, int TargetId);
