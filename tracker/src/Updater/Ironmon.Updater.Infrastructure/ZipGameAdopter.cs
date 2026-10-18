using System.Text.RegularExpressions;
using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Recognizes a complete approved ZIP snapshot and prepares ordinary Git metadata outside the game.
/// </summary>
public sealed partial class ZipGameAdopter
{
    private const string CommitPattern = "\\A[0-9a-f]{40}\\z";
    private const string DigestPattern = "\\A[0-9A-F]{64}\\z";
    private const string RegularMode = "100644";
    private const string ExecutableMode = "100755";
    private const string BlobType = "blob";
    private const string HomeName = "home";
    private const string Init = "init";
    private const string Bare = "--bare";
    private const string EmptyTemplate = "--template=";
    private const string Fetch = "fetch";
    private const string NoTags = "--no-tags";
    private const string NoFetchHead = "--no-write-fetch-head";
    private const string NoSubmodules = "--recurse-submodules=no";
    private const string NoMaintenance = "--no-auto-maintenance";
    private const string Colon = ":";
    private const string MergeBase = "merge-base";
    private const string IsAncestor = "--is-ancestor";
    private const string Fsck = "fsck";
    private const string CatFile = "cat-file";
    private const string ObjectType = "-t";
    private const string CommitType = "commit";
    private const string Strict = "--strict";
    private const string NoReflogs = "--no-reflogs";
    private const string Tree = "ls-tree";
    private const string Recursive = "-r";
    private const string Long = "-l";
    private const string Null = "-z";
    private const string Config = "config";
    private const string BareKey = "core.bare";
    private const string False = "false";
    private const string True = "true";
    private const string AutocrlfKey = "core.autocrlf";
    private const string UrlKey = "remote.origin.url";
    private const string FetchKey = "remote.origin.fetch";
    private const string FetchSpec = "+refs/heads/*:refs/remotes/origin/*";
    private const string BranchPrefix = "branch.";
    private const string RemoteSuffix = ".remote";
    private const string MergeSuffix = ".merge";
    private const string SymbolicRef = "symbolic-ref";
    private const string Head = "HEAD";
    private const string UpdateRef = "update-ref";
    private const string TargetRef = "refs/ironmon/prepared-target";
    private const string ReadTree = "read-tree";
    private const string ConfigFile = "config";
    private const string AttributesFile = ".gitattributes";
    private const string ModulesFile = ".gitmodules";
    private const string UpdateState = ".ironmon-update";
    private const int MaximumFiles = 100000;
    private readonly MinGitCache _cache;
    private readonly RepositoryPolicy _policy;
    private readonly IReadOnlyList<GameBaseline> _baselines;
    private readonly string _staging;

