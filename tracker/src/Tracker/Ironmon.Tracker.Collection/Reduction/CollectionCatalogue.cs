using System.Reflection;
using System.Text.Json;

namespace Ironmon.Tracker.Collection.Reduction;

/// <summary>
/// Loads the versioned collection schema independently from any consumer definitions.
/// </summary>
public sealed class CollectionCatalogue
{
    /// <summary>
    /// Names the embedded generic collection catalogue.
    /// </summary>
    public const string ResourceName = "Ironmon.Tracker.Collection.GameplayCatalogue.json";
    private const string _familiesProperty = "families";
    private const string _eventsProperty = "events";
    private const string _directGroupsProperty = "direct_groups";
    private const string _relationshipsProperty = "relationships";
    private const string _idProperty = "id";
    private const string _keyProperty = "key";
    private const string _eventProperty = "owner_event";
    private const string _storageProperty = "storage_kind";
    private const string _scopesProperty = "scopes";
    private const string _measuresProperty = "measures";
    private const string _kindProperty = "kind";
    private const string _mergeProperty = "merge_operator";
    private const string _unitProperty = "unit";
    private readonly Dictionary<string, CollectionFamilyDefinition[]> _byEvent;

    /// <summary>
    /// Initializes a fully validated generic catalogue.
    /// </summary>
    /// <param name="root">Root metadata object.</param>
    private CollectionCatalogue(JsonElement root)
    {
        Definition = root.Clone();
        Families = [.. root.GetProperty(_familiesProperty).EnumerateArray().Select(ReadFamily)];
        Events = CloneArray(root, _eventsProperty);
        DirectGroups = CloneArray(root, _directGroupsProperty);
        Relationships = CloneArray(root, _relationshipsProperty);
        if (Families.Select(family => family.Id).Distinct().Count() != Families.Count || Families.Select(family => family.Key).Distinct(StringComparer.Ordinal).Count() != Families.Count)
            throw new InvalidDataException("Collection families must have unique stable IDs and keys.");

        _byEvent = Families.GroupBy(family => family.OwnerEvent, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets the complete generic catalogue metadata.
    /// </summary>
    public JsonElement Definition { get; }

    /// <summary>
    /// Gets all declared collection families.
    /// </summary>
    public IReadOnlyList<CollectionFamilyDefinition> Families { get; }

    /// <summary>
    /// Gets the authoritative event field contracts.
    /// </summary>
    public IReadOnlyList<JsonElement> Events { get; }

    /// <summary>
    /// Gets directly indexed sufficient summary definitions.
    /// </summary>
    public IReadOnlyList<JsonElement> DirectGroups { get; }

    /// <summary>
    /// Gets retained causal relationship definitions.
    /// </summary>
    public IReadOnlyList<JsonElement> Relationships { get; }

    /// <summary>
    /// Loads the production embedded catalogue.
    /// </summary>
    /// <returns>A validated catalogue.</returns>
    public static CollectionCatalogue LoadDefault()
    {
        using Stream stream = typeof(CollectionCatalogue).Assembly.GetManifestResourceStream(ResourceName) ?? throw new InvalidOperationException("The collection catalogue resource is missing.");
        return Load(stream);
    }

    /// <summary>
    /// Loads generic collection metadata from a stream.
    /// </summary>
    /// <param name="stream">UTF-8 JSON stream.</param>
    /// <returns>A validated catalogue.</returns>
    public static CollectionCatalogue Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using JsonDocument document = JsonDocument.Parse(stream);
        return new CollectionCatalogue(document.RootElement);
    }

    /// <summary>
    /// Finds the declared families owned by an event.
    /// </summary>
    /// <param name="eventType">Authoritative event type.</param>
    /// <returns>All families routed from this event.</returns>
    public IReadOnlyList<CollectionFamilyDefinition> ForEvent(string eventType)
        => _byEvent.TryGetValue(eventType, out CollectionFamilyDefinition[]? families) ? families : [];

    /// <summary>
    /// Reads one typed family definition.
    /// </summary>
    /// <param name="value">JSON family definition.</param>
    /// <returns>The typed declaration.</returns>
    private static CollectionFamilyDefinition ReadFamily(JsonElement value)
    {
        CollectionMeasureDefinition[] measures = [.. value.GetProperty(_measuresProperty)
            .EnumerateArray()
            .Select(measure => new CollectionMeasureDefinition(measure.GetProperty(_idProperty).GetInt32(), measure.GetProperty(_keyProperty).GetString()!, measure.GetProperty(_kindProperty).GetString()!, measure.GetProperty(_mergeProperty).GetString()!, measure.GetProperty(_unitProperty).GetString()!))];

        return new CollectionFamilyDefinition(value.GetProperty(_idProperty).GetInt32(), value.GetProperty(_keyProperty).GetString()!, value.GetProperty(_eventProperty).GetString()!, value.GetProperty(_storageProperty).GetString()!, [.. value.GetProperty(_scopesProperty).EnumerateArray().Select(scope => scope.GetString()!)], measures, value.Clone());
    }

    /// <summary>
    /// Clones an array so metadata outlives its parser.
    /// </summary>
    /// <param name="root">Metadata root.</param>
    /// <param name="name">Array property name.</param>
    /// <returns>Independent JSON values.</returns>
    private static IReadOnlyList<JsonElement> CloneArray(JsonElement root, string name)
        => [.. root.GetProperty(name).EnumerateArray().Select(value => value.Clone())];
}
