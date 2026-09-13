using Ironmon.Updater.Infrastructure;
using System.Security.Cryptography;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Proves ZIP recognition, isolated metadata preparation and fixture-only promotion and rollback.
/// </summary>
/// <remarks>
/// Initializes adoption tests with the real pinned private Git executable.
/// </remarks>
/// <param name="tool">The verified Git fixture.</param>
public sealed class ZipAdoptionTests(PinnedGitFixture tool) : IClassFixture<PinnedGitFixture>
{
    /// <summary>
    /// Recognizes complete upstream evidence while retaining edited or absent protected files in a ZIP installation.
    /// </summary>
    /// <param name="present">Whether the player retained an edited protected file.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProtectedTrackedFilesDoNotBlockZipRecognition(bool present)
    {
        using var game = new ZipGameFixture(tool);
        await game.InitializeAsync(includeProtectedFile: true);
        if (present)
        {
            ZipGameFixture.Write(game.Installation, ZipGameFixture.ProtectedFile, ZipGameFixture.ExtraText);
        }
        else
        {
            File.Delete(Path.Combine(game.Installation, ZipGameFixture.ProtectedFile));
        }

        var before = Snapshot(game.Installation);
        using var prepared = await game.CreateAdopter().PrepareAsync(game.Installation, game.Target);
        Assert.Equal(game.Baseline.Commit, prepared.BaselineCommit);
        Assert.Equal(before, Snapshot(game.Installation));
        await prepared.RevalidateAsync();
    }

    private const string GitName = ".git";
    private const string RevParse = "rev-parse";
    private const string Head = "HEAD";
    private const string SymbolicRef = "symbolic-ref";
    private const string Diff = "diff";
    private const string ExitCode = "--exit-code";
    private const string Cached = "--cached";
    private const string Config = "config";
    private const string OriginUrl = "remote.origin.url";
    private const string ConfigPath = ".git/config";
    private const string Fsck = "fsck";
    private const string Strict = "--strict";
    private const string Show = "show";
    private const string Colon = ":";
    private const string Wildcard = "*";
    private const string ChangedCase = "changed";
    private const string MissingCase = "missing";
    private const string MixedCase = "mixed";
    private const string BinaryCase = "binary";
    private const string UnknownExecutableCase = "executable";
    private const string CrLf = "\r\n";
    private const string Lf = "\n";
    private const string WrongTitle = "[Game]\nTitle=other-game\n";
    private const string DuplicateTitle = "[Game]\nTitle=infinitefusion-hoenn\nTitle=infinitefusion-hoenn\n";
    private const string ParentFile = "more";
    private const string ChildFile = "flatten/personal.txt";
    private const string UpdateState = ".ironmon-update/active.json";
    private const string MissingCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ManagedDirectory = "Data";
    private const string ExtraUnderManagedDirectory = "Data/personal.txt";

    /// <summary>
    /// Adopts a recognized ZIP in a fixture while preserving every original byte and satisfying standard checkout checks.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PreparesAndPromotesCoherentMetadataWithoutReplacingGameFiles()
    {
        using var game = new ZipGameFixture(tool);
        await game.InitializeAsync();
        var before = Snapshot(game.Installation);
        using var prepared = await game.CreateAdopter().PrepareAsync(game.Installation, game.Target);
        Assert.Equal(before, Snapshot(game.Installation));
        Assert.Equal(game.Baseline.Commit, prepared.BaselineCommit);
        Assert.Equal(game.Target, prepared.TargetCommit);
        Assert.Contains(ZipGameFixture.UserFile, prepared.ExtraFiles);
        Assert.Empty(prepared.TargetCollisions);
        await prepared.RevalidateAsync();
        Directory.Move(prepared.MetadataDirectory, Path.Combine(game.Installation, GitName));
        Assert.Equal(before, Snapshot(game.Installation).Where(row => !row.StartsWith(GitName + '/', StringComparison.Ordinal)).ToArray());
        Assert.Equal(game.Baseline.Commit, await game.RunAsync(game.Installation, RevParse, Head));
        Assert.Equal(RepositoryPolicy.HeadsPrefix + GameFixture.Branch, await game.RunAsync(game.Installation, SymbolicRef, Head));
        Assert.Equal(game.Policy.Remote, await game.RunAsync(game.Installation, Config, OriginUrl));
        Assert.Empty(await game.RunAsync(game.Installation, Diff, ExitCode));
        Assert.Empty(await game.RunAsync(game.Installation, Diff, Cached, ExitCode));
        await game.RunAsync(game.Installation, Fsck, Strict);
        game.Policy.ValidateConfiguration(Path.Combine(game.Installation, ConfigPath));
        GameInstallationLocator.ValidateCandidate(game.Installation);
        using var next = await new PrivateGitProvider(tool.Cache, game.Policy, game.Staging).FetchAsync(game.Installation, game.Target);
        Assert.Equal(ZipGameFixture.TextB.TrimEnd(), (await game.RunAsync(next.ObjectDirectory, Show, game.Target + Colon + ZipGameFixture.TextFile)).TrimEnd());
    }

