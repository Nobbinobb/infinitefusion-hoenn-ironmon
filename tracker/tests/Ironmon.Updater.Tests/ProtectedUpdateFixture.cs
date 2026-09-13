using System.Runtime.Versioning;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises the real administrator worker with signed host bytes and an isolated ACL observation boundary.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ProtectedUpdateFixture : IDisposable
{
    private const string GitCache = "git";
    private const string GitHome = "home";
    private const string GameStaging = "staging";
    private readonly TestWorkspace _workspace = new();

    /// <summary>
    /// Gets the signed disposable release and installation.
    /// </summary>
    internal SignedReleaseFixture Release { get; } = new();

    /// <summary>
    /// Gets the bound capability used by worker operations.
    /// </summary>
    internal ProtectedUpdateTicket Ticket { get; } = new(ProtectedUpdateProtocol.PipePrefix + Guid.NewGuid().ToString(TransactionStorage.GuidFormat), Environment.ProcessId, Guid.NewGuid());

    /// <summary>
    /// Gets the number of protected-state boundaries reached after authentication.
    /// </summary>
    internal int ProtectedWrites { get; private set; }

    /// <summary>
    /// Gets the number of optional sprite operations performed after commit.
    /// </summary>
    internal int SpriteCalls { get; private set; }

    /// <summary>
    /// Signs the real test host so worker self-verification remains enabled.
    /// </summary>
    internal ProtectedUpdateFixture()
    {
        Release.UseHelper(File.ReadAllBytes(Environment.ProcessPath!));
    }

    /// <summary>
    /// Creates the production worker with fixture transport and a measured storage seam instead of machine elevation.
    /// </summary>
    /// <returns>A worker that still verifies signatures, paths, consent, backups and transaction boundaries.</returns>
    internal ProtectedUpdateWorker Worker()
    {
        var downloads = new ReleaseDownloadStore(Release.Cache, Release);
        var cache = new MinGitCache(_workspace.PathFor(GitCache), Release);
        var policy = new RepositoryPolicy(new Uri(ReleaseProtocol.GameRepository), ReleaseProtocol.GameBranch);
        return new ProtectedUpdateWorker(Release.Verifier, downloads, SignedReleaseFixture.Runtime(), new CombinedGamePreparation(cache, policy, _workspace.PathFor(GameStaging)), new CombinedGitVerification(cache, policy, _workspace.PathFor(GitHome)), (_, _, _) =>
        {
            SpriteCalls++;
            return Task.FromResult(new ProtectedSpriteResult(2, 3, 0, 123, 0));
        }, root =>
        {
            Assert.Equal(Release.Root, root);
            ProtectedWrites++;
        }, (root, _) =>
        {
            Assert.Equal(Release.Root, root);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Captures signed exact preparation input and the pre-UAC installation identity.
    /// </summary>
    /// <returns>The typed initial request.</returns>
    internal ProtectedUpdateMessage Initial()
        => new(Ticket, ProtectedUpdateOperation.Prepare, new IronmonUpdateAuthorization(1, Release.Request() with { ApprovedFiles = [] }, Release.Evidence, null), Release.Root, Guid.Empty, Release.Root, InstallationIdentity.Read(Release.Root));

    /// <summary>
    /// Creates an operation confined to the prepared root and transaction.
    /// </summary>
    /// <param name="prepared">The bound preparation.</param>
    /// <param name="operation">The closed operation.</param>
    /// <returns>The bounded operation message.</returns>
    internal ProtectedUpdateMessage Message(PreparedIronmonUpdate prepared, ProtectedUpdateOperation operation)
        => new(Ticket, operation, null, prepared.InstallationRoot, prepared.TransactionId);

    /// <summary>
    /// Removes only the owned fixture trees after workers have released their handles.
    /// </summary>
    public void Dispose()
    {
        Release.Dispose();
        _workspace.Dispose();
    }
}
