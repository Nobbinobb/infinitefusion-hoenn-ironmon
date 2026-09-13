using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Prepares authenticated game content and ordinary Git metadata without writing to the installed game.
/// </summary>
/// <remarks>
/// Constructs a preparation service with independently configured private Git and upstream policy.
/// </remarks>
/// <param name="cache">The verified private Git cache.</param>
/// <param name="policy">The fixed upstream, or an internal disposable fixture upstream.</param>
/// <param name="stagingRoot">The private workspace parent outside the installation.</param>
public sealed class CombinedGamePreparation(MinGitCache cache, RepositoryPolicy policy, string stagingRoot)
{
    private const string Home = "home";
    private const string GitDirectory = ".git";
    private const string Init = "init";
    private const string EmptyTemplate = "--template=";
    private const string InitialBranch = "--initial-branch=";
    private const string Remote = "remote";
    private const string Add = "add";
    private const string Fetch = "fetch";
    private const string NoTags = "--no-tags";
    private const string NoSubmodules = "--recurse-submodules=no";
    private const string NoMaintenance = "--no-auto-maintenance";
    private const string MergeBase = "merge-base";
    private const string IsAncestor = "--is-ancestor";
    private const string Reset = "reset";
    private const string Mixed = "--mixed";
    private const string Config = "config";
    private const string BranchPrefix = "branch.";
    private const string RemoteSuffix = ".remote";
    private const string MergeSuffix = ".merge";
    private const string Autocrlf = "core.autocrlf";
    private const string True = "true";
    private const string Archive = "archive";
    private const string ZipFormat = "--format=zip";
    private const string NoCompression = "-0";
    private const string Output = "--output=";
    private const string ArchiveName = "game.zip";
    private const string PayloadName = "payload";
    private const string Tree = "ls-tree";
    private const string Recursive = "-r";
    private const string Null = "-z";
    private const string Regular = "100644 blob ";
    private const string Executable = "100755 blob ";
    private const string Fsck = "fsck";
    private const string Strict = "--strict";
    private const string NoReflogs = "--no-reflogs";
    private const string Unshallow = "--unshallow";
    private const string Depth = "--depth=1";
    private const string Deepen = "--deepen=";
    private const string Progress = "--progress";
    private const string Shallow = "shallow";
    private const string Head = "HEAD";
    private const string CatFile = "cat-file";
    private const string Type = "-t";
    private const string CommitType = "commit";
    private readonly MinGitCache _cache = cache;
    private readonly RepositoryPolicy _policy = policy;
    private readonly string _staging = PlainPaths.Full(stagingRoot);