    /// <summary>
    /// Restores the original absence of Git metadata when the fixture transaction fails after promotion.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FailureAfterPromotionRestoresOriginalNoGitState()
    {
        using var game = new ZipGameFixture(tool);
        await game.InitializeAsync();
        var before = Snapshot(game.Installation);
        var installedMetadata = Path.Combine(game.Installation, GitName);
        using (var prepared = await game.CreateAdopter().PrepareAsync(game.Installation, game.Target))
        {
            await Assert.ThrowsAsync<IOException>(async () =>
            {
                await prepared.RevalidateAsync();
                Directory.Move(prepared.MetadataDirectory, installedMetadata);
                try
                {
                    await game.RunAsync(game.Installation, Fsck, Strict);
                    throw new IOException("Injected failure after metadata promotion.");
                }
                finally
                {
                    PlainPaths.DeleteOwned(game.Installation, installedMetadata);
                }
            });
        }

        Assert.False(Path.Exists(installedMetadata));
        Assert.Equal(before, Snapshot(game.Installation));
        Assert.Empty(Directory.EnumerateFileSystemEntries(game.Staging));
    }

    /// <summary>
    /// Accepts an explicitly inventoried CRLF alternative and prepares a clean canonical Git index.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task AcceptsOnlyExplicitWindowsTextAlternatives()
    {
        using var game = new ZipGameFixture(tool);
        await game.InitializeAsync();
        ZipGameFixture.Write(game.Installation, ZipGameFixture.TextFile, ZipGameFixture.TextA.Replace(Lf, CrLf));
        using var prepared = await game.CreateAdopter().PrepareAsync(game.Installation, game.Target);
        Directory.Move(prepared.MetadataDirectory, Path.Combine(game.Installation, GitName));
        Assert.Empty(await game.RunAsync(game.Installation, Diff, ExitCode));
    }

    /// <summary>
    /// Rejects altered, missing, mixed and unapproved executable bytes without touching the installation.
    /// </summary>
    /// <param name="scenario">The unsupported content modification to create.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(ChangedCase)]
    [InlineData(MissingCase)]
    [InlineData(MixedCase)]
    [InlineData(BinaryCase)]
    [InlineData(UnknownExecutableCase)]
    public async Task RejectsUnknownOrMixedContent(string scenario)
    {
        using var game = new ZipGameFixture(tool);
        await game.InitializeAsync();
        if (scenario == MissingCase)
        {
            File.Delete(Path.Combine(game.Installation, ZipGameFixture.ObsoleteFile));
        }
        else if (scenario == UnknownExecutableCase)
        {
            File.AppendAllText(Path.Combine(game.Installation, GameInstallationLocator.GameExecutable), ZipGameFixture.ExtraText);
        }
        else if (scenario == BinaryCase)
        {
            File.WriteAllBytes(Path.Combine(game.Installation, ZipGameFixture.TextFile), [0, 13, 10, 255]);
        }
        else
        {
            ZipGameFixture.Write(game.Installation, ZipGameFixture.TextFile, scenario == MixedCase ? ZipGameFixture.TextB : ZipGameFixture.ExtraText);
        }

        var before = Snapshot(game.Installation);
        await Assert.ThrowsAsync<InvalidDataException>(() => game.CreateAdopter().PrepareAsync(game.Installation, game.Target));
        Assert.Equal(before, Snapshot(game.Installation));
        Assert.False(Directory.Exists(game.Staging));
    }

