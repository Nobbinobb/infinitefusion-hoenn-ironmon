using System.Security.Cryptography;
using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Verifies transaction promotion and rollback against real ordinary repositories using only the pinned private Git.
/// </summary>
/// <remarks>
/// Constructs the integration fixture with the already verified private Git runtime.
/// </remarks>
/// <param name="git">The shared pinned runtime fixture.</param>
public sealed class GitTransactionTests(PinnedGitFixture git) : IClassFixture<PinnedGitFixture>
{
    private const string Clone = "clone";
    private const string NoLocal = "--no-local";
    private const string EmptyTemplate = "--template=";
    private const string Reset = "reset";
    private const string Mixed = "--mixed";
    private const string Revision = "rev-parse";
    private const string Head = "HEAD";
    private const string ListFiles = "ls-files";
    private const string StagedOption = "--stage";
    private const string Metadata = ".git";
    private const string Payload = "payload";

    /// <summary>
    /// Promotes a prepared mixed-reset index, or restores original refs/index/objects when final verification fails.
    /// </summary>
    /// <param name="rejectFinal">Whether to force rollback after all filesystem mutations.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealGitMetadataIsVerifiedAndRecoverable(bool rejectFinal)
    {
        using var game = new GameFixture(git);
        using var workspace = new TestWorkspace();
        await game.InitializeAsync();
        await game.RunAsync(workspace.Root, Clone, NoLocal, EmptyTemplate, game.Upstream, game.Staging);
        await game.RunAsync(game.Staging, Reset, Mixed, game.RevisionB);
        var originalIndex = await game.RunAsync(game.Installation, ListFiles, StagedOption);
        var payload = workspace.PathFor(Payload);
        Directory.CreateDirectory(payload);
        await File.WriteAllTextAsync(Path.Combine(payload, GameFixture.GameFile), GameFixture.ContentsB);
        var before = await InstallationFileSnapshot.ReadAsync(game.Installation);
        var originalGit = await TransactionStorage.TreeAsync(Path.Combine(game.Installation, Metadata), CancellationToken.None);
        var baseline = new[] { GameFixture.GameFile, GameFixture.ObsoleteFile }.Select(path => new ManagedFile(path, before.Single(entry => entry.Path == path).Content!, ManagedFileOwner.Game));
        var target = new ManagedFile(GameFixture.GameFile, await TransactionStorage.ContentAsync(Path.Combine(payload, GameFixture.GameFile), CancellationToken.None), ManagedFileOwner.Game);
        var plan = new FileUpdatePlanner(new FileManagementPolicy()).Create(baseline, before, [target]);
        var description = await UpdateTransaction.DescribeAsync(game.Installation, plan, Path.Combine(game.Staging, Metadata));
        var authority = new GitAuthority(TransactionStorage.Serialize(description), game, rejectFinal);
        var engine = new UpdateTransaction(authority, _ => Task.CompletedTask);
        await engine.PrepareAsync(description, payload, Path.Combine(game.Staging, Metadata));
        var result = await engine.ApplyAsync(game.Installation, description.TransactionId);
        Assert.Equal(rejectFinal ? TransactionPhase.RolledBack : TransactionPhase.Committed, result.Phase);
        Assert.Equal(rejectFinal ? game.RevisionA : game.RevisionB, await game.RunAsync(game.Installation, Revision, Head));
        Assert.Equal(GameFixture.UserContents, await File.ReadAllTextAsync(Path.Combine(game.Installation, GameFixture.UserFile)));
        if (rejectFinal)
        {
            Assert.Equal(originalIndex, await game.RunAsync(game.Installation, ListFiles, StagedOption));
            Assert.Equal(originalGit, await TransactionStorage.TreeAsync(Path.Combine(game.Installation, Metadata), CancellationToken.None));
            Assert.Equal(before, await InstallationFileSnapshot.ReadAsync(game.Installation));
        }
        else
        {
            Assert.Equal(GameFixture.ContentsB, await File.ReadAllTextAsync(Path.Combine(game.Installation, GameFixture.GameFile)));
            Assert.DoesNotContain(GameFixture.ObsoleteFile, await game.RunAsync(game.Installation, ListFiles), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies exact independent fixture authorization and the promoted real Git ref/index.
    /// </summary>
    /// <remarks>
    /// Constructs a verifier backed by the test's approved commit rather than mutable journal fields.
    /// </remarks>
    /// <param name="bytes">The independently held descriptor.</param>
    /// <param name="game">The real private Git fixture.</param>
    /// <param name="rejectFinal">Whether to simulate a final component mismatch.</param>
    private sealed class GitAuthority(byte[] bytes, GameFixture game, bool rejectFinal) : ITransactionAuthority
    {
        /// <summary>
        /// Rejects transaction authority changed after fixture approval.
        /// </summary>
        /// <param name="description">The proposed descriptor.</param>
        /// <param name="exactBytes">The persisted bytes.</param>
        /// <param name="cancellationToken">The validation token.</param>
        /// <returns>The descriptor verification result.</returns>
        public Task AuthorizeAsync(TransactionDescription description, ReadOnlyMemory<byte> exactBytes, CancellationToken cancellationToken) => CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), SHA256.HashData(exactBytes.Span)) ? Task.CompletedTask : Task.FromException(new InvalidDataException("The Git fixture transaction changed."));

        /// <summary>
        /// Requires the approved commit and index before allowing a final installation success.
        /// </summary>
        /// <param name="description">The authenticated transaction.</param>
        /// <param name="cancellationToken">The verification token.</param>
        /// <returns>The complete fixture component check.</returns>
        public async Task VerifyInstalledAsync(TransactionDescription description, CancellationToken cancellationToken)
        {
            Assert.Equal(game.RevisionB, await game.RunAsync(game.Installation, Revision, Head));
            Assert.DoesNotContain(GameFixture.ObsoleteFile, await game.RunAsync(game.Installation, ListFiles), StringComparison.Ordinal);
            if (rejectFinal)
                throw new InvalidDataException("Fixture component verification rejected the completed installation.");
        }
    }
}
