using Ironmon.Updater.Core;
using System.Text.RegularExpressions;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Validates an ordinary checkout and fetches approved objects outside the installation.
/// </summary>
public sealed partial class PrivateGitProvider
{
    private const string CommitPattern = "\\A[0-9a-f]{40}\\z";
    private const string GitName = ".git";
    private const string ShallowName = "shallow";
    private const string ConfigName = "config";
    private const string HooksName = "hooks";
    private const string SampleSuffix = ".sample";
    private const string ModulesName = ".gitmodules";
    private const string TabModules = "\t.gitmodules";
    private const string UnsupportedMetadata = "commondir|gitdir|worktrees|modules|shallow|objects/info/alternates|objects/info/http-alternates|info/attributes|info/sparse-checkout|config.worktree|refs/replace|index.lock|MERGE_HEAD|CHERRY_PICK_HEAD|REVERT_HEAD|rebase-apply|rebase-merge";
    private const string HomeName = "home";
    internal const string ObjectsName = "objects.git";
    private const string Init = "init";
    private const string Bare = "--bare";
    private const string EmptyTemplate = "--template=";
    private const string Fetch = "fetch";
    private const string NoTags = "--no-tags";
    private const string NoFetchHead = "--no-write-fetch-head";
    private const string NoSubmodules = "--no-recurse-submodules";
    private const string NoMaintenance = "--no-auto-maintenance";
    private const string CatFile = "cat-file";
    private const string TypeOption = "-t";
    private const string CommitType = "commit";
    private const string MergeBase = "merge-base";
    private const string IsAncestor = "--is-ancestor";
    private const string Fsck = "fsck";
    private const string Strict = "--strict";
    private const string NoReflogs = "--no-reflogs";
    private const string LsTree = "ls-tree";
    private const string Recursive = "-r";
    private const string Null = "-z";
    private const string SubmoduleMode = "160000";
    private const string RegularMode = "100644 ";
    private const string ExecutableMode = "100755 ";
    private const string NormalIndexFlag = "H ";
    private const string RevParse = "rev-parse";
    private const string TopLevel = "--show-toplevel";
    private const string SymbolicRef = "symbolic-ref";
    private const string Quiet = "--quiet";
    private const string Head = "HEAD";
    private const string Verify = "--verify";
    private const string HeadCommit = "HEAD^{commit}";
    private const string Diff = "diff";
    private const string Cached = "--cached";
    private const string NoExternalDiff = "--no-ext-diff";
    private const string EndOptions = "--";
    private const string LsFiles = "ls-files";
    private const string Stage = "--stage";
    private const string Flags = "-v";
    private const string Colon = ":";

