using System.Text.RegularExpressions;
using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Defines fixed release endpoints, stable version parsing and canonical asset identities.
/// </summary>
public static partial class ReleaseProtocol
{
    /// <summary>
    /// Identifies the only public Ironmon release repository.
    /// </summary>
    public const string Repository = "Nobbinobb/infinitefusion-hoenn-ironmon";
    /// <summary>
    /// Identifies the stable release API endpoint.
    /// </summary>
    public const string LatestReleaseUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
    /// <summary>
    /// Identifies the self-contained package flavor.
    /// </summary>
    public const string SelfContained = "self-contained";
    /// <summary>
    /// Identifies the package flavor requiring installed .NET frameworks.
    /// </summary>
    public const string RuntimeRequired = "runtime-required";
    internal const string ManifestName = "update-manifest.json";
    internal const string SignatureName = "update-manifest.sig.json";
    internal const string Stable = "stable";
    internal const string ReleaseDocument = "release";
    internal const string SignatureDocument = "detached-signature";
    internal const string InventoryDocument = "file-inventory";
    internal const string Algorithm = "ecdsa-p256-sha256-p1363";
    internal const string IronmonScope = "ironmon";
    internal const string GameScope = "game";
    internal const string InventoryRole = "ironmon-files";
    internal const string GameInventoryRole = "game-files";
    internal const string LegacyRole = "legacy-ironmon-files";
    internal const string UpdaterRole = "updater";
    internal const string SetupRole = "setup";
    internal const string NotesRole = "release-notes";
    internal const string TrackerRolePrefix = "tracker-";
    internal const string ReplacePolicy = "replace";
    internal const string RetainPolicy = "retain";
    internal const string GameRepository = "https://github.com/infinitefusion/infinitefusion-hoenn-public.git";
    internal const string GameBranch = "releases";
    internal const string PreserveRuns = "preserve-on-supported-combination";
    internal const string FinishRun = "finish-run-before-game-update";
    internal const string ReleaseBaseUrl = "https://github.com/" + Repository + "/releases/";
    internal const string DownloadSegment = "download/v";
    internal const string EngineVersion = "1.0.0";
    private const string StablePattern = "\\A(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)\\z";
    private const string ShaPattern = "\\A[0-9a-fA-F]{64}\\z";
    private const string CommitPattern = "\\A[0-9a-f]{40}\\z";
    private const string AssetPattern = "\\A[A-Za-z0-9][A-Za-z0-9._-]{0,150}\\z";
    private const string HelperPattern = "\\AIronmon-Updater-v([0-9]+\\.[0-9]+\\.[0-9]+)\\.zip\\z";

    /// <summary>
    /// Parses exactly three nonnegative stable numeric version components without lexical ordering or prerelease coercion.
    /// </summary>
    /// <param name="value">The canonical stable version.</param>
    /// <returns>The numeric comparable version.</returns>
    public static Version ParseVersion(string value)
    {
        if (value is null || value.Length > 32 || !StableRegex().IsMatch(value) || !Version.TryParse(value, out var version))
            throw new InvalidDataException(UpdaterText.ReleaseProtocolTheReleaseVersionIsNotASupportedStableNumeric);

        return version;
    }

    /// <summary>
    /// Validates and derives the immutable helper directory version from its signed asset name.
    /// </summary>
    /// <param name="name">The signed helper archive name.</param>
    /// <returns>The canonical helper version.</returns>
    internal static string HelperVersion(string name)
    {
        var match = HelperRegex().Match(name);
        if (!match.Success)
            throw new InvalidDataException(UpdaterText.ReleaseProtocolTheHelperAssetNameDoesNotDeclareASupported);

        ParseVersion(match.Groups[1].Value);
        return match.Groups[1].Value;
    }

    /// <summary>
    /// Requires a flat safe asset basename rather than a filesystem path.
    /// </summary>
    /// <param name="name">The signed asset name.</param>
    internal static void ValidateAssetName(string name)
    {
        if (name is null || !AssetRegex().IsMatch(name))
            throw new InvalidDataException(UpdaterText.ReleaseProtocolTheReleaseAssetNameIsUnsafe);
    }

    /// <summary>
    /// Requires a complete lowercase Git object identity.
    /// </summary>
    /// <param name="commit">The approved commit.</param>
    internal static void ValidateCommit(string commit)
    {
        if (commit is null || !CommitRegex().IsMatch(commit))
            throw new InvalidDataException(UpdaterText.ReleaseProtocolTheReleaseGameCommitIsInvalid);
    }

    /// <summary>
    /// Converts bounded wire file fingerprints to the planner's canonical representation.
    /// </summary>
    /// <param name="bytes">The exact file length.</param>
    /// <param name="sha256">The hexadecimal digest.</param>
    /// <returns>The canonical fingerprint.</returns>
    internal static GameFileContent Content(long bytes, string sha256)
    {
        if (bytes < 0 || bytes > 8L * 1024 * 1024 * 1024 || sha256 is null || !ShaRegex().IsMatch(sha256))
            throw new InvalidDataException(UpdaterText.ReleaseProtocolTheReleaseContainsAnInvalidContentFingerprint);

        return new GameFileContent(bytes, sha256.ToUpperInvariant());
    }

    /// <summary>
    /// Builds the only accepted initial download URL for an asset of a given release.
    /// </summary>
    /// <param name="version">The stable release version.</param>
    /// <param name="name">The flat asset name.</param>
    /// <returns>The exact repository/tag-bound URL.</returns>
    internal static string AssetUrl(string version, string name)
        => ReleaseBaseUrl + DownloadSegment + version + '/' + name;

    /// <summary>
    /// Gets the generated stable version parser.
    /// </summary>
    /// <returns>The version expression.</returns>
    [GeneratedRegex(StablePattern, RegexOptions.CultureInvariant)]
    private static partial Regex StableRegex();

    /// <summary>
    /// Gets the generated SHA-256 validator.
    /// </summary>
    /// <returns>The digest expression.</returns>
    [GeneratedRegex(ShaPattern, RegexOptions.CultureInvariant)]
    private static partial Regex ShaRegex();

    /// <summary>
    /// Gets the generated Git commit validator.
    /// </summary>
    /// <returns>The commit expression.</returns>
    [GeneratedRegex(CommitPattern, RegexOptions.CultureInvariant)]
    private static partial Regex CommitRegex();

    /// <summary>
    /// Gets the generated flat asset name validator.
    /// </summary>
    /// <returns>The asset expression.</returns>
    [GeneratedRegex(AssetPattern, RegexOptions.CultureInvariant)]
    private static partial Regex AssetRegex();

    /// <summary>
    /// Gets the generated versioned helper name parser.
    /// </summary>
    /// <returns>The helper expression.</returns>
    [GeneratedRegex(HelperPattern, RegexOptions.CultureInvariant)]
    private static partial Regex HelperRegex();
}
