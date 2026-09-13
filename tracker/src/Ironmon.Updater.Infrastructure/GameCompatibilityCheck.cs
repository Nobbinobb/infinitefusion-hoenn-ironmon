using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Revalidates installed game code against the tracker's embedded release baseline without requiring Git.
/// </summary>
public static class GameCompatibilityCheck
{
    private const string Extensions = ".rb|.rxdata|.dat|.dll|.exe|.ini|.so";
    private const string DownloadedSettings = "Data/Scripts/DownloadedSettings.rb";
    private const string GitDirectory = ".git";
    private const string HeadPath = ".git/HEAD";
    private const string PackedPath = ".git/packed-refs";
    private const string RefPrefix = "ref: ";
    private const string HeadsPrefix = "refs/heads/";
    private const int MaximumRefsBytes = 8 * 1024 * 1024;
    private static readonly HashSet<string> _extensions = new(Extensions.Split('|'), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Checks pending recovery, Git identity and executable game data, including approved Windows text representations.
    /// </summary>
    /// <param name="root">The installed game directory.</param>
    /// <param name="baseline">The trusted release baseline embedded with the tracker.</param>
    /// <param name="cancellationToken">Cancels startup inspection without changing installed files.</param>
    /// <returns>A compatible result or a reason requiring update or recovery.</returns>
    public static async Task<GameCompatibilityResult> InspectAsync(string root, GameBaseline baseline, CancellationToken cancellationToken = default)
    {
        try
        {
            root = PlainPaths.Full(root);
            var active = PlainPaths.Child(root, InstallationLease.StateDirectory + "/" + InstallationLease.ActiveFile);
            if (Path.Exists(active))
                return new(false, UpdaterText.GameCompatibilityCheckAnUpdateIsIncompleteFinishOrRecoverItBefore);

            var head = ReadHead(root);
            if (head is not null && head != baseline.Commit)
                return new(false, UpdaterText.GameCompatibilityCheckTheGameWasChangedOutsideIronmonAMatchingIronmon);

            var files = baseline.Files.Where(file => _extensions.Contains(Path.GetExtension(file.Path)) && file.Path != DownloadedSettings).ToList();
            if (files.Count == 0)
                throw new InvalidDataException(UpdaterText.GameCompatibilityCheckTheGameCompatibilityBaselineIsEmpty);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = PlainPaths.Child(root, file.Path);
                if (!File.Exists(path))
                    return new(false, UpdaterText.GameCompatibilityCheckARequiredGameFileIsMissing);

                var content = await TransactionStorage.ContentAsync(path, cancellationToken).ConfigureAwait(false);
                if (content != file.Canonical && content != file.WindowsText)
                    return new(false, UpdaterText.GameCompatibilityCheckInstalledGameCodeOrDataDiffersFromThisIronmon);
            }

            if (Path.Exists(active) || ReadHead(root) != head)
                return new(false, UpdaterText.GameCompatibilityCheckTheGameChangedDuringInspectionCheckTheInstallationAgain);

            return new(true, UpdaterText.GameCompatibilityCheckTheGameMatchesThisIronmonRelease);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return new(false, UpdaterText.GameCompatibilityCheckTheInstalledGameCouldNotBeVerifiedCheckThe);
        }
    }

    /// <summary>
    /// Reads ordinary loose or packed Git references without running a player-installed executable.
    /// </summary>
    /// <param name="root">The checked installation root.</param>
    /// <returns>The current commit, or null for a ZIP installation.</returns>
    public static string? ReadHead(string root)
    {
        var metadata = PlainPaths.Child(root, GitDirectory);
        if (!Path.Exists(metadata))
            return null;

        if (!Directory.Exists(metadata))
            throw new InvalidDataException(UpdaterText.GameCompatibilityCheckLinkedGitLayoutsAreUnsupported);

        var head = ReadSmall(PlainPaths.Child(root, HeadPath), 1024).Trim();
        if (head.StartsWith(RefPrefix, StringComparison.Ordinal))
        {
            var reference = head[RefPrefix.Length..];
            if (!reference.StartsWith(HeadsPrefix, StringComparison.Ordinal))
                throw new InvalidDataException(UpdaterText.GameCompatibilityCheckTheGameHEADReferenceIsUnsupported);

            var loose = PlainPaths.Child(metadata, reference);
            head = File.Exists(loose) ? ReadSmall(loose, 1024).Trim() : ReadSmall(PlainPaths.Child(root, PackedPath), MaximumRefsBytes).Split('\n').Select(line => line.Trim().Split(' ', 2)).SingleOrDefault(parts => parts.Length == 2 && parts[1] == reference)?[0] ?? throw new InvalidDataException(UpdaterText.GameCompatibilityCheckTheGameHEADReferenceIsMissing);
        }

        ReleaseProtocol.ValidateCommit(head);
        return head;
    }

    /// <summary>
    /// Bounds ordinary reference reads before allocating text.
    /// </summary>
    /// <param name="path">The validated reference path.</param>
    /// <param name="limit">The maximum byte count.</param>
    /// <returns>The reference text.</returns>
    private static string ReadSmall(string path, int limit)
    {
        if (new FileInfo(path).Length > limit)
            throw new InvalidDataException(UpdaterText.GameCompatibilityCheckTheGitReferenceExceedsItsSizeLimit);

        return File.ReadAllText(path);
    }
}

/// <summary>
/// Records startup compatibility independently of existing tracker views.
/// </summary>
/// <remarks>
/// Initializes the read-only compatibility outcome and its explanation.
/// </remarks>
/// <param name="Compatible">Whether inspected game code matches the release.</param>
/// <param name="Message">The outcome suitable for later update orchestration.</param>
public sealed record GameCompatibilityResult(bool Compatible, string Message);
