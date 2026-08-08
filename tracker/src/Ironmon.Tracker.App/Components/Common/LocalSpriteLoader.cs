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
    /// <returns>The data source, or null when the sprite is unavailable.</returns>
    public static string? Load(string? gameRoot, string? spritePath)
    {
        if (gameRoot is null || spritePath is null)
            return null;

        try
        {
            string root = Path.GetFullPath(gameRoot);
            string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : $"{root}{Path.DirectorySeparatorChar}";
            string path = Path.GetFullPath(Path.Combine(root, spritePath));
            if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                return null;

            string extension = Path.GetExtension(path).ToLowerInvariant();
            string mediaType = extension == ".gif" ? "image/gif" : "image/png";
            return $"data:{mediaType};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
