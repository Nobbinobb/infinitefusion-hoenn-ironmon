using System.Text;
using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;
using static Ironmon.Updater.Tests.ReleaseBundleFixture;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises signed replacements of a test build without changing its public version.
/// </summary>
public sealed class SetupBuildUpgradeTests
{
    private const string _configuration = "[Game]\nTitle=infinitefusion-hoenn\n";
    private const string _core = "Data/Scripts/997_Ironmon/030_Collection_00_Core.rb";
    private const string _oldCore = "previous test build";
    private const string _newCore = "replacement test build";
    private const string _playerEdit = "player modification";
    private const string _save = "File H.rxdata";
    private const string _settings = "player-settings.json";
    private const string _customSprite = "Graphics/player-sprite.png";
    private const string _head = ".git/HEAD";
    private const string _git = "unused-git";
    private const string _staging = "unused-staging";
    private const string _home = "unused-home";
    private const string _downloads = "review-downloads";
    private const string _zipExtension = ".zip";
    private const string _unknownInventory = "unreferenced.json";

    /// <summary>
    /// Reviews and applies a higher signed build of the installed version using its retained ownership evidence.
    /// </summary>
    /// <param name="flavor">The installed runtime package retained by Setup.</param>
    /// <param name="failFinal">Whether independent runtime verification must roll back the replacement.</param>
    /// <returns>Completion after installation, replacement and final ownership verification.</returns>
    [Theory]
    [InlineData(ReleaseProtocol.RuntimeRequired, false)]
    [InlineData(ReleaseProtocol.SelfContained, false)]
    [InlineData(ReleaseProtocol.RuntimeRequired, true)]
    public async Task SameVersionBuildUsesInstalledSignedBaseline(string flavor, bool failFinal)
    {
        using ReleaseBundleFixture fixture = new(gameConfiguration: Encoding.UTF8.GetBytes(_configuration));
        await InstallAsync(fixture, flavor, 11);
        Write(fixture.Root, _save, [42]);
        Write(fixture.Root, _settings, [43]);
        Write(fixture.Root, _customSprite, [44]);
        fixture.ResetBuild(VersionA);
        fixture.Build(VersionA, releaseSequence: 12, customize: (root, _) => Write(root, _core, Encoding.UTF8.GetBytes(_newCore)));
        ReleaseEvidence replacement = fixture.Sign(VersionA);
        SetupPreparation preparation = Preparation(fixture);
        int downloads = fixture.Downloads.Count;
        SetupReview review = await preparation.ReviewAsync(fixture.Root, replacement);
        Assert.Equal(InstallationPurpose.Repair, review.Authorization.Purpose);
        Assert.NotNull(review.Authorization.Current);
        Assert.Equal(11, fixture.Verifier.Verify(review.Authorization.Current.Manifest, review.Authorization.Current.Signatures).ReleaseSequence);
        Assert.Equal(flavor, review.Authorization.Request.Flavor);
        Assert.False(review.AlreadyCurrent);
        Assert.True(review.Plan.CanApply);
        Assert.DoesNotContain(fixture.Downloads.Skip(downloads), uri => uri.EndsWith(_zipExtension, StringComparison.Ordinal));
        Assert.Equal(_oldCore, File.ReadAllText(Path.Combine(fixture.Root, _core)));
        PreparedIronmonUpdate prepared = await preparation.PrepareAsync(review, []);
        UpdateTransaction engine = new(new SignedIronmonAuthority(fixture.Verifier, SignedReleaseFixture.Runtime(failFinal)), _ => Task.CompletedTask);
        var result = await engine.ApplyAsync(fixture.Root, prepared.TransactionId);
        Assert.Equal(failFinal ? TransactionPhase.RolledBack : TransactionPhase.Committed, result.Phase);
        Assert.Equal(failFinal ? _oldCore : _newCore, File.ReadAllText(Path.Combine(fixture.Root, _core)));
        Assert.Equal(new byte[] { 42 }, File.ReadAllBytes(Path.Combine(fixture.Root, _save)));
        Assert.Equal(new byte[] { 43 }, File.ReadAllBytes(Path.Combine(fixture.Root, _settings)));
        Assert.Equal(new byte[] { 44 }, File.ReadAllBytes(Path.Combine(fixture.Root, _customSprite)));
        Assert.Null(UpdateTransaction.ReadActiveId(fixture.Root));
        if (!failFinal)
        {
            SetupReview repair = await preparation.ReviewAsync(fixture.Root, replacement);
            Assert.True(repair.AlreadyCurrent);
            Assert.Null(repair.Authorization.Current);
        }
    }

