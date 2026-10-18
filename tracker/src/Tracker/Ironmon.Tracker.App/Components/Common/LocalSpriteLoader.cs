namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Loads validated local game sprites for display inside the tracker WebView.
/// </summary>
internal static class LocalSpriteLoader
{
    /// <summary>
    /// Loads one validated game-relative sprite as a WebView data source.
    /// </summary>
    /// <param name="gameRoot">The connected game installation directory.</param>
    /// <param name="spritePath">The game-relative sprite path.</param>
    /// <returns>The image data source, or <see langword="null"/> when the path is invalid, outside the installation, or unreadable.</returns>
    public static string? Load(string? gameRoot, string? spritePath)
    {
        if (gameRoot is null || spritePath is null)
            return null;

        try
        {
            string? path = ResolvePath(gameRoot, spritePath);
            if (path is null || !File.Exists(path))
                return null;

            string extension = Path.GetExtension(path).ToLowerInvariant();
            string mediaType = extension == LocalSpriteConstants.GifExtension ? LocalSpriteConstants.GifMediaType : LocalSpriteConstants.PngMediaType;
            return $"data:{mediaType};{LocalSpriteConstants.Base64Marker},{Convert.ToBase64String(File.ReadAllBytes(path))}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Normalizes a sprite path and checks that it is below the installation directory.
    /// </summary>
    /// <param name="gameRoot">The installation directory.</param>
    /// <param name="spritePath">The relative or installation-contained sprite path.</param>
    /// <returns>The absolute path, or <see langword="null"/> when it is not below the installation directory.</returns>
    internal static string? ResolvePath(string gameRoot, string spritePath)
    {
        string root = Path.GetFullPath(gameRoot);
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : $"{root}{Path.DirectorySeparatorChar}";
        string path = Path.GetFullPath(Path.Combine(root, spritePath));
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? path : null;
    }
}
