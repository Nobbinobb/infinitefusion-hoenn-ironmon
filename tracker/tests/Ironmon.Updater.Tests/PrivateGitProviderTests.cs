using System.Security.Cryptography;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Tests the real private Git against disposable repositories, never the user's game or worktree.
/// </summary>
/// <remarks>
/// Initializes integration tests with a verified private runtime.
/// </remarks>
/// <param name="tool">The real pinned Git fixture.</param>
public sealed class PrivateGitProviderTests(PinnedGitFixture tool) : IClassFixture<PinnedGitFixture>
{
    private const string LocalChanges = "local modifications must remain";
    private const string Show = "show";
    private const string RevParse = "rev-parse";
    private const string Head = "HEAD";
    private const string Colon = ":";
    private const string GitConfig = ".git/config";
    private const string IncludeConfig = "\n[include]\npath = external.config\n";
    private const string FilterConfig = "\n[filter \"custom\"]\nclean = arbitrary-command\n";
    private const string CredentialConfig = "\n[credential]\nhelper = arbitrary-command\n";
    private const string AlternateRemoteConfig = "\n[remote \"other\"]\nurl = https://example.invalid/other.git\n";
    private const string FsMonitorConfig = "\n[core]\nfsmonitor = arbitrary-command\n";
    private const string WorktreeConfig = "\n[core]\nworktree = elsewhere\n";
    private const string StagedCase = "staged";
    private const string HiddenCase = "hidden";
    private const string SparseCase = "sparse";
    private const string Add = "add";
    private const string UpdateIndex = "update-index";
    private const string AssumeUnchanged = "--assume-unchanged";
    private const string SkipWorktree = "--skip-worktree";
    private const string LinkedCase = ".git/commondir";
    private const string ModulesCase = ".gitmodules";
    private const string HooksCase = ".git/hooks/post-checkout";
    private const string AlternatesCase = ".git/objects/info/alternates";
    private const string BranchCommand = "branch";
    private const string Rename = "-m";
    private const string CustomBranch = "custom";
    private const string MissingCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string NestedStage = "staging";
    private const string Wildcard = "*";
    private const string PackDirectory = ".git/objects/pack";
    private const string PackPattern = "*.pack";
    private const string OptionCommit = "--all";
    private const string NewlineCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n";

    /// <summary>
    /// Fetches B while upstream is C, preserving all installed files, index, refs and user changes.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FetchesExactApprovedCommitWithoutChangingInstallation()
    {
        using var game = new GameFixture(tool);
        await game.InitializeAsync();
        await File.WriteAllTextAsync(Path.Combine(game.Installation, GameFixture.GameFile), LocalChanges);
        var before = Snapshot(game.Installation);
        using var prepared = await game.Provider.FetchAsync(game.Installation, game.RevisionB);
        Assert.Equal(game.RevisionB, prepared.Commit);
        Assert.Equal(GameFixture.ContentsB, await game.RunAsync(prepared.ObjectDirectory, Show, game.RevisionB + Colon + GameFixture.GameFile));
        Assert.Equal(game.RevisionA, await game.RunAsync(game.Installation, RevParse, Head));
        Assert.Equal(before, Snapshot(game.Installation));
        Assert.Equal(LocalChanges, await File.ReadAllTextAsync(Path.Combine(game.Installation, GameFixture.GameFile)));
        Assert.True(File.Exists(Path.Combine(game.Installation, GameFixture.ObsoleteFile)));
    }

    /// <summary>
    /// Rejects unsupported settings before they can invoke commands or change an installed repository.
    /// </summary>
    /// <param name="configuration">The unsupported Git configuration text to append to the fixture installation.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(IncludeConfig)]
    [InlineData(FilterConfig)]
    [InlineData(CredentialConfig)]
    [InlineData(AlternateRemoteConfig)]
    [InlineData(FsMonitorConfig)]
    [InlineData(WorktreeConfig)]
    public async Task RejectsUnapprovedRepositoryConfiguration(string configuration)
    {
        using var game = new GameFixture(tool);
        await game.InitializeAsync();
        await File.AppendAllTextAsync(Path.Combine(game.Installation, GitConfig), configuration);
        var before = Snapshot(game.Installation);
        await Assert.ThrowsAsync<InvalidDataException>(() => game.Provider.FetchAsync(game.Installation, game.RevisionB));
        Assert.Equal(before, Snapshot(game.Installation));
        Assert.False(Directory.Exists(game.Staging));
    }

    /// <summary>
    /// Refuses staged changes and sparse/hidden index flags instead of silently discarding them.
    /// </summary>
    /// <param name="state">The staged-change or hidden-index scenario to construct.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(StagedCase)]
    [InlineData(HiddenCase)]
    [InlineData(SparseCase)]
    public async Task RejectsUnsupportedIndexState(string state)
    {
        using var game = new GameFixture(tool);
        await game.InitializeAsync();
        if (state == StagedCase)
        {
            await File.WriteAllTextAsync(Path.Combine(game.Installation, GameFixture.GameFile), LocalChanges);
            await game.RunAsync(game.Installation, Add, GameFixture.GameFile);
        }
        else
        {
            await game.RunAsync(game.Installation, UpdateIndex, state == HiddenCase ? AssumeUnchanged : SkipWorktree, GameFixture.GameFile);
        }

        var before = Snapshot(game.Installation);
        await Assert.ThrowsAsync<InvalidDataException>(() => game.Provider.FetchAsync(game.Installation, game.RevisionB));
        Assert.Equal(before, Snapshot(game.Installation));
    }

