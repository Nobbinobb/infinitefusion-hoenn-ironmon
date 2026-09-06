using System.Text.Json;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Resolves offline item presentation from the generated game catalog without changing archived statistics.
/// </summary>
public static class ArchiveItemPresentation
{
    private const string _resource = "Ironmon.Tracker.App.Resources.Catalogs.item-presentation.json";
    private const string _name = "Name";
    private const string _category = "Category";
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> _catalog = new(LoadCatalog);

    /// <summary>
    /// Combines consumption counts by stable item identifier, independently of their Bag or Held source.
    /// </summary>
    /// <param name="statistics">The recorded statistics.</param>
    /// <returns>The item usage rows, ordered by count and then name.</returns>
    public static IReadOnlyList<ArchivedItemUsage> GetUsage(RunStatisticsPayload statistics)
    {
        return [.. statistics.ItemsBySource.Values.SelectMany(source => source)
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .Select(group => CreateUsage(group.Key, group.Sum(item => (long)item.Value)))
            .OrderByDescending(item => item.Count).ThenBy(item => item.Name, StringComparer.CurrentCulture)];
    }

    /// <summary>
    /// Resolves one usage row with a stable identifier fallback for future or removed items.
    /// </summary>
    /// <param name="id">The recorded item identifier.</param>
    /// <param name="count">The combined use count.</param>
    /// <returns>The display row.</returns>
    private static ArchivedItemUsage CreateUsage(string id, long count)
    {
        _catalog.Value.TryGetValue(id, out Dictionary<string, string>? entry);
        return new ArchivedItemUsage { Name = entry?.GetValueOrDefault(_name) ?? id, Category = entry?.GetValueOrDefault(_category), Count = count };
    }

    /// <summary>
    /// Loads the bundled presentation-only item catalog.
    /// </summary>
    /// <returns>The generated names and categories indexed by stable identifier.</returns>
    private static Dictionary<string, Dictionary<string, string>> LoadCatalog()
    {
        using Stream stream = typeof(ArchiveItemPresentation).Assembly.GetManifestResourceStream(_resource) ?? throw new InvalidOperationException("The item presentation catalog is missing.");
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream) ?? throw new InvalidOperationException("The item presentation catalog is empty.");
    }
}
