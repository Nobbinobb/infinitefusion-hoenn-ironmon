using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Reuses recorded artwork or the game's persistent species-artwork cache for older archives.
/// </summary>
internal static partial class ArchivedPokemonSpriteLoader
{
    private const string _cacheDirectory = "Graphics/CustomBattlers/local_sprites/IronmonTracker";
    private const string _filePrefix = "history-";
    private const string _fileSuffix = ".json";
    private const string _spriteProperty = "sprite_path";
    private static readonly Regex _identifier = Identifier();

    [GeneratedRegex(@"\A[A-Z0-9_]+:[0-9]{1,3}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();

    /// <summary>
    /// Resolves readable recorded artwork before falling back to previously materialized species artwork.
    /// </summary>
    /// <param name="gameRoot">The local game installation, or null while unavailable.</param>
    /// <param name="speciesId">The recorded canonical species and form identifier.</param>
    /// <param name="recordedPath">The originally captured image path, when available.</param>
    /// <returns>A readable game-relative image path, or null when materialization is needed.</returns>
    internal static string? Resolve(string? gameRoot, string speciesId, string? recordedPath)
    {
        if (gameRoot is null)
            return null;

        try
        {
            if (recordedPath is not null && File.Exists(LocalSpriteLoader.ResolvePath(gameRoot, recordedPath)))
                return recordedPath;

            if (!_identifier.IsMatch(speciesId))
                return null;

            string metadata = Path.Combine(gameRoot, _cacheDirectory, string.Concat(_filePrefix, speciesId.Replace(':', '-'), _fileSuffix));
            if (!File.Exists(metadata))
                return null;

            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(metadata));
            string? sprite = document.RootElement.GetProperty(_spriteProperty).GetString();
            return sprite is not null && File.Exists(LocalSpriteLoader.ResolvePath(gameRoot, sprite)) ? sprite : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
