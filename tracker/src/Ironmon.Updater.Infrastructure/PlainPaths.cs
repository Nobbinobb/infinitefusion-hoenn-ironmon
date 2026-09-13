using Ironmon.Updater.Core;
namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Rejects redirected paths before operating on updater-owned directories.
/// </summary>
internal static class PlainPaths
{
    private const string SeparatorPair = @"\\";
    private const string Dot = ".";
    private const string Parent = "..";
    private const string Invalid = ":<>\"|?*";
    private const string Wildcard = "*";
    private const string ReservedNames = "CON|PRN|AUX|NUL|COM1|COM2|COM3|COM4|COM5|COM6|COM7|COM8|COM9|LPT1|LPT2|LPT3|LPT4|LPT5|LPT6|LPT7|LPT8|LPT9";
    private static readonly HashSet<string> _reserved = new(ReservedNames.Split('|'), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns an absolute local path with no existing reparse-point ancestor.
    /// </summary>
    /// <param name="path">The fully qualified local path to normalize and validate.</param>
    /// <returns>The normalized absolute path without a trailing directory separator, except for a volume root.</returns>
    internal static string Full(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        if (!Path.IsPathFullyQualified(path) || full.StartsWith(SeparatorPair, StringComparison.Ordinal))
            throw new IOException(UpdaterText.PlainPathsUpdaterPathsMustBeFullyQualifiedLocalPaths);

        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(UpdaterText.PlainPathsRedirectedInstallationOrCachePathsAreNotSupported);
        }

        return Path.TrimEndingDirectorySeparator(full);
    }

    /// <summary>
    /// Resolves a safe relative path beneath a fixed local root.
    /// </summary>
    /// <param name="root">The fully qualified parent directory.</param>
    /// <param name="relative">The child path, with no traversal, reserved Windows names or unsafe components.</param>
    /// <returns>The validated absolute child path.</returns>
    internal static string Child(string root, string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Any(part => part.Length == 0 || part is Dot or Parent || part.EndsWith('.') || part.EndsWith(' ') || part.Any(c => char.IsControl(c) || Invalid.Contains(c)) || _reserved.Contains(part.Split('.')[0])))
            throw new InvalidDataException(UpdaterText.PlainPathsTheArchiveContainsAnUnsafePath);

        var full = Full(Path.Combine(root, Path.Combine(parts)));
        if (!full.StartsWith(Full(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(UpdaterText.PlainPathsTheArchivePathEscapesItsDestination);

        return full;
    }

    /// <summary>
    /// Checks every entry without following directory links.
    /// </summary>
    /// <param name="root">The existing directory whose tree must contain no reparse points.</param>
    internal static void CheckTree(string root)
    {
        Full(root);
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException(UpdaterText.PlainPathsRedirectedFilesAreNotSupported);

            if ((attributes & FileAttributes.Directory) != 0)
                CheckTree(entry);
        }
    }

    /// <summary>
    /// Deletes only a checked child directory owned by the caller.
    /// </summary>
    /// <remarks>
    /// Ownership is a caller precondition. This method checks containment and reparse points, normalizes file attributes, and deletes the tree if it exists.
    /// </remarks>
    /// <param name="parent">The fully qualified parent that bounds the caller's owned directories.</param>
    /// <param name="child">The owned directory to remove; it must be a strict descendant of the parent.</param>
    internal static void DeleteOwned(string parent, string child)
    {
        var full = Full(child);
        if (!full.StartsWith(Full(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException(UpdaterText.PlainPathsRefusingToRemoveADirectoryOutsideTheOwnedParent);

        if (!Directory.Exists(full))
            return;

        CheckTree(full);
        foreach (var file in Directory.EnumerateFiles(full, Wildcard, SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);

        Directory.Delete(full, recursive: true);
    }
}
