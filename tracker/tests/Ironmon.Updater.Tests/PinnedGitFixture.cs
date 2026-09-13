using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Acquires the actual pinned Git once for isolated integration fixtures.
/// </summary>
public sealed class PinnedGitFixture : IAsyncLifetime
{
    private readonly TestWorkspace _workspace = new();
    private const string CacheName = "private-git";
    private const string AssetRelativePath = "../../test-assets/MinGit.zip";

    /// <summary>
    /// Gets the verified private cache.
    /// </summary>
    internal MinGitCache Cache { get; private set; } = null!;

    /// <summary>
    /// Gets the actual private executable.
    /// </summary>
    internal string Executable { get; private set; } = string.Empty;

    /// <summary>
    /// Verifies the provisioned official archive and acquires its private runtime for this fixture.
    /// </summary>
    /// <returns>A task that completes when the cache and executable properties are ready.</returns>
    public async Task InitializeAsync()
    {
        var archive = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, AssetRelativePath));
        if (!File.Exists(archive))
            throw new FileNotFoundException("Run tools/Test-Updater.ps1 to provision the pinned integration-test archive.", archive);

        Cache = new MinGitCache(_workspace.PathFor(CacheName), new FileArtifact(archive));
        Executable = await Cache.AcquireAsync();
    }

    /// <summary>
    /// Removes the fixture workspace and its private runtime.
    /// </summary>
    /// <returns>A completed task after synchronous cleanup succeeds.</returns>
    public Task DisposeAsync()
    {
        _workspace.Dispose();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Builds a disposable A/B/C upstream and an installation left at A.
/// </summary>
internal sealed class GameFixture : IDisposable
{
    private readonly TestWorkspace _workspace = new();
    private readonly PinnedGitFixture _tool;

    internal const string Branch = "releases";
    internal const string GameFile = "Game.txt";
    internal const string ObsoleteFile = "obsolete.txt";
    internal const string UserFile = "personal-save.txt";
    internal const string UserContents = "personal fixture data";
    internal const string ContentsA = "game version A";
    internal const string ContentsB = "game version B";
    internal const string ContentsC = "game version C";
    private const string InstallationName = "installed game";
    private const string UpstreamName = "upstream";
    private const string StagingName = "staging";
    private const string HomeName = "empty-home";
    private const string Init = "init";
    private const string InitialBranch = "--initial-branch=releases";
    private const string EmptyTemplate = "--template=";
    private const string Clone = "clone";
    private const string NoLocal = "--no-local";
    private const string BranchOption = "--branch";
    private const string Add = "add";
    private const string All = "--all";
    private const string ConfigOption = "-c";
    private const string AuthorName = "user.name=Updater Fixture";
    private const string AuthorEmail = "user.email=fixture@example.invalid";
    private const string Commit = "commit";
    private const string MessageOption = "-m";
    private const string RevParse = "rev-parse";
    private const string Head = "HEAD";
    private const string ConfigPath = ".git/config";

    /// <summary>
    /// Gets the installed ordinary checkout.
    /// </summary>
    internal string Installation => _workspace.PathFor(InstallationName);

    /// <summary>
    /// Gets the local upstream.
    /// </summary>
    internal string Upstream => _workspace.PathFor(UpstreamName);

    /// <summary>
    /// Gets the private object staging directory.
    /// </summary>
    internal string Staging => _workspace.PathFor(StagingName);

    /// <summary>
    /// Gets the isolated Git process runner.
    /// </summary>
    internal GitProcess Git { get; }

    /// <summary>
    /// Gets the installed revision.
    /// </summary>
    internal string RevisionA { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the approved middle revision.
    /// </summary>
    internal string RevisionB { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the newer upstream tip.
    /// </summary>
    internal string RevisionC { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a local-transport policy confined to the fixture.
    /// </summary>
    internal RepositoryPolicy Policy => new(Upstream.Replace('\\', '/'), Branch, true);

    /// <summary>
    /// Constructs the provider using the same pinned cache as production.
    /// </summary>
    internal PrivateGitProvider Provider => new(_tool.Cache, Policy, Staging);

    /// <summary>
    /// Creates the disposable working directories and runner.
    /// </summary>
    /// <param name="tool">The initialized fixture providing the verified Git executable and shared cache.</param>
    internal GameFixture(PinnedGitFixture tool)
    {
        _tool = tool;
        Git = new GitProcess(tool.Executable, _workspace.PathFor(HomeName), localFixtures: true);
    }

    /// <summary>
    /// Builds revisions with changed and obsolete files; does not use system Git.
    /// </summary>
    /// <returns>A task that completes with the upstream at C, the installation at A, and B recorded as the approved revision.</returns>
    internal async Task InitializeAsync()
    {
        await RunAsync(_workspace.Root, Init, InitialBranch, EmptyTemplate, Upstream);
        await File.WriteAllTextAsync(Path.Combine(Upstream, GameFile), ContentsA);
        await File.WriteAllTextAsync(Path.Combine(Upstream, ObsoleteFile), ContentsA);
        RevisionA = await CommitAsync(ContentsA);
        await RunAsync(_workspace.Root, Clone, EmptyTemplate, NoLocal, BranchOption, Branch, Upstream.Replace('\\', '/'), Installation);
        Policy.ValidateConfiguration(Path.Combine(Installation, ConfigPath));
        await File.WriteAllTextAsync(Path.Combine(Upstream, GameFile), ContentsB);
        File.Delete(Path.Combine(Upstream, ObsoleteFile));
        RevisionB = await CommitAsync(ContentsB);
        await File.WriteAllTextAsync(Path.Combine(Upstream, GameFile), ContentsC);
        RevisionC = await CommitAsync(ContentsC);
        await File.WriteAllTextAsync(Path.Combine(Installation, UserFile), UserContents);
    }

    /// <summary>
    /// Runs an explicit fixture command and includes its bounded diagnostics on assertion failure.
    /// </summary>
    /// <param name="directory">The fixture working directory.</param>
    /// <param name="arguments">The structured Git arguments.</param>
    /// <returns>A task whose result is trimmed standard output after a successful exit-code assertion.</returns>
    internal async Task<string> RunAsync(string directory, params string[] arguments)
    {
        var result = await Git.RunAsync(directory, arguments);
        Assert.True(result.ExitCode == 0, result.Error);
        return result.Output.Trim();
    }

    /// <summary>
    /// Commits test-controlled content with a per-command identity.
    /// </summary>
    /// <param name="message">The commit message for the fixture revision.</param>
    /// <returns>A task whose result is the newly created upstream commit ID.</returns>
    private async Task<string> CommitAsync(string message)
    {
        await RunAsync(Upstream, Add, All);
        await RunAsync(Upstream, ConfigOption, AuthorName, ConfigOption, AuthorEmail, Commit, MessageOption, message);
        return await RunAsync(Upstream, RevParse, Head);
    }

    /// <summary>
    /// Removes the disposable upstream, installation and staging workspace.
    /// </summary>
    public void Dispose()
        => _workspace.Dispose();
}
