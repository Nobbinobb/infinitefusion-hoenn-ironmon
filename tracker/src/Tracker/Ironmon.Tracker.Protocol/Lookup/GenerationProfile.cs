using System.Security.Cryptography;
using System.Text.Json;

namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Defines the complete set of deterministic algorithm families pinned by a generation profile.
/// </summary>
public static class GenerationAlgorithmNames
{
    /// <summary>
    /// Gets the deterministic hash-contract family name.
    /// </summary>
    public const string HashContract = "hash_contract";

    /// <summary>
    /// Gets the custom-fusion eligibility family name.
    /// </summary>
    public const string CustomFusionEligibility = "custom_fusion_eligibility";

    /// <summary>
    /// Gets the species-mapping family name.
    /// </summary>
    public const string SpeciesMapping = "species_mapping";

    /// <summary>
    /// Gets the ability-assignment family name.
    /// </summary>
    public const string AbilityAssignment = "ability_assignment";

    /// <summary>
    /// Gets the base-stat generation family name.
    /// </summary>
    public const string BaseStats = "base_stats";

    /// <summary>
    /// Gets the move-access generation family name.
    /// </summary>
    public const string MoveAccess = "move_access";

    /// <summary>
    /// Gets the item-slot generation family name.
    /// </summary>
    public const string ItemSlots = "item_slots";

    /// <summary>
    /// Gets the normal-evolution generation family name.
    /// </summary>
    public const string NormalEvolution = "normal_evolution";

    /// <summary>
    /// Gets the fusion-evolution generation family name.
    /// </summary>
    public const string FusionEvolution = "fusion_evolution";

    /// <summary>
    /// Gets the player fusion and reversal family name.
    /// </summary>
    public const string PlayerFusionReversal = "player_fusion_reversal";

    /// <summary>
    /// Gets the gym-party expansion family name.
    /// </summary>
    public const string GymPartyExpansion = "gym_party_expansion";

    /// <summary>
    /// Gets the caught-fusion component family name.
    /// </summary>
    public const string CaughtFusionComponent = "caught_fusion_component";

    /// <summary>
    /// Gets the progression-support Pokémon family name.
    /// </summary>
    public const string ProgressionSupportPokemon = "progression_support_pokemon";

    /// <summary>
    /// Gets every required family name in ordinal order.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        AbilityAssignment,
        BaseStats,
        CaughtFusionComponent,
        CustomFusionEligibility,
        FusionEvolution,
        GymPartyExpansion,
        HashContract,
        ItemSlots,
        MoveAccess,
        NormalEvolution,
        PlayerFusionReversal,
        ProgressionSupportPokemon,
        SpeciesMapping
    ];
}

/// <summary>
/// Identifies one immutable implementation of a deterministic algorithm family.
/// </summary>
public sealed class GenerationAlgorithmPayload
{
    /// <summary>
    /// Initializes an empty algorithm descriptor for protocol serialization.
    /// </summary>
    public GenerationAlgorithmPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the stable algorithm-family name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets or initializes the family-specific implementation version.
    /// </summary>
    public int Version { get; init; }
}

/// <summary>
/// Identifies one immutable data component used by deterministic generation.
/// </summary>
public sealed class GenerationComponentPayload
{
    /// <summary>
    /// Initializes an empty component descriptor for protocol serialization.
    /// </summary>
    public GenerationComponentPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the stable component name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets or initializes the component's independently versioned storage schema.
    /// </summary>
    public int SchemaVersion { get; init; }

    /// <summary>
    /// Gets or initializes the lowercase SHA-256 digest of the exact component bytes.
    /// </summary>
    public required string Sha256 { get; init; }

    /// <summary>
    /// Gets or initializes the exact component byte length.
    /// </summary>
    public long ByteLength { get; init; }
}

/// <summary>
/// Describes the immutable algorithms and data snapshot needed to generate or inspect one run.
/// </summary>
public sealed class GenerationProfilePayload
{
    /// <summary>
    /// Initializes an empty generation profile for protocol serialization.
    /// </summary>
    public GenerationProfilePayload()
    {
    }

    /// <summary>
    /// Gets or initializes the generation-profile contract schema.
    /// </summary>
    public int SchemaVersion { get; init; } = GenerationProfileFingerprint.SchemaVersion;

    /// <summary>
    /// Gets or initializes every deterministic algorithm descriptor.
    /// </summary>
    public IReadOnlyList<GenerationAlgorithmPayload> Algorithms { get; init; } = [];

    /// <summary>
    /// Gets or initializes every immutable data-component descriptor.
    /// </summary>
    public IReadOnlyList<GenerationComponentPayload> Components { get; init; } = [];
}

/// <summary>
/// Validates, canonicalizes, and fingerprints immutable generation profiles.
/// </summary>
public static class GenerationProfileFingerprint
{
    private const string LowerHexCharacters = "0123456789abcdef";

    /// <summary>
    /// Gets the only generation-profile schema currently supported.
    /// </summary>
    public const int SchemaVersion = 1;