    /// <summary>
    /// Reports all extra-file and directory collisions while preserving the existing ZIP and its user files.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReportsTargetCollisionsWithoutOverwritingThem()
    {
        using var game = new ZipGameFixture(tool);
        await game.InitializeAsync();
        ZipGameFixture.Write(game.Installation, ZipGameFixture.NewFile, ZipGameFixture.ExtraText);
        ZipGameFixture.Write(game.Installation, ParentFile, ZipGameFixture.ExtraText);
        ZipGameFixture.Write(game.Installation, ChildFile, ZipGameFixture.ExtraText);
        ZipGameFixture.Write(game.Installation, UpdateState, ZipGameFixture.ExtraText);
        var before = Snapshot(game.Installation);
        using var prepared = await game.CreateAdopter().PrepareAsync(game.Installation, game.Target);
        Assert.Contains(ZipGameFixture.NewFile, prepared.TargetCollisions);
        Assert.Contains(ParentFile, prepared.TargetCollisions);
        Assert.Contains(ChildFile, prepared.TargetCollisions);
        Assert.Contains(ZipGameFixture.FlattenedFile, prepared.TargetCollisions);
        Assert.Contains(UpdateState, prepared.TargetCollisions);
        Assert.Equal(before, Snapshot(game.Installation));
    }

    /// <summary>
    /// Distinguishes a managed directory transition from actual user files obstructing that transition.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReportsOnlyUnrelatedEntriesWhenTargetReplacesManagedDirectory()
    {
        using var game = new ZipGameFixture(tool);
        await game.InitializeAsync();
        File.Delete(Path.Combine(game.Upstream, ZipGameFixture.TextFile));
        Directory.Delete(Path.Combine(game.Upstream, ManagedDirectory));
        ZipGameFixture.Write(game.Upstream, ManagedDirectory, ZipGameFixture.TextB);
        var target = await game.CommitAsync(ZipGameFixture.TextB);
        using (var prepared = await game.CreateAdopter().PrepareAsync(game.Installation, target))
            Assert.Empty(prepared.TargetCollisions);

        ZipGameFixture.Write(game.Installation, ExtraUnderManagedDirectory, ZipGameFixture.ExtraText);
        using var obstructed = await game.CreateAdopter().PrepareAsync(game.Installation, target);
        Assert.Equal([ExtraUnderManagedDirectory], obstructed.TargetCollisions);
    }

    /// <summary>
    /// Detects changes after preparation instead of promoting metadata against a stale content snapshot.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RevalidationRejectsChangedUserFiles()
    {
        using var game = new ZipGameFixture(tool);
        await game.InitializeAsync();
        using var prepared = await game.CreateAdopter().PrepareAsync(game.Installation, game.Target);
        File.AppendAllText(Path.Combine(game.Installation, ZipGameFixture.UserFile), ZipGameFixture.TextB);
        var before = Snapshot(game.Installation);
        await Assert.ThrowsAsync<IOException>(() => prepared.RevalidateAsync());
        Assert.Equal(before, Snapshot(game.Installation));
        Assert.False(Path.Exists(Path.Combine(game.Installation, GitName)));
    }

    /// <summary>
    /// Rejects an incomplete trusted inventory when fetched Git objects reveal an omitted baseline file.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RequiresCompleteInventoryAndCleansFailedPreparation()
    {
        using var game = new ZipGameFixture(tool);
        await game.InitializeAsync();
        var incomplete = game.Baseline with { Files = game.Baseline.Files.Where(file => file.Path != ZipGameFixture.ObsoleteFile).ToArray() };
        var before = Snapshot(game.Installation);
        await Assert.ThrowsAsync<InvalidDataException>(() => game.CreateAdopter([incomplete]).PrepareAsync(game.Installation, game.Target));
        Assert.Equal(before, Snapshot(game.Installation));
        Assert.Empty(Directory.EnumerateFileSystemEntries(game.Staging));
    }

