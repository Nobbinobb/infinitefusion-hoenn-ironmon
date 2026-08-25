using System.Reflection;
using System.Text.Json;

namespace Ironmon.Tracker.Connection.Obtainability;

/// <summary>
/// Provides the release-generated authored acquisition and resource descriptors shared with the game.
/// </summary>
internal sealed class ObtainabilitySourceCatalog
{
    private const string NoMappingKind = "none";
    private const string ResourceName = "Ironmon.Tracker.Connection.Resources.obtainability_source_catalog.json";
    private const string WildMappingKind = "wild";

    /// <summary>
    /// Gets the semantic catalog schema version.
    /// </summary>
    internal int SchemaVersion { get; private init; }

    /// <summary>
    /// Gets the generated content fingerprint.
    /// </summary>
    internal required string Fingerprint { get; init; }

    /// <summary>
    /// Gets the authored Pokémon acquisition descriptors in deterministic source order.
    /// </summary>
    internal IReadOnlyList<ObtainabilitySourceDescriptor> Sources { get; private init; } = [];

    /// <summary>
    /// Gets the authored item-resource descriptors in deterministic source order.
    /// </summary>
    internal IReadOnlyList<ObtainabilityResourceDescriptor> Resources { get; private init; } = [];

    /// <summary>
    /// Loads and validates the embedded semantic source catalog.
    /// </summary>
    /// <returns>The immutable catalog.</returns>
    internal static ObtainabilitySourceCatalog Load()
    {
        Assembly assembly = typeof(ObtainabilitySourceCatalog).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The obtainability source catalog resource is missing.");

        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;

        List<ObtainabilitySourceDescriptor> sources = [];
        foreach (JsonElement entry in root.GetProperty("sources").EnumerateArray())
        {
            sources.Add(new ObtainabilitySourceDescriptor(
                entry.GetProperty("species_id").GetInt32(),
                entry.GetProperty("mapping_kind").GetString() ?? string.Empty,
                [.. entry.GetProperty("mapping_context").EnumerateArray().Select(ContextValue)],
                entry.GetProperty("reason").GetString() ?? string.Empty,
                entry.GetProperty("detail").GetString() ?? string.Empty,
                entry.GetProperty("caught").GetBoolean(),
                entry.TryGetProperty("source_id", out JsonElement sourceId) ? sourceId.GetString() : null));
        }

        List<ObtainabilityResourceDescriptor> resources = [];
        foreach (JsonElement entry in root.GetProperty("resources").EnumerateArray())
        {
            resources.Add(new ObtainabilityResourceDescriptor(
                [.. entry.GetProperty("item_ids").EnumerateArray().Select(item => item.GetString() ?? string.Empty)],
                entry.GetProperty("quantity").GetInt32(),
                entry.GetProperty("repeatable").GetBoolean(),
                entry.GetProperty("tm_gift").GetBoolean(),
                entry.GetProperty("slot_id").GetString() ?? string.Empty));
        }

        ObtainabilitySourceCatalog result = new()
        {
            SchemaVersion = root.GetProperty("schema_version").GetInt32(),
            Fingerprint = root.GetProperty("fingerprint").GetString() ?? string.Empty,
            Sources = sources,
            Resources = resources
        };

        result.Validate();
        return result;
    }

    /// <summary>
    /// Converts one deterministic mapping-context value to its cross-runtime text form.
    /// </summary>
    /// <param name="value">The generated JSON context value.</param>
    /// <returns>The exact text hashed by both runtimes.</returns>
    private static string ContextValue(JsonElement value)
        => value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();

    /// <summary>
    /// Rejects a stale or malformed semantic catalog.
    /// </summary>
    private void Validate()
    {
        if (SchemaVersion != 1 || Fingerprint.Length != 16)
            throw new InvalidDataException($"Obtainability source catalog schema {SchemaVersion} is unsupported.");

        if (Sources.Count == 0 || Sources.Any(source => source.SpeciesId < 1
            || source.Reason.Length == 0 || source.Detail.Length == 0
            || (source.MappingKind != NoMappingKind && source.MappingKind != WildMappingKind)))
            throw new InvalidDataException("The obtainability source catalog contains a malformed acquisition descriptor.");

        if (Resources.Count == 0 || Resources.Any(resource => resource.ItemIds.Count == 0 || resource.ItemIds.Any(string.IsNullOrWhiteSpace) || resource.Quantity < 1 || resource.SlotId.Length == 0))
            throw new InvalidDataException("The obtainability source catalog contains a malformed resource descriptor.");
    }
}

/// <summary>
/// Stores one parsed authored Pokémon acquisition before run-specific deterministic mapping.
/// </summary>
/// <param name="SpeciesId">The authored numeric species identifier.</param>
/// <param name="MappingKind">Whether the source remains literal or uses the wild generator.</param>
/// <param name="MappingContext">The stable deterministic mapping context.</param>
/// <param name="Reason">The concise witness reason.</param>
/// <param name="Detail">The human-readable witness step.</param>
/// <param name="Caught">Whether caught-fusion transformations apply.</param>
/// <param name="SourceId">The optional one-use authored source identifier.</param>
internal sealed record ObtainabilitySourceDescriptor(int SpeciesId, string MappingKind, IReadOnlyList<string> MappingContext, string Reason, string Detail, bool Caught, string? SourceId);

/// <summary>
/// Stores one parsed authored item supply before run-specific TM-gift mapping.
/// </summary>
/// <param name="ItemIds">The authored item candidates.</param>
/// <param name="Quantity">The quantity contributed by each candidate.</param>
/// <param name="Repeatable">Whether the source supplies an effectively unbounded quantity.</param>
/// <param name="TmGift">Whether TM candidates use the deterministic TM-gift channel.</param>
/// <param name="SlotId">The stable deterministic item-slot identity.</param>
internal sealed record ObtainabilityResourceDescriptor(IReadOnlyList<string> ItemIds, int Quantity, bool Repeatable, bool TmGift, string SlotId);
