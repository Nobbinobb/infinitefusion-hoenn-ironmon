using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Reads the displayed fusion sprite's visual head and body from game-written metadata.
/// </summary>
/// <remarks>
/// Component names and images come from the displayed sprite rather than Ironmon's fusion materials.
/// Parsed metadata is cached with a bounded entry count and remains available while disconnected.
/// </remarks>
internal static class LocalSpriteComponentsLoader
{
    private const string _cacheDirectory = "Graphics/CustomBattlers/local_sprites/IronmonTracker";
    private const string _metadataPrefix = "components-";
    private const string _metadataExtension = ".json";
    private const string _schemaProperty = "schema_version";
    private const string _spriteProperty = "sprite_path";
    private const string _nameProperty = "name";
    private const string _headProperty = "head";
    private const string _bodyProperty = "body";
    private const int _maximumMetadataLength = 16384;
    private const int _maximumCacheEntries = 2048;
    private static readonly Lock _gate = new();
    private static readonly Dictionary<string, CachedComponents> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Loads the exact displayed sprite's head and body, including when the game is offline.
    /// </summary>
    /// <param name="gameRoot">The installation owning the sprite and its presentation cache.</param>
    /// <param name="spritePath">The game-relative displayed sprite path.</param>
    /// <returns>The visual components, or <see langword="null"/> when the sprite, metadata, or either component image is unavailable or invalid.</returns>
    internal static PokemonSpriteComponents? Load(string? gameRoot, string? spritePath)
    {
        if (gameRoot is null || spritePath is null)
            return null;

        try
        {
            string? sprite = LocalSpriteLoader.ResolvePath(gameRoot, spritePath);
            if (sprite is null || !File.Exists(sprite))
                return null;

            string relative = NormalizePath(Path.GetRelativePath(Path.GetFullPath(gameRoot), sprite));
            string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(relative)));
            string path = Path.Combine(gameRoot, _cacheDirectory, _metadataPrefix + digest + _metadataExtension);
            FileInfo file = new(path);
            if (!file.Exists || file.Length <= 0 || file.Length > _maximumMetadataLength)
                return null;

            lock (_gate)
            {
                if (_cache.TryGetValue(path, out CachedComponents? cached) && cached.Modified == file.LastWriteTimeUtc && cached.Length == file.Length)
                    return ComponentsExist(gameRoot, cached.Components) ? cached.Components : null;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(_schemaProperty, out JsonElement schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out int version) || version != 1 || !root.TryGetProperty(_spriteProperty, out JsonElement storedPath) || storedPath.ValueKind != JsonValueKind.String || NormalizePath(storedPath.GetString()!) != relative)
                return null;

            PokemonSpriteComponent? head = ReadComponent(root, _headProperty, gameRoot);
            PokemonSpriteComponent? body = ReadComponent(root, _bodyProperty, gameRoot);
            if (head is null || body is null)
                return null;

            PokemonSpriteComponents components = new(head, body);
            lock (_gate)
            {
                if (_cache.Count >= _maximumCacheEntries)
                    _cache.Remove(_cache.Keys.First());

                _cache[path] = new(file.LastWriteTimeUtc, file.Length, components);
            }

            return components;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Validates one named base sprite inside the same installation.
    /// </summary>
    /// <param name="document">The game-owned metadata object.</param>
    /// <param name="property">The metadata property identifying the head or body.</param>
    /// <param name="gameRoot">The installation boundary.</param>
    /// <returns>The named component, or <see langword="null"/> when its data or installation-contained image is unavailable or invalid.</returns>
    private static PokemonSpriteComponent? ReadComponent(JsonElement document, string property, string gameRoot)
    {
        if (!document.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(_nameProperty, out JsonElement name) || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()) || !value.TryGetProperty(_spriteProperty, out JsonElement sprite) || sprite.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(sprite.GetString()))
            return null;

        string? path = LocalSpriteLoader.ResolvePath(gameRoot, sprite.GetString()!);
        return path is not null && File.Exists(path) ? new(name.GetString()!, sprite.GetString()!) : null;
    }

    /// <summary>
    /// Checks whether both images referenced by cached component metadata still exist.
    /// </summary>
    /// <param name="gameRoot">The owning installation directory.</param>
    /// <param name="components">The previously validated visual components.</param>
    /// <returns><see langword="true"/> when both component image files remain available; otherwise, <see langword="false"/>.</returns>
    private static bool ComponentsExist(string gameRoot, PokemonSpriteComponents components)
        => File.Exists(LocalSpriteLoader.ResolvePath(gameRoot, components.Head.SpritePath)) && File.Exists(LocalSpriteLoader.ResolvePath(gameRoot, components.Body.SpritePath));

    /// <summary>
    /// Matches the game's slash and ASCII-case normalization for presentation cache keys.
    /// </summary>
    /// <param name="path">The relative sprite path.</param>
    /// <returns>The stable cross-language cache key.</returns>
    private static string NormalizePath(string path)
        => string.Concat(path.Replace('\\', '/').Select(character => char.IsAsciiLetterUpper(character) ? (char)(character + 32) : character));

    /// <summary>
    /// Stores parsed component metadata with its source file's modification signature.
    /// </summary>
    /// <remarks>
    /// Initializes the file signature and validated component pair used by the bounded metadata cache.
    /// </remarks>
    /// <param name="Modified">The metadata file's last modification timestamp in UTC.</param>
    /// <param name="Length">The sidecar byte length.</param>
    /// <param name="Components">The validated displayed components.</param>
    private sealed record CachedComponents(DateTime Modified, long Length, PokemonSpriteComponents Components);
}

/// <summary>
/// Describes the two normal Pokémon represented by a displayed fusion sprite.
/// </summary>
/// <remarks>
/// Initializes the normal Pokémon represented by the displayed sprite's head and body.
/// </remarks>
/// <param name="head">The displayed head Pokémon.</param>
/// <param name="body">The displayed body Pokémon.</param>
internal sealed class PokemonSpriteComponents(PokemonSpriteComponent head, PokemonSpriteComponent body)
{
    /// <summary>
    /// Gets the normal Pokémon represented by the displayed head.
    /// </summary>
    public PokemonSpriteComponent Head { get; } = head;

    /// <summary>
    /// Gets the normal Pokémon represented by the displayed body.
    /// </summary>
    public PokemonSpriteComponent Body { get; } = body;
}

/// <summary>
/// Describes a game-resolved normal Pokémon and its base sprite.
/// </summary>
/// <remarks>
/// Initializes a game-supplied display name and the corresponding local base sprite path.
/// </remarks>
/// <param name="name">The localized Pokémon name.</param>
/// <param name="spritePath">The game-relative base sprite path.</param>
internal sealed class PokemonSpriteComponent(string name, string spritePath)
{
    /// <summary>
    /// Gets the display name exported by the game.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Gets the game-relative base sprite path.
    /// </summary>
    public string SpritePath { get; } = spritePath;
}