    /// <summary>
    /// Creates the lowercase SHA-256 identity of a validated generation profile.
    /// </summary>
    /// <param name="profile">The profile to validate and fingerprint.</param>
    /// <returns>The lowercase SHA-256 profile identity.</returns>
    public static string Create(GenerationProfilePayload profile)
    {
        byte[] canonicalJson = CreateCanonicalJson(profile);
        return Convert.ToHexString(SHA256.HashData(canonicalJson)).ToLowerInvariant();
    }

    /// <summary>
    /// Creates the canonical UTF-8 JSON representation used for profile identity.
    /// </summary>
    /// <param name="profile">The profile to validate and canonicalize.</param>
    /// <returns>The canonical UTF-8 JSON bytes.</returns>
    public static byte[] CreateCanonicalJson(GenerationProfilePayload profile)
    {
        GenerationProfilePayload normalized = Normalize(profile);
        JsonElement document = JsonSerializer.SerializeToElement(normalized, TrackerJson.Options);
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
            WriteCanonical(writer, document);

        return stream.ToArray();
    }

    /// <summary>
    /// Validates a profile and returns its deterministically ordered representation.
    /// </summary>
    /// <param name="profile">The profile to validate.</param>
    /// <returns>A normalized profile.</returns>
    private static GenerationProfilePayload Normalize(GenerationProfilePayload profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.SchemaVersion != SchemaVersion)
            throw new ArgumentException($"Unsupported generation profile schema {profile.SchemaVersion}.", nameof(profile));

        if (profile.Algorithms.Count != GenerationAlgorithmNames.All.Count)
            throw new ArgumentException("The generation profile does not contain every required algorithm family.", nameof(profile));

        GenerationAlgorithmPayload[] algorithms = [.. profile.Algorithms.OrderBy(entry => entry.Name, StringComparer.Ordinal)];
        for (int index = 0; index < algorithms.Length; index++)
        {
            GenerationAlgorithmPayload algorithm = algorithms[index];
            if (!IsValidName(algorithm.Name) || algorithm.Version <= 0)
                throw new ArgumentException("The generation profile contains an invalid algorithm descriptor.", nameof(profile));

            if (!StringComparer.Ordinal.Equals(algorithm.Name, GenerationAlgorithmNames.All[index]))
                throw new ArgumentException("The generation profile does not contain exactly the required algorithm families.", nameof(profile));
        }

        if (profile.Components.Count == 0)
            throw new ArgumentException("The generation profile must contain at least one data component.", nameof(profile));

        GenerationComponentPayload[] components = [.. profile.Components.OrderBy(entry => entry.Name, StringComparer.Ordinal)];
        for (int index = 0; index < components.Length; index++)
        {
            GenerationComponentPayload component = components[index];
            if (!IsValidName(component.Name) || component.SchemaVersion <= 0 || component.ByteLength <= 0 || !IsLowerSha256(component.Sha256))
                throw new ArgumentException("The generation profile contains an invalid data-component descriptor.", nameof(profile));

            if (index > 0 && StringComparer.Ordinal.Equals(component.Name, components[index - 1].Name))
                throw new ArgumentException("Generation component names must be unique.", nameof(profile));
        }

        return new GenerationProfilePayload
        {
            SchemaVersion = SchemaVersion,
            Algorithms = algorithms,
            Components = components
        };
    }

    /// <summary>
    /// Determines whether a contract name uses the portable lowercase identifier grammar.
    /// </summary>
    /// <param name="value">The value to inspect.</param>
    /// <returns>Whether the name is valid.</returns>
    private static bool IsValidName(string? value)
    {
        if (string.IsNullOrEmpty(value) || value[0] is < 'a' or > 'z')
            return false;

        return value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');
    }

    /// <summary>
    /// Determines whether a value is an exact lowercase SHA-256 digest.
    /// </summary>
    /// <param name="value">The value to inspect.</param>
    /// <returns>Whether the digest is valid.</returns>
    private static bool IsLowerSha256(string? value)
    {
        return value is { Length: 64 } && value.All(LowerHexCharacters.Contains);
    }

    /// <summary>
    /// Writes JSON with object properties sorted ordinally at every depth.
    /// </summary>
    /// <param name="writer">The destination JSON writer.</param>
    /// <param name="element">The JSON value to write.</param>
    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if(element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (JsonProperty property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }

            writer.WriteEndObject();
            return;
        }

        if(element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (JsonElement item in element.EnumerateArray())
                WriteCanonical(writer, item);

            writer.WriteEndArray();
            return;
        }

        if(element.ValueKind == JsonValueKind.String)
        {
            writer.WriteStringValue(element.GetString());
            return;
        }

        if(element.ValueKind == JsonValueKind.Number)
        {
            writer.WriteRawValue(element.GetRawText());
            return;
        }

        if(element.ValueKind == JsonValueKind.True)
        {
            writer.WriteBooleanValue(true);
            return;
        }

        if (element.ValueKind == JsonValueKind.False)
        {
            writer.WriteBooleanValue(false);
            return;
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            writer.WriteNullValue();
            return;
        }

        throw new ArgumentException("The generation profile contains unsupported JSON data.", nameof(element));
    }
}
