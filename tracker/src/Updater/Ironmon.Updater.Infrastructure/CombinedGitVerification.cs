using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Revalidates prepared Git state from fixed recovery locations using private Git and signed commit identities.
/// </summary>
/// <remarks>
/// Constructs offline verification independently of mutable journal claims and local Git configuration.
/// </remarks>
/// <param name="cache">The independently verified private Git runtime.</param>
/// <param name="policy">The trusted fixed upstream policy.</param>
/// <param name="home">The private process home outside the game.</param>
public sealed class CombinedGitVerification(MinGitCache cache, RepositoryPolicy policy, string home)
{
    private const string GitDirectory = ".git";
    private const string MergeBase = "merge-base";
    private const string IsAncestor = "--is-ancestor";
    private const int MaximumMetadataPathBytes = 249;
    private readonly MinGitCache _cache = cache;
    private readonly RepositoryPolicy _policy = policy;
    private readonly string _home = PlainPaths.Full(home);

    /// <summary>
    /// Rejects roots whose durable Git witness would exceed the pinned Windows runtime's repository-discovery path budget.
    /// </summary>
    /// <param name="root">The selected absolute installation root.</param>
    internal static void EnsureSupportedRoot(string root)
    {
        var metadata = Path.Combine(PlainPaths.Full(root), IronmonOnlyUpdate.EvidenceDirectory, Guid.Empty.ToString(TransactionStorage.GuidFormat), GitDirectory);
        if (System.Text.Encoding.UTF8.GetByteCount(metadata) > MaximumMetadataPathBytes)
            throw new IOException(UpdaterText.CombinedGitVerificationThisInstallationPathIsTooLongForSafeGame);
    }

    /// <summary>
    /// Saves an independent immutable verification copy which remains available after Git promotion or rollback.
    /// </summary>
    /// <param name="description">The proposed transaction.</param>
    /// <param name="metadata">The verified prepared metadata.</param>
    /// <param name="cancellationToken">The copy token.</param>
    /// <returns>A task completing when the witness exactly matches the proposed Git state.</returns>
    internal static async Task PreserveAsync(TransactionDescription description, string metadata, CancellationToken cancellationToken)
    {
        var destination = PlainPaths.Child(WitnessRoot(description), GitDirectory);
        if (Path.Exists(destination))
            throw new IOException(UpdaterText.CombinedGitVerificationThisTransactionAlreadyHasGitVerificationEvidence);

        Directory.CreateDirectory(destination);
        foreach (var entry in description.GitAfter ?? throw new InvalidDataException(UpdaterText.CombinedGitVerificationCombinedUpdatesRequirePreparedGitMetadata))
        {
            var target = PlainPaths.Child(destination, entry.Path);
            if (entry.Content is null)
            {
                Directory.CreateDirectory(target);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await TransactionStorage.CopyAsync(PlainPaths.Child(metadata, entry.Path), target, entry.Content, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Checks exact metadata bytes, upstream configuration, branch, index, connectivity and forward ancestry.
    /// </summary>
    /// <param name="description">The independently reconstructed signed transaction.</param>
    /// <param name="previousCommit">The signed historical baseline.</param>
    /// <param name="targetCommit">The signed selected target.</param>
    /// <param name="cancellationToken">The verification token.</param>
    /// <returns>A task rejecting arbitrary metadata substituted into a journal.</returns>
    public async Task AuthorizeAsync(TransactionDescription description, string previousCommit, string targetCommit, CancellationToken cancellationToken)
    {
        var root = WitnessRoot(description);
        var actual = await TransactionStorage.TreeAsync(PlainPaths.Child(root, GitDirectory), cancellationToken).ConfigureAwait(false);
        if (description.GitAfter is null || !description.GitAfter.SequenceEqual(actual))
            throw new InvalidDataException(UpdaterText.CombinedGitVerificationTheTransactionSGitStateDiffersFromItsIndependently);

        var git = new GitProcess(await _cache.AcquireAsync(cancellationToken).ConfigureAwait(false), _home, localFixtures: _policy.LocalFixture);
        var provider = new PrivateGitProvider(_cache, _policy, _home);
        provider.ValidateLayout(root, allowShallow: true);
        if (await provider.InspectAsync(git, root, cancellationToken).ConfigureAwait(false) != targetCommit)
            throw new InvalidDataException(UpdaterText.CombinedGitVerificationThePreparedBranchOrIndexDoesNotIdentifyThe);

        var ancestry = await git.RunAsync(root, [MergeBase, IsAncestor, previousCommit, targetCommit], cancellationToken: cancellationToken).ConfigureAwait(false);
        if (ancestry.ExitCode != 0)
            throw new InvalidDataException(UpdaterText.CombinedGitVerificationCombinedUpdatesCannotDowngradeOrCrossUnrelatedGameHistories);
    }

    /// <summary>
    /// Requires the live final checkout to expose the approved branch and clean target index before commit.
    /// </summary>
    /// <param name="description">The authenticated transaction.</param>
    /// <param name="targetCommit">The signed expected game commit.</param>
    /// <param name="cancellationToken">The verification token.</param>
    /// <returns>The independent final Git verification.</returns>
    public async Task VerifyInstalledAsync(TransactionDescription description, string targetCommit, CancellationToken cancellationToken)
    {
        var git = new GitProcess(await _cache.AcquireAsync(cancellationToken).ConfigureAwait(false), _home, localFixtures: _policy.LocalFixture);
        var provider = new PrivateGitProvider(_cache, _policy, _home);
        if (await provider.InspectAsync(git, description.InstallationRoot, cancellationToken).ConfigureAwait(false) != targetCommit)
            throw new InvalidDataException(UpdaterText.CombinedGitVerificationTheInstalledGitBranchAndIndexDoNotMatch);
    }

    /// <summary>
    /// Resolves the fixed recovery witness by typed transaction identity.
    /// </summary>
    /// <param name="description">The bound transaction.</param>
    /// <returns>The installation-contained witness root.</returns>
    internal static string WitnessRoot(TransactionDescription description)
        => PlainPaths.Child(description.InstallationRoot, IronmonOnlyUpdate.EvidenceDirectory + '/' + description.TransactionId.ToString(TransactionStorage.GuidFormat));
}
