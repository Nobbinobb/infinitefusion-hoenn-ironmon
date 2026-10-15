namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Supplies game-resolved name components and optional search inputs for one generation, session, and label context.
/// </summary>
/// <remarks>
/// Instances are initialized through their properties for protocol serialization. Fusion-only exports omit normal-species search inputs and pool eligibility.
/// </remarks>
public sealed class PokemonNameCatalogPayload
{
    /// <summary>
    /// Gets or initializes the compact name-catalog schema version.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// Gets or initializes the owning generation profile.
    /// </summary>
    public required string GenerationProfileId { get; init; }

    /// <summary>
    /// Gets or initializes the game-issued token identifying the exact names and optional eligible-pool context represented by this export.
    /// </summary>
    public required string ContextToken { get; init; }

    /// <summary>
    /// Gets or initializes the fusion-only context token shared with area presentation, or null when the export is already fusion-only.
    /// </summary>
    public string? FusionNameContextToken { get; init; }

    /// <summary>
    /// Gets or initializes the number of normal species on each fusion component axis.
    /// </summary>
    public int NormalSpeciesCount { get; init; }

    /// <summary>
    /// Gets or initializes game-resolved head-name prefixes indexed by numeric species identifier, with index zero unused.
    /// </summary>
    public IReadOnlyList<string?> Prefixes { get; init; } = [];

    /// <summary>
    /// Gets or initializes game-resolved body-name suffixes indexed by numeric species identifier, with index zero unused.
    /// </summary>
    public IReadOnlyList<string?> Suffixes { get; init; } = [];

    /// <summary>
    /// Gets or initializes the game's capitalization of each body-name suffix, indexed by numeric species identifier.
    /// </summary>
    public IReadOnlyList<string?> CapitalizedSuffixes { get; init; } = [];

    /// <summary>
    /// Gets or initializes the game's lowercase head-name prefixes, indexed by numeric species identifier.
    /// </summary>
    public IReadOnlyList<string?> LowercasePrefixes { get; init; } = [];

    /// <summary>
    /// Gets or initializes the game's lowercase head-name prefixes after removing the final character before case conversion.
    /// </summary>
    public IReadOnlyList<string?> LowercaseTrimmedPrefixes { get; init; } = [];

    /// <summary>
    /// Gets or initializes the game's lowercase body-name suffixes, indexed by numeric species identifier.
    /// </summary>
    public IReadOnlyList<string?> LowercaseSuffixes { get; init; } = [];

    /// <summary>
    /// Gets or initializes the game's lowercase capitalized body-name suffixes, indexed by numeric species identifier.
    /// </summary>
    public IReadOnlyList<string?> LowercaseCapitalizedSuffixes { get; init; } = [];

    /// <summary>
    /// Gets or initializes game-normalized lowercase normal-species names in the same order as NormalSpecies.
    /// </summary>
    public IReadOnlyList<string> NormalizedNormalNames { get; init; } = [];

    /// <summary>
    /// Gets or initializes game-normalized lowercase name-and-identity search texts in the same order as NormalSpecies.
    /// </summary>
    public IReadOnlyList<string> NormalizedNormalSearchTexts { get; init; } = [];

    /// <summary>
    /// Gets or initializes normal-species identities and display names in the original game pool order.
    /// </summary>
    public IReadOnlyList<PokemonSearchMatch> NormalSpecies { get; init; } = [];

    /// <summary>
    /// Gets or initializes the base64-encoded row-major custom-fusion eligibility bitset, or null when pool eligibility is omitted.
    /// </summary>
    public string? PackedCustomFusionPool { get; init; }
}