    /// <summary>
    /// Initializes adoption from trusted historical inventories and an approved upstream policy.
    /// </summary>
    /// <param name="cache">The private verified Git cache.</param>
    /// <param name="policy">The trusted official remote and branch policy.</param>
    /// <param name="baselines">Complete authenticated historical inventories; installed files must never supply these.</param>
    /// <param name="stagingRoot">The fully qualified updater-owned preparation root outside the game.</param>
    public ZipGameAdopter(MinGitCache cache, RepositoryPolicy policy, IReadOnlyList<GameBaseline> baselines, string stagingRoot)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        ArgumentNullException.ThrowIfNull(baselines);
        _baselines = baselines.Select(baseline => baseline with { Files = Array.AsReadOnly(baseline.Files.ToArray()) }).ToArray();
        _staging = PlainPaths.Full(stagingRoot);
    }

    /// <summary>
    /// Recognizes the ZIP baseline, verifies upstream objects and prepares metadata without writing to the installation.
    /// </summary>
    /// <param name="installationRoot">The fully qualified candidate game root.</param>
    /// <param name="approvedTarget">The trusted target release's exact game commit.</param>
    /// <param name="cancellationToken">The token used to cancel preparation.</param>
    /// <returns>A caller-owned preparation result to dispose after the later transaction consumes it.</returns>
    public async Task<ZipAdoptionPreparation> PrepareAsync(string installationRoot, string approvedTarget, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateCommit(approvedTarget);
        var root = PlainPaths.Full(installationRoot);
        if (Overlaps(root, _staging) || Overlaps(root, _cache.Root))
            throw new IOException(UpdaterText.ZipGameAdopterZIPAdoptionRequiresCacheAndPreparationDirectoriesOutsideThe);

        GameInstallationLocator.ValidateCandidate(root);
        var snapshot = await GameDirectorySnapshot.ReadAsync(root, cancellationToken).ConfigureAwait(false);
        var local = snapshot.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        var matches = _baselines.Where(baseline => Matches(root, baseline, local)).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException(UpdaterText.ZipGameAdopterTheGameDoesNotMatchOneUnambiguousApprovedZIP);

        var baseline = matches[0];
        var baselinePaths = baseline.Files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extras = snapshot.Where(entry => entry.Content is not null && !baselinePaths.Contains(entry.Path)).Select(entry => entry.Path).ToArray();
        var executable = await _cache.AcquireAsync(cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(PlainPaths.Full(_staging));
        var workspace = PlainPaths.Child(_staging, Guid.NewGuid().ToString());
        Directory.CreateDirectory(workspace);
        try
        {
            var git = new GitProcess(executable, PlainPaths.Child(workspace, HomeName), localFixtures: _policy.LocalFixture);
            var metadata = PlainPaths.Child(workspace, GameDirectorySnapshot.GitDirectory);
            await RequireAsync(git, workspace, [Init, Bare, EmptyTemplate, metadata], cancellationToken: cancellationToken).ConfigureAwait(false);
            var upstreamRef = RepositoryPolicy.RemoteRef + _policy.Branch;
            await RequireAsync(git, metadata, [Fetch, NoTags, NoFetchHead, NoSubmodules, NoMaintenance, _policy.Remote, baseline.Commit, approvedTarget, RepositoryPolicy.HeadsPrefix + _policy.Branch + Colon + upstreamRef], TimeSpan.FromMinutes(20), cancellationToken).ConfigureAwait(false);
            string[] revisions = [baseline.Commit, approvedTarget];
            foreach (var revision in revisions)
            {
                var type = await RequireAsync(git, metadata, [CatFile, ObjectType, revision], cancellationToken: cancellationToken).ConfigureAwait(false);
                if (type.Trim() != CommitType)
                    throw new InvalidDataException(UpdaterText.ZipGameAdopterAdoptionRequiresExactCommitObjectsNotTagsOrOther);
            }

            await RequireAsync(git, metadata, [MergeBase, IsAncestor, baseline.Commit, approvedTarget], cancellationToken: cancellationToken).ConfigureAwait(false);
            await RequireAsync(git, metadata, [MergeBase, IsAncestor, approvedTarget, upstreamRef], cancellationToken: cancellationToken).ConfigureAwait(false);
            await RequireAsync(git, metadata, [Fsck, Strict, NoReflogs, baseline.Commit, approvedTarget], cancellationToken: cancellationToken).ConfigureAwait(false);
            var originalTree = await ReadTreeAsync(git, metadata, baseline.Commit, cancellationToken).ConfigureAwait(false);
            ValidateInventoryTree(baseline, originalTree);
            var targetTree = await ReadTreeAsync(git, metadata, approvedTarget, cancellationToken).ConfigureAwait(false);
            ValidateTreePaths(root, targetTree.Select(file => file.Path));

            var collisions = FindCollisions(snapshot, baselinePaths, targetTree.Select(file => file.Path));
            await ConfigureAsync(git, metadata, baseline.Commit, approvedTarget, cancellationToken).ConfigureAwait(false);
            _policy.ValidateConfiguration(Path.Combine(metadata, ConfigFile));
            var result = new ZipAdoptionPreparation(_staging, workspace, root, baseline.Commit, approvedTarget, snapshot, extras, collisions);
            await result.RevalidateAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            PlainPaths.DeleteOwned(_staging, workspace);
            throw;
        }
    }

    /// <summary>
    /// Validates an inventory and compares every owned file against its explicitly accepted byte representations.
    /// </summary>
    /// <param name="root">The candidate installation root used for path validation.</param>
    /// <param name="baseline">The complete trusted historical inventory.</param>
    /// <param name="local">The actual installation entries.</param>
    /// <returns>Whether every baseline file matches without inferring ownership of extra files.</returns>
    private static bool Matches(string root, GameBaseline baseline, IReadOnlyDictionary<string, GameDirectoryEntry> local)
    {
        ValidateCommit(baseline.Commit);
        if (baseline.Files.Count == 0 || baseline.Files.Count > MaximumFiles)
            throw new InvalidDataException(UpdaterText.ZipGameAdopterTheBaselineInventoryHasAnUnsupportedFileCount);

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matched = true;
        foreach (var file in baseline.Files)
        {
            ValidateGamePath(root, file.Path);
            ValidateCommit(file.Blob);
            ValidateContent(file.Canonical);
            if (file.WindowsText is not null)
                ValidateContent(file.WindowsText);

            if (!paths.Add(file.Path) || file.Mode is not (RegularMode or ExecutableMode))
                throw new InvalidDataException(UpdaterText.ZipGameAdopterTheBaselineInventoryContainsDuplicatePathsOrUnsupportedFile);

            if (ReleaseVerifier.IsManagedGamePath(file.Path) && (!local.TryGetValue(file.Path, out var entry) || entry.Content is null || (entry.Content != file.Canonical && entry.Content != file.WindowsText)))
                matched = false;
        }

        if (!paths.Contains(GameInstallationLocator.GameIni) || !paths.Contains(GameInstallationLocator.GameExecutable))
            throw new InvalidDataException(UpdaterText.ZipGameAdopterTheBaselineInventoryMustAuthenticateTheExecutableAndGame);

        return matched;
    }

    /// <summary>
    /// Reads the exact regular-file tree, rejecting submodules and symlinks before metadata can be promoted.
    /// </summary>
    /// <param name="git">The isolated Git runner.</param>
    /// <param name="metadata">The prepared repository directory.</param>
    /// <param name="commit">The exact verified commit to inspect.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>The Git mode, blob, length and path of each tracked file.</returns>
    private static async Task<IReadOnlyList<GameTreeEntry>> ReadTreeAsync(GitProcess git, string metadata, string commit, CancellationToken cancellationToken)
    {
        var output = await RequireAsync(git, metadata, [Tree, Recursive, Long, Null, commit], cancellationToken: cancellationToken).ConfigureAwait(false);
        var entries = new List<GameTreeEntry>();
        foreach (var row in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = row.IndexOf('\t');
            var fields = row[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (entries.Count >= MaximumFiles || fields.Length != 4 || fields[0] is not (RegularMode or ExecutableMode) || fields[1] != BlobType || !long.TryParse(fields[3], out var length))
                throw new InvalidDataException(UpdaterText.ZipGameAdopterTheApprovedGameTreeContainsUnsupportedEntries);

            entries.Add(new GameTreeEntry(row[(tab + 1)..], fields[0], fields[2], length));
        }

        return entries;
    }

    /// <summary>
    /// Requires the complete inventory to describe the fetched baseline tree exactly.
    /// </summary>
    /// <param name="baseline">The trusted historical inventory.</param>
    /// <param name="tree">The fetched commit's full file tree.</param>
    private static void ValidateInventoryTree(GameBaseline baseline, IReadOnlyList<GameTreeEntry> tree)
    {
        var expected = baseline.Files.Select(file => new GameTreeEntry(file.Path, file.Mode, file.Blob, file.Canonical.Length)).OrderBy(file => file.Path, StringComparer.Ordinal);
        if (!expected.SequenceEqual(tree.OrderBy(file => file.Path, StringComparer.Ordinal)))
            throw new InvalidDataException(UpdaterText.ZipGameAdopterTheHistoricalInventoryDoesNotCoverTheCompleteApproved);
    }

    /// <summary>
    /// Prepares branch, index and tracking metadata while retaining both baseline and approved target objects.
    /// </summary>
    /// <param name="git">The isolated Git runner.</param>
    /// <param name="metadata">The privately owned repository directory.</param>
    /// <param name="baseline">The exact installed baseline commit.</param>
    /// <param name="target">The exact approved future target commit.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A task that completes when the metadata represents an ordinary baseline checkout.</returns>
    private async Task ConfigureAsync(GitProcess git, string metadata, string baseline, string target, CancellationToken cancellationToken)
    {
        await RequireAsync(git, metadata, [Config, UrlKey, _policy.Remote], cancellationToken: cancellationToken).ConfigureAwait(false);
        await RequireAsync(git, metadata, [Config, FetchKey, FetchSpec], cancellationToken: cancellationToken).ConfigureAwait(false);
        await RequireAsync(git, metadata, [Config, BranchPrefix + _policy.Branch + RemoteSuffix, RepositoryPolicy.Origin], cancellationToken: cancellationToken).ConfigureAwait(false);
        await RequireAsync(git, metadata, [Config, BranchPrefix + _policy.Branch + MergeSuffix, RepositoryPolicy.HeadsPrefix + _policy.Branch], cancellationToken: cancellationToken).ConfigureAwait(false);
        await RequireAsync(git, metadata, [Config, AutocrlfKey, True], cancellationToken: cancellationToken).ConfigureAwait(false);
        await RequireAsync(git, metadata, [SymbolicRef, Head, RepositoryPolicy.HeadsPrefix + _policy.Branch], cancellationToken: cancellationToken).ConfigureAwait(false);
        await RequireAsync(git, metadata, [UpdateRef, RepositoryPolicy.HeadsPrefix + _policy.Branch, baseline], cancellationToken: cancellationToken).ConfigureAwait(false);
        await RequireAsync(git, metadata, [UpdateRef, TargetRef, target], cancellationToken: cancellationToken).ConfigureAwait(false);
        await RequireAsync(git, metadata, [ReadTree, baseline], cancellationToken: cancellationToken).ConfigureAwait(false);
        await RequireAsync(git, metadata, [Config, BareKey, False], cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Identifies extra files or directories that overlap target file destinations.
    /// </summary>
    /// <param name="snapshot">The actual installation entries.</param>
    /// <param name="baseline">The baseline-owned file paths.</param>
    /// <param name="targetPaths">The target-owned file paths.</param>
    /// <returns>The sorted obstructing paths; conflict resolution belongs to the later file planner.</returns>
    private static IReadOnlyList<string> FindCollisions(IReadOnlyList<GameDirectoryEntry> snapshot, HashSet<string> baseline, IEnumerable<string> targetPaths)
    {
        var target = targetPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var baselineDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in baseline)
        {
            for (var slash = path.LastIndexOf('/'); slash >= 0; slash = path.LastIndexOf('/', slash - 1))
                baselineDirectories.Add(path[..slash]);
        }

        foreach (var path in target)
        {
            for (var slash = path.LastIndexOf('/'); slash >= 0; slash = path.LastIndexOf('/', slash - 1))
                parents.Add(path[..slash]);
        }

        var collisions = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var entry in snapshot.Where(entry => !baseline.Contains(entry.Path)))
        {
            if (entry.Content is null && baselineDirectories.Contains(entry.Path))
                continue;

            if (target.Contains(entry.Path) || (entry.Content is not null && parents.Contains(entry.Path)) || entry.Path.Equals(UpdateState, StringComparison.OrdinalIgnoreCase) || entry.Path.StartsWith(UpdateState + '/', StringComparison.OrdinalIgnoreCase))
                collisions.Add(entry.Path);

            for (var slash = entry.Path.LastIndexOf('/'); slash >= 0; slash = entry.Path.LastIndexOf('/', slash - 1))
            {
                if (target.Contains(entry.Path[..slash]))
                    collisions.Add(entry.Path);
            }
        }

        return [.. collisions];
    }

    /// <summary>
    /// Rejects target trees whose distinct Git paths alias files or directories on Windows.
    /// </summary>
    /// <param name="root">The candidate root used for containment checks.</param>
    /// <param name="paths">Every case-sensitive tracked path in the approved target.</param>
    internal static void ValidateTreePaths(string root, IEnumerable<string> paths)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            ValidateGamePath(root, path);
            if (!files.Add(path))
                throw new InvalidDataException(UpdaterText.ZipGameAdopterTheGameTreeHasPathsThatCollideOnWindows);
        }

        foreach (var path in files)
        {
            for (var slash = path.LastIndexOf('/'); slash >= 0; slash = path.LastIndexOf('/', slash - 1))
            {
                if (files.Contains(path[..slash]))
                    throw new InvalidDataException(UpdaterText.ZipGameAdopterTheGameTreeHasAFileThatAlsoServes);
            }
        }
    }

    /// <summary>
    /// Rejects unsafe paths and game features that need a separate checkout-conversion policy.
    /// </summary>
    /// <param name="root">The candidate root used for containment checks.</param>
    /// <param name="path">The case-sensitive Git path.</param>
    private static void ValidateGamePath(string root, string path)
    {
        PlainPaths.Child(root, path);
        if (path.Contains('\\') || path.Split('/').Any(part => part.Equals(GameDirectorySnapshot.GitDirectory, StringComparison.OrdinalIgnoreCase) || part.Equals(AttributesFile, StringComparison.OrdinalIgnoreCase) || part.Equals(ModulesFile, StringComparison.OrdinalIgnoreCase) || part.Equals(UpdateState, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(UpdaterText.ZipGameAdopterTheGameInventoryRequiresAnUnsupportedMetadataOrAttribute);
    }

    /// <summary>
    /// Validates an exact Git object ID before it can become a command argument.
    /// </summary>
    /// <param name="commit">The lowercase forty-character SHA-1 identifier.</param>
    private static void ValidateCommit(string commit)
    {
        if (!CommitRegex().IsMatch(commit))
            throw new InvalidDataException(UpdaterText.ZipGameAdopterAnExactLowercaseGitObjectIDIsRequired);
    }

    /// <summary>
    /// Validates the shape of an authenticated content fingerprint.
    /// </summary>
    /// <param name="content">The expected byte length and digest.</param>
    private static void ValidateContent(GameFileContent content)
    {
        if (content.Length < 0 || !DigestRegex().IsMatch(content.Sha256))
            throw new InvalidDataException(UpdaterText.ZipGameAdopterTheBaselineContainsAnInvalidByteFingerprint);
    }

    /// <summary>
    /// Tests equal paths and either direction of directory containment.
    /// </summary>
    /// <param name="first">The first normalized absolute path.</param>
    /// <param name="second">The second normalized absolute path.</param>
    /// <returns>Whether using the paths together could write into the installation.</returns>
    private static bool Overlaps(string first, string second)
    {
        return first.Equals(second, StringComparison.OrdinalIgnoreCase)
            || first.StartsWith(second + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || second.StartsWith(first + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Runs one structured command and rejects nonzero exit status.
    /// </summary>
    /// <param name="git">The isolated private Git runner.</param>
    /// <param name="directory">The private working directory.</param>
    /// <param name="arguments">The explicit command arguments.</param>
    /// <param name="timeout">An optional command deadline; the initial game fetch allows twenty minutes.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>The successful command's bounded standard output.</returns>
    private static async Task<string> RequireAsync(GitProcess git, string directory, string[] arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var result = await git.RunAsync(directory, arguments, timeout, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidDataException(UpdaterText.ZipGameAdopterGitCouldNotVerifyOrPrepareZIPAdoption);

        return result.Output;
    }

    /// <summary>
    /// Gets the generated expression for exact lowercase SHA-1 object identifiers.
    /// </summary>
    /// <returns>The culture-invariant object identifier expression.</returns>
    [GeneratedRegex(CommitPattern, RegexOptions.CultureInvariant)]
    private static partial Regex CommitRegex();

    /// <summary>
    /// Gets the generated expression for uppercase SHA-256 content digests.
    /// </summary>
    /// <returns>The culture-invariant content digest expression.</returns>
    [GeneratedRegex(DigestPattern, RegexOptions.CultureInvariant)]
    private static partial Regex DigestRegex();

    /// <summary>
    /// Captures one regular file from a verified Git tree.
    /// </summary>
    /// <remarks>
    /// Initializes a structural inventory comparison entry.
    /// </remarks>
    /// <param name="Path">The exact Git path.</param>
    /// <param name="Mode">The regular-file mode.</param>
    /// <param name="Blob">The blob object ID.</param>
    /// <param name="Length">The canonical blob length.</param>
    private sealed record GameTreeEntry(string Path, string Mode, string Blob, long Length);
}
