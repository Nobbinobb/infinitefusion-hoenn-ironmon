using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Provides an explicit online check of the production downloader and private HTTPS transport.
/// </summary>
public sealed class GitNetworkTests
{
    private const string Category = "Category";
    private const string Network = "Network";
    private const string CacheName = "network-cache";
    private const string HomeName = "home";
    private const string LsRemote = "ls-remote";
    private const string ExitCode = "--exit-code";
    private const string Upstream = "https://github.com/infinitefusion/infinitefusion-hoenn-public.git";
    private const string ReleaseRef = "refs/heads/releases";
    private const string RefPattern = "^[0-9a-f]{40}\\trefs/heads/releases$";

    /// <summary>
    /// Authenticates the actual pinned download and queries the public game branch using only private Git.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait(Category, Network)]
    public async Task DownloadsPinnedRuntimeAndUsesVerifiedHttps()
    {
        using var workspace = new TestWorkspace();
        using var source = new GitHubArtifactSource();
        var cache = new MinGitCache(workspace.PathFor(CacheName), source);
        var executable = await cache.AcquireAsync();
        var git = new GitProcess(executable, workspace.PathFor(HomeName));
        var result = await git.RunAsync(workspace.Root, [LsRemote, ExitCode, Upstream, ReleaseRef]);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Matches(RefPattern, result.Output.Trim());
    }
}
