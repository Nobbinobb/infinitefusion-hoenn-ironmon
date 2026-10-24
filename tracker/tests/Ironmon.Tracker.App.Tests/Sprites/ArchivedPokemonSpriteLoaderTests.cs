using Ironmon.Tracker.App.Components.Common;
using System.Text.Json;

namespace Ironmon.Tracker.App.Tests.Sprites;

/// <summary>
/// Verifies artwork backfill for old archives without trusting external paths or damaged cache metadata.
/// </summary>
public sealed class ArchivedPokemonSpriteLoaderTests
{
    private const string _directory = "ironmon-archive-sprite-tests";
    private const string _cache = "Graphics/CustomBattlers/local_sprites/IronmonTracker";
    private const string _metadata = "history-B117H339-0.json";
    private const string _species = "B117H339:0";
    private const string _cachedSprite = "Graphics/cached.png";
    private const string _recordedSprite = "Graphics/recorded.png";
    private const string _missingSprite = "Graphics/missing.png";
    private const string _outside = "../outside.png";
    private const string _invalidSpecies = "../B117H339:0";
    private const string _damagedMetadata = "{";

    /// <summary>
    /// Reuses native-materialized artwork when old configurations have no image or their old file disappeared.
    /// </summary>
    [Fact]
    public void MissingRecordedArtworkUsesPersistentCacheAndKeepsAvailableRecordedVariant()
    {
        string root = Path.Combine(Path.GetTempPath(), _directory, Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(root, _cache));
        try
        {
            File.WriteAllBytes(Path.Combine(root, _cachedSprite), [1]);
            File.WriteAllText(Path.Combine(root, _cache, _metadata), JsonSerializer.Serialize(new { sprite_path = _cachedSprite }));
            Assert.Equal(_cachedSprite, ArchivedPokemonSpriteLoader.Resolve(root, _species, null));
            Assert.Equal(_cachedSprite, ArchivedPokemonSpriteLoader.Resolve(root, _species, _missingSprite));
            File.WriteAllBytes(Path.Combine(root, _recordedSprite), [2]);
            Assert.Equal(_recordedSprite, ArchivedPokemonSpriteLoader.Resolve(root, _species, _recordedSprite));
            Assert.Null(ArchivedPokemonSpriteLoader.Resolve(root, _invalidSpecies, null));
            File.WriteAllText(Path.Combine(root, _cache, _metadata), JsonSerializer.Serialize(new { sprite_path = _outside }));
            Assert.Null(ArchivedPokemonSpriteLoader.Resolve(root, _species, _outside));
            File.WriteAllText(Path.Combine(root, _cache, _metadata), _damagedMetadata);
            Assert.Null(ArchivedPokemonSpriteLoader.Resolve(root, _species, null));
            Assert.Null(ArchivedPokemonSpriteLoader.Resolve(null, _species, _recordedSprite));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