    private readonly MinGitCache _cache;
    private readonly RepositoryPolicy _policy;
    private readonly string _staging;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Initializes the provider with a reviewed upstream and private staging root.
    /// </summary>
    /// <param name="cache">The private Git cache used to acquire a verified executable.</param>
    /// <param name="policy">The trusted upstream and branch policy; it must not come from installation metadata.</param>
    /// <param name="stagingRoot">The fully qualified updater-owned root for disposable preparation workspaces.</param>
    /// <param name="clock">The process timeout clock, or <see langword="null" /> to use the system clock.</param>
    public PrivateGitProvider(MinGitCache cache, RepositoryPolicy policy, string stagingRoot, TimeProvider? clock = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _staging = PlainPaths.Full(stagingRoot);
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Fetches an exact approved ancestor of the expected upstream branch without modifying installed files or Git state.
    /// </summary>
    /// <remarks>
    /// The caller supplies release approval; this method verifies object integrity and branch ancestry. Unsupported repository layouts or configuration are rejected. Inspection does not lock the installation against concurrent writers.
    /// </remarks>
    /// <param name="installationRoot">The fully qualified ordinary game checkout to inspect.</param>
    /// <param name="approvedCommit">The trusted target's exact lowercase forty-character SHA-1 commit ID.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task whose result owns the prepared workspace. The caller must dispose the result after using its staged objects.</returns>
    public async Task<PreparedGitRevision> FetchAsync(string installationRoot, string approvedCommit, CancellationToken cancellationToken = default)
    {
        if (!CommitRegex().IsMatch(approvedCommit))
            throw new ArgumentException(UpdaterText.PrivateGitProviderAnExactLowercaseSHA1CommitIsRequired, nameof(approvedCommit));

        var root = PlainPaths.Full(installationRoot);
        if (Overlaps(_staging, root) || Overlaps(_cache.Root, root))
            throw new IOException(UpdaterText.PrivateGitProviderObjectStagingAndTheInstallationMustBeSeparateDirectories);

        cancellationToken.ThrowIfCancellationRequested();
        ValidateLayout(root);
        var executable = await _cache.AcquireAsync(cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(PlainPaths.Full(_staging));
        var workspace = PlainPaths.Child(_staging, Guid.NewGuid().ToString());
        Directory.CreateDirectory(workspace);
        try
        {
            var git = new GitProcess(executable, PlainPaths.Child(workspace, HomeName), _clock, _policy.LocalFixture);
            var head = await InspectAsync(git, root, cancellationToken).ConfigureAwait(false);
            var objects = PlainPaths.Child(workspace, ObjectsName);
            await RequireAsync(git, workspace, [Init, Bare, EmptyTemplate, objects], cancellationToken).ConfigureAwait(false);
            await RequireAsync(git, objects, [Fetch, NoTags, NoFetchHead, NoSubmodules, NoMaintenance, _policy.Remote, approvedCommit, RepositoryPolicy.HeadsPrefix + _policy.Branch + Colon + RepositoryPolicy.RemoteRef + _policy.Branch], cancellationToken).ConfigureAwait(false);
            var type = await RequireAsync(git, objects, [CatFile, TypeOption, approvedCommit], cancellationToken).ConfigureAwait(false);
            if (type.Trim() != CommitType)
                throw new InvalidDataException(UpdaterText.PrivateGitProviderTheApprovedObjectIsNotACommit);

            await RequireAsync(git, objects, [MergeBase, IsAncestor, approvedCommit, RepositoryPolicy.RemoteRef + _policy.Branch], cancellationToken).ConfigureAwait(false);
            await RequireAsync(git, objects, [Fsck, Strict, NoReflogs, approvedCommit], cancellationToken).ConfigureAwait(false);
            var tree = await RequireAsync(git, objects, [LsTree, Recursive, Null, approvedCommit], cancellationToken).ConfigureAwait(false);
            if (tree.Split('\0', StringSplitOptions.RemoveEmptyEntries).Any(entry => entry.StartsWith(SubmoduleMode, StringComparison.Ordinal) || entry.EndsWith(TabModules, StringComparison.Ordinal)))
                throw new InvalidDataException(UpdaterText.PrivateGitProviderSubmodulesInAnApprovedRevisionRequireASeparatePolicy);

            if (head != await InspectAsync(git, root, cancellationToken).ConfigureAwait(false))
                throw new IOException(UpdaterText.PrivateGitProviderTheInstallationChangedDuringPreparation);

            return new PreparedGitRevision(_staging, workspace, approvedCommit);
        }
        catch
        {
            PlainPaths.DeleteOwned(_staging, workspace);
            throw;
        }
    }

    /// <summary>
    /// Detects equal, parent or child paths before allocating staging.
    /// </summary>
    /// <param name="first">The first normalized absolute path.</param>
    /// <param name="second">The second normalized absolute path.</param>
    /// <returns>Whether the paths are equal or one is a descendant of the other, compared without case sensitivity.</returns>
    private static bool Overlaps(string first, string second)
    {
        return first.Equals(second, StringComparison.OrdinalIgnoreCase)
            || first.StartsWith(second + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || second.StartsWith(first + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates ordinary metadata and rejects redirecting or unsupported repository layouts before invoking Git.
    /// </summary>
    /// <param name="root">The fully qualified installation root to inspect.</param>
    /// <param name="allowShallow">Whether authenticated combined preparation may inspect ordinary shallow metadata.</param>
    internal void ValidateLayout(string root, bool allowShallow = false)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(UpdaterText.PrivateGitProviderTheInstallationDoesNotExist);

        var metadata = PlainPaths.Child(root, GitName);
        if (!Directory.Exists(metadata))
            throw new InvalidDataException(UpdaterText.PrivateGitProviderOnlyOrdinaryGitCheckoutsAreSupportedInThisStep);

        PlainPaths.CheckTree(metadata);
        foreach (var name in UnsupportedMetadata.Split('|'))
        {
            if (allowShallow && name == ShallowName)
                continue;

            if (Path.Exists(Path.Combine(metadata, name)))
                throw new InvalidDataException(UpdaterText.PrivateGitProviderThisRepositoryLayoutRequiresAnUnsupportedMetadataPolicy);
        }

        if (File.Exists(Path.Combine(root, ModulesName)))
            throw new InvalidDataException(UpdaterText.PrivateGitProviderRepositoriesWithSubmodulesAreNotSupported);

        var hooks = Path.Combine(metadata, HooksName);
        if (Directory.Exists(hooks) && Directory.EnumerateFiles(hooks).Any(file => !file.EndsWith(SampleSuffix, StringComparison.Ordinal)))
            throw new InvalidDataException(UpdaterText.PrivateGitProviderTheRepositoryContainsCustomHooks);

        _policy.ValidateConfiguration(PlainPaths.Child(metadata, ConfigName));
    }

    /// <summary>
    /// Checks roots, branch, index and object connectivity with writes and optional locks disabled.
    /// </summary>
    /// <param name="git">The isolated private Git runner.</param>
    /// <param name="root">The normalized absolute installation root.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task whose result is the installed HEAD commit after the supported repository checks pass.</returns>
    internal async Task<string> InspectAsync(GitProcess git, string root, CancellationToken cancellationToken)
    {
        ValidateLayout(root, allowShallow: true);
        var reported = (await RequireAsync(git, root, [RevParse, TopLevel], cancellationToken).ConfigureAwait(false)).Trim();
        if (!PlainPaths.Full(reported).Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(UpdaterText.PrivateGitProviderGitDidNotIdentifyTheExpectedInstallationRoot);

        var branch = (await RequireAsync(git, root, [SymbolicRef, Quiet, Head], cancellationToken).ConfigureAwait(false)).Trim();
        if (branch != RepositoryPolicy.HeadsPrefix + _policy.Branch)
            throw new InvalidDataException(UpdaterText.PrivateGitProviderTheInstallationIsNotOnItsExpectedReleaseBranch);

        var head = (await RequireAsync(git, root, [RevParse, Verify, HeadCommit], cancellationToken).ConfigureAwait(false)).Trim();
        await RequireAsync(git, root, [Diff, Cached, Quiet, NoExternalDiff, Head, EndOptions], cancellationToken).ConfigureAwait(false);
        var index = await RequireAsync(git, root, [LsFiles, Stage, Null], cancellationToken).ConfigureAwait(false);
        foreach (var entry in index.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!entry.StartsWith(RegularMode, StringComparison.Ordinal) && !entry.StartsWith(ExecutableMode, StringComparison.Ordinal))
                throw new InvalidDataException(UpdaterText.PrivateGitProviderTheIndexContainsUnsupportedModesOrSubmodules);

            var path = entry[(entry.IndexOf('\t') + 1)..];
            PlainPaths.Child(root, path);
        }

        var flags = await RequireAsync(git, root, [LsFiles, Flags, Null], cancellationToken).ConfigureAwait(false);
        if (flags.Split('\0', StringSplitOptions.RemoveEmptyEntries).Any(entry => !entry.StartsWith(NormalIndexFlag, StringComparison.Ordinal)))
            throw new InvalidDataException(UpdaterText.PrivateGitProviderHiddenIndexChangesAreNotSupported);

        await RequireAsync(git, root, [Fsck, Strict, NoReflogs, Head], cancellationToken).ConfigureAwait(false);
        return head;
    }

    /// <summary>
    /// Requires success without exposing raw process output as a user-facing error.
    /// </summary>
    /// <param name="git">The isolated private Git runner.</param>
    /// <param name="directory">The fully qualified working directory.</param>
    /// <param name="arguments">The structured command arguments.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task whose result is the standard output of a successful command.</returns>
    /// <exception cref="InvalidDataException">Git exits with a nonzero status.</exception>
    private static async Task<string> RequireAsync(GitProcess git, string directory, string[] arguments, CancellationToken cancellationToken)
    {
        var result = await git.RunAsync(directory, arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidDataException(UpdaterText.PrivateGitProviderGitCouldNotVerifyOrPrepareTheRequestedRepository);

        return result.Output;
    }

    /// <summary>
    /// Gets the generated expression for exact lowercase SHA-1 commit identifiers.
    /// </summary>
    /// <returns>The culture-invariant commit identifier expression.</returns>
    [GeneratedRegex(CommitPattern, RegexOptions.CultureInvariant)]
    private static partial Regex CommitRegex();
}