    /// <summary>
    /// Rejects ambiguous catalogs, existing Git metadata and pre-cancelled work without installation mutations.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RejectsAmbiguityExistingMetadataAndCancellation()
    {
        using var game = new ZipGameFixture(tool);
        await game.InitializeAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => game.CreateAdopter([game.Baseline, game.Baseline]).PrepareAsync(game.Installation, game.Target));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => game.CreateAdopter().PrepareAsync(game.Installation, game.Target, new CancellationToken(true)));
        ZipGameFixture.Write(game.Installation, GitName, ZipGameFixture.ExtraText);
        var before = Snapshot(game.Installation);
        await Assert.ThrowsAsync<InvalidDataException>(() => game.CreateAdopter().PrepareAsync(game.Installation, game.Target));
        Assert.Equal(before, Snapshot(game.Installation));
    }

    /// <summary>
    /// Rejects unavailable target commits and removes private staging after the fetch fails.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task MissingTargetLeavesNoGitAndNoPreparationResidue()
    {
        using var game = new ZipGameFixture(tool);
        await game.InitializeAsync();
        var before = Snapshot(game.Installation);
        await Assert.ThrowsAsync<InvalidDataException>(() => game.CreateAdopter().PrepareAsync(game.Installation, MissingCommit));
        Assert.Equal(before, Snapshot(game.Installation));
        Assert.Empty(Directory.EnumerateFileSystemEntries(game.Staging));
    }

    /// <summary>
    /// Rejects an annotated tag even when it resolves to an otherwise approved commit on the release branch.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RequiresCommitObjectInsteadOfAnnotatedTag()
    {
        using var game = new ZipGameFixture(tool);
        await game.InitializeAsync();
        var tag = await game.CreateAnnotatedTargetAsync();
        var before = Snapshot(game.Installation);
        await Assert.ThrowsAsync<InvalidDataException>(() => game.CreateAdopter().PrepareAsync(game.Installation, tag));
        Assert.Equal(before, Snapshot(game.Installation));
        Assert.Empty(Directory.EnumerateFileSystemEntries(game.Staging));
    }

    /// <summary>
    /// Locates the game by executable and INI identity rather than the installation directory name.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DiscoversGameFromInstalledTrackerAndRejectsInvalidIdentity()
    {
        using var game = new ZipGameFixture(tool);
        await game.InitializeAsync();
        var tracker = Path.Combine(game.Installation, ZipGameFixture.TrackerPath);
        Assert.Equal(game.Installation, GameInstallationLocator.FindFromTracker(tracker));
        ZipGameFixture.Write(game.Installation, GameInstallationLocator.GameIni, WrongTitle);
        Assert.Throws<InvalidDataException>(() => GameInstallationLocator.FindFromTracker(tracker));
        ZipGameFixture.Write(game.Installation, GameInstallationLocator.GameIni, DuplicateTitle);
        Assert.Throws<InvalidDataException>(() => GameInstallationLocator.FindFromTracker(tracker));
    }

    /// <summary>
    /// Loads the release-generated inventory and checks supported paths and required game files across revisions.
    /// </summary>
    [Fact]
    public void LoadsReleaseHoennInventory()
    {
        using var workspace = new TestWorkspace();
        var baseline = HoennGameBaseline.Load();
        ZipGameAdopter.ValidateTreePaths(workspace.Root, baseline.Files.Select(file => file.Path));
        Assert.NotEmpty(baseline.Files);
        Assert.Equal(40, baseline.Commit.Length);
        Assert.True(baseline.Files.Sum(file => file.Canonical.Length) > 0);
        Assert.Equal(baseline.Files.Count, baseline.Files.Select(file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(baseline.Files, file => file.Path == GameInstallationLocator.GameExecutable);
        Assert.Contains(baseline.Files, file => file.Path == GameInstallationLocator.GameIni);
    }

    /// <summary>
    /// Captures every fixture file's exact bytes, including any Git metadata already present.
    /// </summary>
    /// <param name="root">The disposable installation root.</param>
    /// <returns>Sorted relative file paths paired with SHA-256 digests.</returns>
    private static string[] Snapshot(string root)
    {
        var files = Directory.EnumerateFiles(root, Wildcard, SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/') + Colon + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

        return [.. files];
    }
}