    /// <summary>
    /// Rejects linked-worktree, submodule, hook and alternate-object metadata before acquisition or Git invocation.
    /// </summary>
    /// <param name="layout">The unsupported metadata path to create inside the fixture installation.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(LinkedCase)]
    [InlineData(ModulesCase)]
    [InlineData(HooksCase)]
    [InlineData(AlternatesCase)]
    public async Task RejectsUnsupportedLayouts(string layout)
    {
        using var game = new GameFixture(tool);
        await game.InitializeAsync();
        var target = Path.Combine(game.Installation, layout);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, LocalChanges);
        var before = Snapshot(game.Installation);
        await Assert.ThrowsAsync<InvalidDataException>(() => game.Provider.FetchAsync(game.Installation, game.RevisionB));
        Assert.Equal(before, Snapshot(game.Installation));
    }

    /// <summary>
    /// Rejects an unexpected branch even when its commit is otherwise valid.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RejectsCustomBranch()
    {
        using var game = new GameFixture(tool);
        await game.InitializeAsync();
        await game.RunAsync(game.Installation, BranchCommand, Rename, CustomBranch);
        var before = Snapshot(game.Installation);
        await Assert.ThrowsAsync<InvalidDataException>(() => game.Provider.FetchAsync(game.Installation, game.RevisionB));
        Assert.Equal(before, Snapshot(game.Installation));
    }

    /// <summary>
    /// Leaves every installed byte unchanged when the exact commit is unavailable.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task MissingCommitCleansPrivateStaging()
    {
        using var game = new GameFixture(tool);
        await game.InitializeAsync();
        var before = Snapshot(game.Installation);
        await Assert.ThrowsAsync<InvalidDataException>(() => game.Provider.FetchAsync(game.Installation, MissingCommit));
        Assert.Equal(before, Snapshot(game.Installation));
        Assert.Empty(Directory.EnumerateFileSystemEntries(game.Staging));
    }

    /// <summary>
    /// Rejects staging inside the game and pre-cancelled work without installation writes.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RejectsOverlappingStagingAndHonorsCancellation()
    {
        using var game = new GameFixture(tool);
        await game.InitializeAsync();
        var before = Snapshot(game.Installation);
        var provider = new PrivateGitProvider(tool.Cache, game.Policy, Path.Combine(game.Installation, NestedStage));
        await Assert.ThrowsAsync<IOException>(() => provider.FetchAsync(game.Installation, game.RevisionB));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => game.Provider.FetchAsync(game.Installation, game.RevisionB, new CancellationToken(true)));
        Assert.Equal(before, Snapshot(game.Installation));
    }

    /// <summary>
    /// Builds a content snapshot including the Git index and metadata.
    /// </summary>
    /// <param name="root">The fixture installation whose files will be hashed recursively.</param>
    /// <returns>Sorted relative file paths paired with their SHA-256 digests.</returns>
    private static string[] Snapshot(string root)
    {
        var files = Directory.EnumerateFiles(root, Wildcard, SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(root, path) + Colon + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

        return [.. files];
    }

    /// <summary>
    /// Rejects a damaged object database without repairing the user's checkout in place.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RejectsCorruptObjectsWithoutChangingOtherFiles()
    {
        using var game = new GameFixture(tool);
        await game.InitializeAsync();
        var pack = Directory.EnumerateFiles(Path.Combine(game.Installation, PackDirectory), PackPattern).Single();
        var bytes = await File.ReadAllBytesAsync(pack);
        bytes[^1] ^= 0x01;
        File.SetAttributes(pack, FileAttributes.Normal);
        await File.WriteAllBytesAsync(pack, bytes);
        var before = Snapshot(game.Installation);
        await Assert.ThrowsAsync<InvalidDataException>(() => game.Provider.FetchAsync(game.Installation, game.RevisionB));
        Assert.Equal(before, Snapshot(game.Installation));
    }

    /// <summary>
    /// Rejects abbreviated refs, option-like input and trailing newlines before filesystem work.
    /// </summary>
    /// <param name="commit">The malformed commit argument to reject.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(Head)]
    [InlineData(OptionCommit)]
    [InlineData(NewlineCommit)]
    public async Task RejectsNonExactCommitArguments(string commit)
    {
        using var game = new GameFixture(tool);
        await Assert.ThrowsAsync<ArgumentException>(() => game.Provider.FetchAsync(game.Installation, commit));
        Assert.False(Directory.Exists(game.Staging));
    }
}