    /// <summary>
    /// Verifies the installed baseline and prepares a bounded-history release checkout at the exact approved forward commit.
    /// </summary>
    /// <param name="root">The installation root.</param>
    /// <param name="before">The authenticated installed game inventory.</param>
    /// <param name="target">The authenticated target game inventory.</param>
    /// <param name="cancellationToken">The preparation token.</param>
    /// <returns>The caller-owned game payload and ordinary Git metadata.</returns>
    public async Task<PreparedCombinedGame> PrepareAsync(string root, ReleaseFileInventory? before, ReleaseFileInventory target, CancellationToken cancellationToken = default)
    {
        root = PlainPaths.Full(root);
        CombinedGitVerification.EnsureSupportedRoot(root);
        if (_staging.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || root.StartsWith(_staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || root.Equals(_staging, StringComparison.OrdinalIgnoreCase) || _cache.Root.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || root.StartsWith(_cache.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || root.Equals(_cache.Root, StringComparison.OrdinalIgnoreCase))
            throw new IOException(UpdaterText.CombinedGamePreparationGamePreparationAndPrivateGitMustRemainOutsideThe);

        if (before is not null)
            ReleaseProtocol.ValidateCommit(before.GameCommit);
        ReleaseProtocol.ValidateCommit(target.GameCommit);

        var snapshot = await InstallationFileSnapshot.ReadAsync(root, cancellationToken).ConfigureAwait(false);
        if (before is null && (snapshot.Count != 0 || Path.Exists(PlainPaths.Child(root, GitDirectory))))
            throw new InvalidDataException(UpdaterText.CombinedGamePreparationANewGameInstallationRequiresAnEmptyDestination);

        InstallationProgressScope.Report(new(InstallationStage.PreparingDownload));
        var originalGit = Path.Exists(PlainPaths.Child(root, GitDirectory)) ? await TransactionStorage.TreeAsync(PlainPaths.Child(root, GitDirectory), cancellationToken).ConfigureAwait(false) : null;
        var executable = await _cache.AcquireAsync(cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(_staging);
        var workspace = PlainPaths.Child(_staging, Guid.NewGuid().ToString(TransactionStorage.GuidFormat));
        Directory.CreateDirectory(workspace);
        try
        {
            var git = new GitProcess(executable, PlainPaths.Child(workspace, Home), localFixtures: _policy.LocalFixture);
            if (originalGit is not null)
            {
                var provider = new PrivateGitProvider(_cache, _policy, _staging);
                var head = await provider.InspectAsync(git, root, cancellationToken).ConfigureAwait(false);
                if (head != before!.GameCommit)
                    throw new InvalidDataException(UpdaterText.CombinedGamePreparationTheGameWasChangedExternallyOrIsAlreadyNewer);
            }
            else if (before is not null)
            {
                await SignedIronmonAuthority.VerifyGameAsync(root, before, cancellationToken).ConfigureAwait(false);
            }

            if (originalGit is null)
            {
                await RequireAsync(git, workspace, [Init, EmptyTemplate, InitialBranch + _policy.Branch], cancellationToken).ConfigureAwait(false);
                await RequireAsync(git, workspace, [Remote, Add, RepositoryPolicy.Origin, _policy.Remote], cancellationToken).ConfigureAwait(false);
            }
            else
            {
                foreach (var entry in originalGit)
                {
                    var destination = PlainPaths.Child(workspace, GitDirectory + '/' + entry.Path);
                    if (entry.Content is null)
                    {
                        Directory.CreateDirectory(destination);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        await TransactionStorage.CopyAsync(PlainPaths.Child(root, GitDirectory + '/' + entry.Path), destination, entry.Content, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            var remoteRef = RepositoryPolicy.RemoteRef + _policy.Branch;
            var refspec = RepositoryPolicy.HeadsPrefix + _policy.Branch + ':' + remoteRef;
            var fetch = new List<string> { Fetch, Progress, NoTags, NoSubmodules, NoMaintenance };
            if (before is null)
                fetch.Add(Depth);

            fetch.AddRange([RepositoryPolicy.Origin, before?.GameCommit ?? target.GameCommit, target.GameCommit, refspec]);
            InstallationProgressScope.Report(new(InstallationStage.DownloadingGame));
            await RequireAsync(git, workspace, [.. fetch], cancellationToken).ConfigureAwait(false);
            InstallationProgressScope.Report(new(InstallationStage.VerifyingGame));
            foreach (var commit in new[] { before?.GameCommit ?? target.GameCommit, target.GameCommit })
            {
                if ((await RequireAsync(git, workspace, [CatFile, Type, commit], cancellationToken).ConfigureAwait(false)).Trim() != CommitType)
                    throw new InvalidDataException(UpdaterText.CombinedGamePreparationTheSignedGameIdentityMustResolveToAnExact);
            }

            await RequireAncestryAsync(git, workspace, before?.GameCommit ?? target.GameCommit, target.GameCommit, refspec, cancellationToken).ConfigureAwait(false);
            await RequireAncestryAsync(git, workspace, target.GameCommit, remoteRef, refspec, cancellationToken).ConfigureAwait(false);
            await RequireAsync(git, workspace, [Fsck, Strict, NoReflogs, before?.GameCommit ?? target.GameCommit, target.GameCommit], cancellationToken).ConfigureAwait(false);
            if (before is not null)
                await VerifyTreeAsync(git, workspace, before, cancellationToken).ConfigureAwait(false);

            await VerifyTreeAsync(git, workspace, target, cancellationToken).ConfigureAwait(false);
            InstallationProgressScope.Report(new(InstallationStage.PreparingGame));
            var archive = PlainPaths.Child(workspace, ArchiveName);
            await RequireAsync(git, workspace, [Archive, ZipFormat, NoCompression, Output + archive, target.GameCommit], cancellationToken).ConfigureAwait(false);
            var payload = PlainPaths.Child(workspace, PayloadName);
            await ReleaseArchive.ExtractAsync(archive, await TransactionStorage.ContentAsync(archive, cancellationToken).ConfigureAwait(false), payload, target.Files, cancellationToken).ConfigureAwait(false);
            InstallationProgressScope.Report(new(InstallationStage.VerifyingGame));
            await RequireAsync(git, workspace, [Config, Autocrlf, True], cancellationToken).ConfigureAwait(false);
            await RequireAsync(git, workspace, [Config, BranchPrefix + _policy.Branch + RemoteSuffix, RepositoryPolicy.Origin], cancellationToken).ConfigureAwait(false);
            await RequireAsync(git, workspace, [Config, BranchPrefix + _policy.Branch + MergeSuffix, RepositoryPolicy.HeadsPrefix + _policy.Branch], cancellationToken).ConfigureAwait(false);
            await RequireAsync(git, workspace, [Reset, Mixed, target.GameCommit], cancellationToken).ConfigureAwait(false);
            _policy.ValidateConfiguration(PlainPaths.Child(workspace, GitDirectory + '/' + Config));
            if (!snapshot.SequenceEqual(await InstallationFileSnapshot.ReadAsync(root, cancellationToken).ConfigureAwait(false)) || !await SameGitAsync(root, originalGit, cancellationToken).ConfigureAwait(false))
                throw new IOException(UpdaterText.CombinedGamePreparationTheInstallationChangedWhileTheCombinedUpdateWasBeing);

            return new PreparedCombinedGame(_staging, workspace, payload, PlainPaths.Child(workspace, GitDirectory), originalGit);
        }
        catch
        {
            PlainPaths.DeleteOwned(_staging, workspace);
            throw;
        }
    }

    /// <summary>
    /// Deepens a shallow checkout only when its current boundary prevents proving forward ancestry.
    /// </summary>
    /// <param name="git">The isolated private runner.</param>
    /// <param name="workspace">The disposable checkout.</param>
    /// <param name="before">The required ancestor.</param>
    /// <param name="target">The required descendant.</param>
    /// <param name="refspec">The fixed upstream branch mapping.</param>
    /// <param name="cancellationToken">The operation token.</param>
    /// <returns>The independently proven ancestry relation.</returns>
    private static async Task RequireAncestryAsync(GitProcess git, string workspace, string before, string target, string refspec, CancellationToken cancellationToken)
    {
        var depth = 1;
        while (true)
        {
            var result = await git.RunAsync(workspace, [MergeBase, IsAncestor, before, target], cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.ExitCode == 0)
                return;

            if (result.ExitCode != 1 || !File.Exists(PlainPaths.Child(workspace, GitDirectory + "/" + Shallow)))
                throw new InvalidDataException(UpdaterText.CombinedGitVerificationCombinedUpdatesCannotDowngradeOrCrossUnrelatedGameHistories);

            InstallationProgressScope.Report(new(InstallationStage.DownloadingGame));
            await RequireAsync(git, workspace, [Fetch, Progress, NoTags, NoSubmodules, NoMaintenance, depth > 1024 ? Unshallow : Deepen + depth.ToString(System.Globalization.CultureInfo.InvariantCulture), RepositoryPolicy.Origin, refspec], cancellationToken).ConfigureAwait(false);
            depth *= 2;
            InstallationProgressScope.Report(new(InstallationStage.VerifyingGame));
        }
    }

    /// <summary>
    /// Checks that fetched commit paths cover the complete signed ordinary-file inventory.
    /// </summary>
    /// <param name="git">The isolated Git runner.</param>
    /// <param name="workspace">The owned repository.</param>
    /// <param name="inventory">The signed commit inventory.</param>
    /// <param name="cancellationToken">The inspection token.</param>
    private static async Task VerifyTreeAsync(GitProcess git, string workspace, ReleaseFileInventory inventory, CancellationToken cancellationToken)
    {
        var rows = (await RequireAsync(git, workspace, [Tree, Recursive, Null, inventory.GameCommit], cancellationToken).ConfigureAwait(false)).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (rows.Any(row => !(row.StartsWith(Regular, StringComparison.Ordinal) || row.StartsWith(Executable, StringComparison.Ordinal)) || row.IndexOf('\t') < 0))
            throw new InvalidDataException(UpdaterText.CombinedGamePreparationTheGameTreeContainsLinksSubmodulesOrUnsupportedObjects);

        var paths = rows.Select(row => row[(row.IndexOf('\t') + 1)..]).Order(StringComparer.Ordinal);
        if (!paths.SequenceEqual(inventory.Files.Select(file => file.Path).Order(StringComparer.Ordinal)))
            throw new InvalidDataException(UpdaterText.CombinedGamePreparationTheSignedGameInventoryDoesNotCoverTheComplete);
    }

    /// <summary>
    /// Rechecks the original complete Git metadata without refreshing its index.
    /// </summary>
    /// <param name="root">The installation.</param>
    /// <param name="expected">The original Git tree, or null for ZIP.</param>
    /// <param name="cancellationToken">The snapshot token.</param>
    /// <returns>Whether the original metadata remains unchanged.</returns>
    internal static async Task<bool> SameGitAsync(string root, LocalFileEntry[]? expected, CancellationToken cancellationToken)
    {
        var path = PlainPaths.Child(root, GitDirectory);
        if (expected is null || !Directory.Exists(path))
            return expected is null && !Path.Exists(path);

        var actual = await TransactionStorage.TreeAsync(path, cancellationToken).ConfigureAwait(false);
        return expected.SequenceEqual(actual);
    }

    /// <summary>
    /// Runs a bounded private Git operation and reports a missing or unsupported approved revision clearly.
    /// </summary>
    /// <param name="git">The private runner.</param>
    /// <param name="root">The working directory.</param>
    /// <param name="arguments">The exact structured command.</param>
    /// <param name="cancellationToken">The operation token.</param>
    /// <returns>The bounded successful output.</returns>
    private static async Task<string> RequireAsync(GitProcess git, string root, string[] arguments, CancellationToken cancellationToken)
    {
        var result = await git.RunAsync(root, arguments, TimeSpan.FromMinutes(20), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidDataException(UpdaterText.CombinedGamePreparationTheExactApprovedGameRevisionCouldNotBeFetched);

        return result.Output;
    }
}

/// <summary>
/// Owns private game content and coherent Git metadata until the transaction has copied them.
/// </summary>
/// <remarks>
/// Constructs a disposable workspace result without transferring live installation ownership.
/// </remarks>
/// <param name="parent">The private workspace parent.</param>
/// <param name="workspace">The owned disposable directory.</param>
/// <param name="payload">The verified game program files.</param>
/// <param name="metadata">The prepared ordinary Git directory.</param>
/// <param name="originalGit">The complete original metadata snapshot.</param>
public sealed class PreparedCombinedGame(string parent, string workspace, string payload, string metadata, LocalFileEntry[]? originalGit) : IDisposable
{
    /// <summary>
    /// Gets the authenticated game payload directory.
    /// </summary>
    public string Payload { get; } = payload;
    /// <summary>
    /// Gets the prepared ordinary Git directory.
    /// </summary>
    public string Metadata { get; } = metadata;
    /// <summary>
    /// Gets the original metadata snapshot used to detect external launcher writes.
    /// </summary>
    public LocalFileEntry[]? OriginalGit { get; } = originalGit;
    /// <summary>
    /// Deletes only the owned private workspace after transaction preparation consumes it.
    /// </summary>
    public void Dispose()
        => PlainPaths.DeleteOwned(parent, workspace);
}