    /// <summary>
    /// Refuses rollback and changed content reusing an already installed signed build sequence.
    /// </summary>
    /// <param name="targetSequence">The equal or older replacement sequence.</param>
    /// <returns>Completion after review rejects the replacement without changing the installed program.</returns>
    [Theory]
    [InlineData(11)]
    [InlineData(10)]
    public async Task SameVersionCannotReplaceOrRollBackSignedSequence(long targetSequence)
    {
        using ReleaseBundleFixture fixture = new(gameConfiguration: Encoding.UTF8.GetBytes(_configuration));
        await InstallAsync(fixture, ReleaseProtocol.RuntimeRequired, 11);
        fixture.ResetBuild(VersionA);
        fixture.Build(VersionA, releaseSequence: targetSequence, customize: (root, _) => Write(root, _core, Encoding.UTF8.GetBytes(_newCore)));
        ReleaseEvidence replacement = fixture.Sign(VersionA);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Preparation(fixture).ReviewAsync(fixture.Root, replacement));
        Assert.Equal(UpdaterText.SignedIronmonAuthorityThePreviousSignedReleaseIsNotACompatibleForward, error.Message);
        Assert.Equal(_oldCore, File.ReadAllText(Path.Combine(fixture.Root, _core)));
    }

    /// <summary>
    /// Requires explicit conflict approval for real local edits when the next test build changes the same file.
    /// </summary>
    /// <returns>Completion after unapproved replacement is rejected and the edit is preserved.</returns>
    [Fact]
    public async Task SameVersionBuildStillRejectsModifiedProgram()
    {
        using ReleaseBundleFixture fixture = new(gameConfiguration: Encoding.UTF8.GetBytes(_configuration));
        await InstallAsync(fixture, ReleaseProtocol.RuntimeRequired, 11);
        Write(fixture.Root, _core, Encoding.UTF8.GetBytes(_playerEdit));
        fixture.ResetBuild(VersionA);
        fixture.Build(VersionA, releaseSequence: 12, customize: (root, _) => Write(root, _core, Encoding.UTF8.GetBytes(_newCore)));
        ReleaseEvidence replacement = fixture.Sign(VersionA);
        SetupPreparation preparation = Preparation(fixture);
        SetupReview review = await preparation.ReviewAsync(fixture.Root, replacement);
        Assert.False(review.Plan.CanApply);
        await Assert.ThrowsAsync<IronmonUpdateConflictException>(() => preparation.PrepareAsync(review, []));
        Assert.Equal(_playerEdit, File.ReadAllText(Path.Combine(fixture.Root, _core)));
    }

    /// <summary>
    /// Authenticates retained inventory bytes and rejects duplicate or unrelated documents before downloading programs.
    /// </summary>
    /// <param name="mutation">Whether to corrupt bytes, duplicate a document, or add an unsigned reference.</param>
    /// <returns>Completion after portable evidence is rejected without downloading a program archive.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RetainedInventoryCannotBypassAuthentication(int mutation)
    {
        using SignedReleaseFixture fixture = new();
        InventoryEvidence first = fixture.Evidence.Inventories[0];
        byte[] changed = (byte[])first.Bytes.Clone();
        changed[0] ^= 1;
        InventoryEvidence[] documents = mutation switch
        {
            0 => [new InventoryEvidence(first.AssetName, changed), .. fixture.Evidence.Inventories.Skip(1)],
            1 => [.. fixture.Evidence.Inventories, first],
            _ => [.. fixture.Evidence.Inventories, new InventoryEvidence(_unknownInventory, [1])]
        };
        ReleaseEvidence evidence = fixture.Evidence with { Inventories = documents };
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Coordinator().PrepareAsync(fixture.Request(), evidence));
        string expected = mutation switch
        {
            0 => UpdaterText.ReleaseVerifierTheInventoryBytesDoNotMatchTheSignedRelease,
            1 => UpdaterText.SignedIronmonAuthorityTheRecoveryEvidenceContainsDuplicateInventoryDocuments,
            _ => UpdaterText.ReleaseVerifierTheInventoryIsNotReferencedByThisSignedRelease
        };
        Assert.Equal(expected, error.Message);
        Assert.DoesNotContain(fixture.Requests, uri => uri.AbsolutePath.EndsWith(_zipExtension, StringComparison.Ordinal));
    }

    /// <summary>
    /// Installs a generated signed build and retains its real authorization record for the subsequent review.
    /// </summary>
    /// <param name="fixture">The isolated generated release and installation.</param>
    /// <param name="flavor">The package flavor to install.</param>
    /// <param name="sequence">The installed signed build sequence.</param>
    /// <returns>Completion after the initial build is committed.</returns>
    private static async Task InstallAsync(ReleaseBundleFixture fixture, string flavor, long sequence)
    {
        Write(fixture.Root, GamePath, Encoding.UTF8.GetBytes(_configuration));
        Write(fixture.Root, GameInstallationLocator.GameExecutable, [77, 90]);
        Write(fixture.Root, _head, Encoding.UTF8.GetBytes(CommitA));
        fixture.Build(VersionA, releaseSequence: sequence, customize: (root, _) => Write(root, _core, Encoding.UTF8.GetBytes(_oldCore)));
        ReleaseEvidence evidence = fixture.Sign(VersionA);
        IronmonUpdateRequest request = new(fixture.Root, SetupProtocol.UninstalledVersion, flavor, flavor, CommitA, false, []);
        PreparedIronmonUpdate prepared = await fixture.Coordinator().PrepareInstallationAsync(request, evidence, null, InstallationPurpose.AddIronmon);
        UpdateTransaction engine = new(new SignedIronmonAuthority(fixture.Verifier, SignedReleaseFixture.Runtime()), _ => Task.CompletedTask);
        Assert.Equal(TransactionPhase.Committed, (await engine.ApplyAsync(fixture.Root, prepared.TransactionId)).Phase);
    }

    /// <summary>
    /// Composes the real Setup workflow with fixture-only executable-version detection and unused Git services.
    /// </summary>
    /// <param name="fixture">The isolated generated release and installation.</param>
    /// <returns>The actual signed Setup reviewer and preparer.</returns>
    private static SetupPreparation Preparation(ReleaseBundleFixture fixture)
    {
        string root = fixture.DirectoryFor(VersionB);
        ReleaseDownloadStore downloads = new(Path.Combine(root, _downloads), fixture);
        MinGitCache git = new(Path.Combine(root, _git), fixture);
        RepositoryPolicy policy = new(new Uri(ReleaseProtocol.GameRepository), ReleaseProtocol.GameBranch);
        CombinedGamePreparation game = new(git, policy, Path.Combine(root, _staging));
        CombinedGitVerification verification = new(git, policy, Path.Combine(root, _home));
        return new SetupPreparation(fixture.Verifier, downloads, SignedReleaseFixture.Runtime(), game, verification, _ => VersionA);
    }
}
