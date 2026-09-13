using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises cache authentication, recovery and hostile ZIP boundaries without executing fixture bytes.
/// </summary>
public sealed class MinGitCacheTests
{
    private const string CacheName = "cache";
    private const string ArchiveName = "package.zip";
    private const string Damaged = "damaged";
    private const string InjectedName = "injected.exe";
    private const string Stages = "stage-*";
    private const string RuntimeName = "runtime";
    private const string Traversal = "../escape.exe";
    private const string Absolute = "C:/escape.exe";
    private const string DeviceName = "NUL.exe";
    private const string AlternateStream = "cmd/git.exe:payload";
    private const string Duplicate = "CMD/GIT.EXE";
    private const string InsecureSource = "http://github.com/fixture.zip";
    private const string UnknownSource = "https://example.com/fixture.zip";
    private const string CredentialSource = "https://user:secret@github.com/fixture.zip";

    /// <summary>
    /// Reuses a verified cache, repairs extracted damage and preserves licensing.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReusesAndRepairsTheAuthenticatedRuntime()
    {
        using var workspace = new TestWorkspace();
        var (bytes, package) = TestArchives.Create();
        var source = new MemoryArtifact(bytes);
        var cache = new MinGitCache(workspace.PathFor(CacheName), source, package, TimeProvider.System);
        var executable = await cache.AcquireAsync();
        Assert.Equal(TestArchives.FakeExecutable, await File.ReadAllTextAsync(executable));
        Assert.Equal(executable, await cache.AcquireAsync());
        Assert.Equal(1, source.Calls);
        await File.WriteAllTextAsync(executable, Damaged);
        await cache.AcquireAsync();
        Assert.Equal(TestArchives.FakeExecutable, await File.ReadAllTextAsync(executable));
        var runtime = Path.Combine(workspace.PathFor(CacheName), RuntimeName);
        await File.WriteAllTextAsync(Path.Combine(runtime, InjectedName), Damaged);
        await cache.AcquireAsync();
        Assert.False(File.Exists(Path.Combine(runtime, InjectedName)));
        Assert.Equal(TestArchives.FakeLicense, await File.ReadAllTextAsync(Path.Combine(runtime, MinGitPackage.License)));
        Assert.Equal(1, source.Calls);
    }

    /// <summary>
    /// Redownloads a damaged compressed cache before trusting extracted files.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CorruptArchiveIsReacquired()
    {
        using var workspace = new TestWorkspace();
        var (bytes, package) = TestArchives.Create();
        var source = new MemoryArtifact(bytes);
        var cache = new MinGitCache(workspace.PathFor(CacheName), source, package, TimeProvider.System);
        await cache.AcquireAsync();
        await File.WriteAllTextAsync(Path.Combine(workspace.PathFor(CacheName), ArchiveName), Damaged);
        await cache.AcquireAsync();
        Assert.Equal(2, source.Calls);
    }

    /// <summary>
    /// Rejects a same-sized altered download and removes partial staging.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task IncorrectHashNeverPublishesRuntime()
    {
        using var workspace = new TestWorkspace();
        var (bytes, package) = TestArchives.Create();
        bytes[^1] ^= 0x01;
        var root = workspace.PathFor(CacheName);
        var cache = new MinGitCache(root, new MemoryArtifact(bytes), package, TimeProvider.System);
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.AcquireAsync());
        Assert.False(Directory.Exists(Path.Combine(root, RuntimeName)));
        Assert.Empty(Directory.EnumerateDirectories(root, Stages));
    }

    /// <summary>
    /// Cancels an incomplete acquisition without promoting its bytes.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CancellationCleansPartialAcquisition()
    {
        using var workspace = new TestWorkspace();
        using var cancel = new CancellationTokenSource();
        var (bytes, package) = TestArchives.Create();
        var source = new MemoryArtifact(bytes) { DuringDownload = cancel.Cancel };
        var root = workspace.PathFor(CacheName);
        var cache = new MinGitCache(root, source, package, TimeProvider.System);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.AcquireAsync(cancel.Token));
        Assert.False(Directory.Exists(Path.Combine(root, RuntimeName)));
        Assert.Empty(Directory.EnumerateDirectories(root, Stages));
    }

    /// <summary>
    /// Serializes simultaneous acquisitions so they publish one complete runtime.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ConcurrentAcquisitionUsesOneDownload()
    {
        using var workspace = new TestWorkspace();
        var (bytes, package) = TestArchives.Create();
        var source = new MemoryArtifact(bytes);
        var cache = new MinGitCache(workspace.PathFor(CacheName), source, package, TimeProvider.System);
        var results = await Task.WhenAll(cache.AcquireAsync(), cache.AcquireAsync());
        Assert.Equal(results[0], results[1]);
        Assert.Equal(1, source.Calls);
    }

    /// <summary>
    /// Rejects paths that could escape staging or collide on Windows.
    /// </summary>
    /// <param name="entry">The unsafe or colliding entry name to include in a correctly hashed archive.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(Traversal)]
    [InlineData(Absolute)]
    [InlineData(DeviceName)]
    [InlineData(AlternateStream)]
    [InlineData(Duplicate)]
    public async Task RejectsUnsafeArchivePaths(string entry)
    {
        using var workspace = new TestWorkspace();
        var (bytes, package) = TestArchives.Create(entry);
        var root = workspace.PathFor(CacheName);
        var cache = new MinGitCache(root, new MemoryArtifact(bytes), package, TimeProvider.System);
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.AcquireAsync());
        Assert.False(Directory.Exists(Path.Combine(root, RuntimeName)));
        Assert.Empty(Directory.EnumerateDirectories(root, Stages));
    }

    /// <summary>
    /// Rejects unsafe sources before network access.
    /// </summary>
    /// <param name="uri">The disallowed source address to pass to the production downloader.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(InsecureSource)]
    [InlineData(UnknownSource)]
    [InlineData(CredentialSource)]
    public async Task RejectsUnapprovedDownloadSources(string uri)
    {
        using var source = new GitHubArtifactSource();
        using var destination = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => source.CopyToAsync(new Uri(uri), destination, 100, CancellationToken.None));
        Assert.Empty(destination.ToArray());
    }
}
