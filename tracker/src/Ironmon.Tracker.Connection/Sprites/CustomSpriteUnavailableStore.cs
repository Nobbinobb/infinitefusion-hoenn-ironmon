using System.Text.Json;

namespace Ironmon.Tracker.Connection.Sprites;

/// <summary>
/// Remembers confirmed HTTP 404 resources separately from the game's sprite manifest and installed files.
/// </summary>
internal static class CustomSpriteUnavailableStore
{
    internal const string RelativePath = "Data/Ironmon/unavailable_sprite_sheets.json";
    private static readonly Lock _sync = new();

    /// <summary>
    /// Reads the unavailable absolute URLs for one installation, ignoring malformed cache contents.
    /// </summary>
    /// <param name="gameRoot">The validated installation directory.</param>
    /// <returns>The resources excluded from ordinary download plans.</returns>
    internal static HashSet<string> Read(string gameRoot)
    {
        lock (_sync)
        {
            string path = Path.Combine(gameRoot, RelativePath);
            if (!File.Exists(path))
                return new HashSet<string>(StringComparer.Ordinal);

            try
            {
                string[] urls = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? [];
                return new HashSet<string>(urls.Where(static url => !string.IsNullOrWhiteSpace(url)), StringComparer.Ordinal);
            }
            catch (JsonException)
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }
        }
    }

    /// <summary>
    /// Atomically adds or removes a resource while preserving other concurrent download outcomes.
    /// </summary>
    /// <param name="gameRoot">The validated installation directory.</param>
    /// <param name="resource">The exact requested resource, including its server endpoint.</param>
    /// <param name="unavailable">Whether the resource returned HTTP 404.</param>
    internal static void SetUnavailable(string gameRoot, Uri resource, bool unavailable)
    {
        lock (_sync)
        {
            HashSet<string> urls = Read(gameRoot);
            bool changed = unavailable ? urls.Add(resource.AbsoluteUri) : urls.Remove(resource.AbsoluteUri);
            if (changed)
                TrackerAtomicFileWriter.WriteAllText(Path.Combine(gameRoot, RelativePath), JsonSerializer.Serialize(urls.Order(StringComparer.Ordinal)));
        }
    }
}
