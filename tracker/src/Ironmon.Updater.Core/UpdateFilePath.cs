using System.Text.RegularExpressions;

namespace Ironmon.Updater.Core;

/// <summary>
/// Validates portable Windows installation paths without consulting the filesystem.
/// </summary>
internal static partial class UpdateFilePath
{
    private const string InvalidCharacters = "\\:<>\"|?*";
    private const string ReservedNames = "CON|PRN|AUX|NUL|CONIN$|CONOUT$|COM1|COM2|COM3|COM4|COM5|COM6|COM7|COM8|COM9|LPT1|LPT2|LPT3|LPT4|LPT5|LPT6|LPT7|LPT8|LPT9|COM¹|COM²|COM³|LPT¹|LPT²|LPT³";
    private const string DigestPattern = "\\A[0-9A-F]{64}\\z";
    private const int MaximumParts = 128;
    private static readonly HashSet<string> _reserved = new(ReservedNames.Split('|'), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Rejects ambiguous, rooted, traversing and reserved paths rather than normalizing them.
    /// </summary>
    /// <param name="path">The path to validate.</param>
    internal static void Validate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var parts = path.Split('/');
        if (path.Length > 32700 || parts.Length > MaximumParts || parts.Any(part => part.Length == 0 || part.Length > 255 || part.EndsWith('.') || part.EndsWith(' ') || part.Any(character => char.IsControl(character) || InvalidCharacters.Contains(character)) || _reserved.Contains(part.Split('.')[0].TrimEnd(' '))))
            throw new InvalidDataException(UpdaterText.UpdateFilePathTheFileInventoryContainsAnUnsafeWindowsPath);
    }

    /// <summary>
    /// Validates exact byte lengths and SHA-256 values from trusted inventory adapters.
    /// </summary>
    /// <param name="content">The file fingerprint to validate.</param>
    internal static void ValidateContent(GameFileContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length < 0 || content.Sha256 is null || !DigestRegex().IsMatch(content.Sha256))
            throw new InvalidDataException(UpdaterText.UpdateFilePathTheFileInventoryContainsAnInvalidFingerprint);
    }

    /// <summary>
    /// Enumerates immediate-to-outer parent directories of a validated relative path.
    /// </summary>
    /// <param name="path">The validated relative path.</param>
    /// <returns>Each ancestor, excluding the installation root.</returns>
    internal static IEnumerable<string> Parents(string path)
    {
        for (var slash = path.LastIndexOf('/'); slash >= 0; slash = path.LastIndexOf('/', slash - 1))
            yield return path[..slash];
    }

    /// <summary>
    /// Tests whether a path equals or descends from another path on Windows.
    /// </summary>
    /// <param name="path">The possible child path.</param>
    /// <param name="parent">The possible parent path.</param>
    /// <returns>Whether the paths overlap in the requested direction.</returns>
    internal static bool Within(string path, string parent)
        => path.Equals(parent, StringComparison.OrdinalIgnoreCase) || path.StartsWith(parent + '/', StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the generated expression for canonical SHA-256 fingerprints.
    /// </summary>
    /// <returns>The culture-invariant digest expression.</returns>
    [GeneratedRegex(DigestPattern, RegexOptions.CultureInvariant)]
    private static partial Regex DigestRegex();
}
