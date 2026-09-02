using System.Text.Json;

namespace Ironmon.Tracker.Connection.Sprites;

/// <summary>
/// Persists the official response metadata required for conditional custom-sprite sheet synchronization.
/// </summary>
internal static class CustomSpriteSheetSyncStore
{
    internal const string RelativePath = "Data/Ironmon/custom_sprite_sheet_sync.json";
    private static readonly Lock _sync = new();

    /// <summary>
    /// Reads the last successful synchronization metadata for one installation.
    /// </summary>
    /// <param name="gameRoot">The validated Infinite Fusion installation directory.</param>
    /// <returns>The metadata keyed by endpoint-relative sheet path.</returns>
    internal static Dictionary<string, CustomSpriteSheetSyncMetadata> Read(string gameRoot)
    {
        lock (_sync)
        {
            string path = Path.Combine(gameRoot, RelativePath);
            if (!File.Exists(path))
                return new Dictionary<string, CustomSpriteSheetSyncMetadata>(StringComparer.OrdinalIgnoreCase);

            try
            {
                Dictionary<string, CustomSpriteSheetSyncMetadata>? entries = JsonSerializer.Deserialize<Dictionary<string, CustomSpriteSheetSyncMetadata>>(File.ReadAllText(path));
                return entries is null
                    ? new Dictionary<string, CustomSpriteSheetSyncMetadata>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, CustomSpriteSheetSyncMetadata>(entries, StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException)
            {
                return new Dictionary<string, CustomSpriteSheetSyncMetadata>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// Atomically writes the successful synchronization metadata for one installation.
    /// </summary>
    /// <param name="gameRoot">The validated Infinite Fusion installation directory.</param>
    /// <param name="entries">The metadata keyed by endpoint-relative sheet path.</param>
    internal static void Write(string gameRoot, IReadOnlyDictionary<string, CustomSpriteSheetSyncMetadata> entries)
    {
        lock (_sync)
        {
            SortedDictionary<string, CustomSpriteSheetSyncMetadata> ordered = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string relativePath, CustomSpriteSheetSyncMetadata metadata) in entries)
                ordered[relativePath] = metadata;

            TrackerAtomicFileWriter.WriteAllText(Path.Combine(gameRoot, RelativePath), JsonSerializer.Serialize(ordered));
        }
    }
}

/// <summary>
/// Describes one locally verified response from the official custom-sprite sheet endpoint.
/// </summary>
/// <remarks>Initializes immutable synchronization metadata.</remarks>
/// <param name="EntityTag">The server entity tag used for exact conditional requests.</param>
/// <param name="LastModifiedUtc">The server modification time retained as a conditional-request fallback.</param>
/// <param name="ContentLength">The verified local sheet length.</param>
/// <param name="LocalLastWriteUtc">The local modification time observed after the successful synchronization.</param>
internal sealed record CustomSpriteSheetSyncMetadata(string? EntityTag, DateTimeOffset? LastModifiedUtc, long ContentLength, DateTimeOffset LocalLastWriteUtc);
