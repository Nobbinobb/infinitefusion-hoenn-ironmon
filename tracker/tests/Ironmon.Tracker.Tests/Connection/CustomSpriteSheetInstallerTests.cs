using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Ironmon.Tracker.Connection.Sprites;

namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies resumable installation of Infinite Fusion custom sprite sheets.
/// </summary>
public sealed partial class CustomSpriteSheetInstallerTests : IDisposable
{
    /// <summary>
    /// Defines the executable marker required by installation-root validation.
    /// </summary>
    private const string GameExecutableName = "InfiniteFusion2.exe";

    /// <summary>
    /// Defines the manifest location populated by each isolated test installation.
    /// </summary>
    private const string ManifestRelativePath = "Data/sprites/CUSTOM_SPRITES";

    /// <summary>
    /// Defines the normal-species sprite manifest required by installation-root validation.
    /// </summary>
    private const string BaseManifestRelativePath = "Data/sprites/BASE_SPRITES";

    /// <summary>
    /// Defines the expected game-owned destination for downloaded custom sheets.
    /// </summary>
    private const string SheetFolderRelativePath = "Graphics/CustomBattlers/spritesheets/spritesheets_custom";

    /// <summary>
    /// Defines the expected game-owned destination for normal-species sprite sheets.
    /// </summary>
    private const string BaseSheetFolderRelativePath = "Graphics/CustomBattlers/spritesheets/spritesheets_base";

    /// <summary>
    /// Provides duplicate body entries, one alternate sheet, and one malformed line.
    /// </summary>
    private const string ManifestContents = "1.2.png\n1.3.png\n1.4a.png\ninvalid.png\n";

    /// <summary>
    /// Provides a compact PNG-signature fixture used for local validation and HTTP responses.
    /// </summary>
    private static readonly byte[] _png = [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4];

    /// <summary>
    /// Stores the unique temporary Infinite Fusion root owned by the current test instance.
    /// </summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "IronmonTrackerTests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Verifies duplicate manifest entries collapse to one sheet and valid local sheets remain eligible for synchronization.
    /// </summary>
    [Fact]
    public void PlanDeduplicatesSheetsAndIncludesValidLocalFiles()
    {
        PrepareInstallation();
        string existingPath = Path.Combine(_root, SheetFolderRelativePath, "1", "1.png");
        Directory.CreateDirectory(Path.GetDirectoryName(existingPath)!);
        File.WriteAllBytes(existingPath, _png);
        using HttpClient client = new(new StaticResponseHandler(_png));
        using CustomSpriteSheetInstaller installer = new(client, static _ => false);

        CustomSpriteInstallPlan plan = installer.CreatePlan(_root);

        Assert.Equal(2, plan.TotalSheetCount);
        Assert.Equal(1, plan.ExistingSheetCount);
        Assert.Equal(2, plan.PendingSheetCount);
    }

    /// <summary>
    /// Verifies missing sheets download into the exact game-owned spritesheet layout.
    /// </summary>
    /// <returns>A task representing the asynchronous installation assertion.</returns>
    [Fact]
    public async Task InstallDownloadsMissingSheetsToGameLayout()
    {
        PrepareInstallation();
        StaticResponseHandler handler = new(_png);
        using HttpClient client = new(handler);
        using CustomSpriteSheetInstaller installer = new(client, static _ => false, parallelDownloads: 2);
        CustomSpriteInstallPlan plan = installer.CreatePlan(_root);

        CustomSpriteInstallResult result = await installer.InstallAsync(plan);

        Assert.Equal(2, result.DownloadedSheetCount);
        Assert.Equal(0, result.UnchangedSheetCount);
        Assert.Equal(0, result.FailedSheetCount);
        Assert.Equal(_png.Length * 2, result.DownloadedBytes);
        Assert.Equal(["1/1.png", "1/1a.png"], handler.Paths.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(_png, File.ReadAllBytes(Path.Combine(_root, SheetFolderRelativePath, "1", "1.png")));
        Assert.Equal(_png, File.ReadAllBytes(Path.Combine(_root, SheetFolderRelativePath, "1", "1a.png")));
    }

    /// <summary>
    /// Verifies normal-species manifest variants collapse to one base sheet and replacing it clears every derived variant cache.
    /// </summary>
    /// <returns>A task representing the base-sheet synchronization assertion.</returns>
    [Fact]
    public async Task SynchronizationIncludesBaseSpeciesSheetsAndInvalidatesAllVariantCaches()
    {
        PrepareInstallation();
        File.WriteAllText(Path.Combine(_root, ManifestRelativePath), string.Empty);
        File.WriteAllText(Path.Combine(_root, BaseManifestRelativePath), "303.png\n303s.png\ninvalid.png\n");
        string baseSheetPath = Path.Combine(_root, BaseSheetFolderRelativePath, "303.png");
        Directory.CreateDirectory(Path.GetDirectoryName(baseSheetPath)!);
        File.WriteAllBytes(baseSheetPath, _png);
        string trackerCacheRoot = Path.Combine(_root, "Graphics", "CustomBattlers", "local_sprites", "IronmonTracker");
        Directory.CreateDirectory(trackerCacheRoot);
        string mainCache = Path.Combine(trackerCacheRoot, "base-303-0-main.png");
        string alternateCache = Path.Combine(trackerCacheRoot, "base-303-0-s.png");
        string unrelatedCache = Path.Combine(trackerCacheRoot, "base-304-0-s.png");
        File.WriteAllBytes(mainCache, _png);
        File.WriteAllBytes(alternateCache, _png);
        File.WriteAllBytes(unrelatedCache, _png);
        byte[] changedPng = [.. _png, 5, 6, 7];
        ConcurrentBag<string> requests = [];
        using HttpClient client = new(new DelegateResponseHandler((request, _) =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(changedPng) });
        }));
        using CustomSpriteSheetInstaller installer = new(client, static _ => false);

        CustomSpriteInstallPlan plan = installer.CreatePlan(_root);
        CustomSpriteInstallResult result = await installer.InstallAsync(plan);

        Assert.Equal(1, plan.TotalSheetCount);
        Assert.Equal(1, plan.ExistingSheetCount);
        Assert.Equal(1, result.DownloadedSheetCount);
        Assert.Equal("https://infinitefusion.net/customsprites/spritesheets/spritesheets_base/303.png", Assert.Single(requests));
        Assert.Equal(changedPng, File.ReadAllBytes(baseSheetPath));
        Assert.False(File.Exists(mainCache));
        Assert.False(File.Exists(alternateCache));
        Assert.True(File.Exists(unrelatedCache));
    }

    /// <summary>
    /// Verifies a legacy sheet is conditionally refreshed, its derived caches are removed, and later checks use the persisted entity tag.
    /// </summary>
    /// <returns>A task representing both synchronization passes.</returns>
    [Fact]
    public async Task SynchronizationRefreshesChangedLegacySheetsAndThenUsesEntityTags()
    {
        PrepareInstallation();
        string mainSheetPath = Path.Combine(_root, SheetFolderRelativePath, "1", "1.png");
        Directory.CreateDirectory(Path.GetDirectoryName(mainSheetPath)!);
        File.WriteAllBytes(mainSheetPath, _png);
        DateTime legacyWriteTime = new(2026, 8, 25, 17, 32, 18, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(mainSheetPath, legacyWriteTime);
        string trackerCacheRoot = Path.Combine(_root, "Graphics", "CustomBattlers", "local_sprites", "IronmonTracker");
        Directory.CreateDirectory(trackerCacheRoot);
        string mainCache = Path.Combine(trackerCacheRoot, "custom-1-99-main.png");
        string alternateCache = Path.Combine(trackerCacheRoot, "custom-1-99-a.png");
        File.WriteAllBytes(mainCache, _png);
        File.WriteAllBytes(alternateCache, _png);
        byte[] changedPng = [.. _png, 5, 6, 7];
        DateTimeOffset serverModified = new(2026, 8, 31, 23, 58, 36, TimeSpan.Zero);
        ConcurrentBag<(string Path, DateTimeOffset? IfModifiedSince, string[] EntityTags)> firstRequests = [];
        using HttpClient firstClient = new(new DelegateResponseHandler((request, _) =>
        {
            firstRequests.Add(CaptureConditionalHeaders(request));
            HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new ByteArrayContent(changedPng) };
            response.Headers.ETag = new EntityTagHeaderValue("\"sheet-v2\"");
            response.Content.Headers.LastModified = serverModified;
            return Task.FromResult(response);
        }));

        using CustomSpriteSheetInstaller firstInstaller = new(firstClient, static _ => false);
        CustomSpriteInstallResult firstResult = await firstInstaller.InstallAsync(firstInstaller.CreatePlan(_root));

        Assert.Equal(2, firstResult.DownloadedSheetCount);
        Assert.Equal(0, firstResult.UnchangedSheetCount);
        (string Path, DateTimeOffset? IfModifiedSince, string[] EntityTags) legacyRequest = Assert.Single(firstRequests, request => request.Path.EndsWith("/1/1.png", StringComparison.Ordinal));
        Assert.Equal(new DateTimeOffset(legacyWriteTime), legacyRequest.IfModifiedSince);
        Assert.Empty(legacyRequest.EntityTags);
        Assert.False(File.Exists(mainCache));
        Assert.False(File.Exists(alternateCache));
        Assert.Equal(changedPng, File.ReadAllBytes(mainSheetPath));
        Assert.True(File.Exists(Path.Combine(_root, CustomSpriteSheetSyncStore.RelativePath)));

        ConcurrentBag<(string Path, DateTimeOffset? IfModifiedSince, string[] EntityTags)> secondRequests = [];
        using HttpClient secondClient = new(new DelegateResponseHandler((request, _) =>
        {
            secondRequests.Add(CaptureConditionalHeaders(request));
            HttpResponseMessage response = new(HttpStatusCode.NotModified);
            response.Headers.ETag = new EntityTagHeaderValue("\"sheet-v2\"");
            return Task.FromResult(response);
        }));

        using CustomSpriteSheetInstaller secondInstaller = new(secondClient, static _ => false);
        CustomSpriteInstallResult secondResult = await secondInstaller.InstallAsync(secondInstaller.CreatePlan(_root));

        Assert.Equal(0, secondResult.DownloadedSheetCount);
        Assert.Equal(2, secondResult.UnchangedSheetCount);
        Assert.All(secondRequests, request => Assert.Equal("\"sheet-v2\"", Assert.Single(request.EntityTags)));
    }

    /// <summary>
    /// Verifies a misleading legacy timestamp cannot preserve a sheet whose server length proves it changed.
    /// </summary>
    /// <returns>A task representing the guarded conditional synchronization.</returns>
    [Fact]
    public async Task SynchronizationRetriesWithoutConditionsWhenNotModifiedLengthDiffers()
    {
        PrepareInstallation();
        File.WriteAllText(Path.Combine(_root, ManifestRelativePath), "1.2.png\n");
        string sheetPath = Path.Combine(_root, SheetFolderRelativePath, "1", "1.png");
        Directory.CreateDirectory(Path.GetDirectoryName(sheetPath)!);
        File.WriteAllBytes(sheetPath, _png);
        File.SetLastWriteTimeUtc(sheetPath, DateTime.UtcNow.AddDays(1));
        byte[] changedPng = [.. _png, 8, 9];
        ConcurrentBag<(string Path, DateTimeOffset? IfModifiedSince, string[] EntityTags)> requests = [];
        int requestCount = 0;
        using HttpClient client = new(new DelegateResponseHandler((request, _) =>
        {
            requests.Add(CaptureConditionalHeaders(request));
            if (Interlocked.Increment(ref requestCount) == 1)
            {
                HttpResponseMessage unchanged = new(HttpStatusCode.NotModified) { Content = new ByteArrayContent([]) };
                unchanged.Content.Headers.ContentLength = changedPng.Length;
                return Task.FromResult(unchanged);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(changedPng) });
        }));

        using CustomSpriteSheetInstaller installer = new(client, static _ => false);
        CustomSpriteInstallResult result = await installer.InstallAsync(installer.CreatePlan(_root));

        Assert.Equal(1, result.DownloadedSheetCount);
        Assert.Equal(changedPng, File.ReadAllBytes(sheetPath));
        Assert.Contains(requests, request => request.IfModifiedSince is not null);
        Assert.Contains(requests, request => request.IfModifiedSince is null && request.EntityTags.Length == 0);
    }

    /// <summary>
    /// Verifies the downloader refuses to write while the represented game installation is running.
    /// </summary>
    /// <returns>A task representing the asynchronous rejection assertion.</returns>
    [Fact]
    public async Task InstallRejectsRunningGame()
    {
        PrepareInstallation();
        using HttpClient client = new(new StaticResponseHandler(_png));
        using CustomSpriteSheetInstaller installer = new(client, static _ => true);
        CustomSpriteInstallPlan plan = installer.CreatePlan(_root);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(plan));

        Assert.Contains("Close Infinite Fusion", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies starting Infinite Fusion during an active request cancels transport work and reports the game guard.
    /// </summary>
    /// <returns>A task representing the asynchronous process-monitor assertion.</returns>
    [Fact]
    public async Task InstallStopsWhenGameStartsDuringDownload()
    {
        PrepareInstallation();
        int gameRunning = 0;
        BlockingResponseHandler handler = new();
        using HttpClient client = new(handler);
        using CustomSpriteSheetInstaller installer = new(client, _ => Volatile.Read(ref gameRunning) != 0, parallelDownloads: 2);
        CustomSpriteInstallPlan plan = installer.CreatePlan(_root);
        Task<CustomSpriteInstallResult> installation = installer.InstallAsync(plan);
        await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Volatile.Write(ref gameRunning, 1);
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => installation.WaitAsync(TimeSpan.FromSeconds(3)));

        Assert.Contains("Close Infinite Fusion", exception.Message, StringComparison.Ordinal);
        string sheetRoot = Path.Combine(_root, SheetFolderRelativePath);
        Assert.Empty(Directory.EnumerateFiles(sheetRoot, "*", SearchOption.AllDirectories));
    }

    /// <summary>
    /// Creates the minimum executable and manifest structure accepted as an Infinite Fusion installation.
    /// </summary>
    private void PrepareInstallation()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Data", "sprites"));
        File.WriteAllText(Path.Combine(_root, GameExecutableName), string.Empty);
        File.WriteAllText(Path.Combine(_root, ManifestRelativePath), ManifestContents);
        File.WriteAllText(Path.Combine(_root, BaseManifestRelativePath), string.Empty);
    }

    /// <summary>
    /// Copies the request URI and conditional headers before the production request is disposed.
    /// </summary>
    /// <param name="request">The live request being captured.</param>
    /// <returns>The endpoint path, modification time, and entity tags.</returns>
    private static (string Path, DateTimeOffset? IfModifiedSince, string[] EntityTags) CaptureConditionalHeaders(HttpRequestMessage request)
        => (request.RequestUri!.AbsolutePath, request.Headers.IfModifiedSince, [.. request.Headers.IfNoneMatch.Select(static value => value.ToString())]);

    /// <summary>
    /// Removes the isolated Infinite Fusion installation created for the current test instance.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    /// <summary>
    /// Returns deterministic PNG content while recording endpoint-relative request paths.
    /// </summary>
    /// <param name="content">The response bytes returned for every request.</param>
    private sealed class StaticResponseHandler(byte[] content) : HttpMessageHandler
    {
        /// <summary>
        /// Defines the endpoint prefix removed from captured absolute request paths.
        /// </summary>
        private const string BasePath = "/customsprites/spritesheets/spritesheets_custom/";

        /// <summary>
        /// Stores the immutable response payload supplied by the owning test.
        /// </summary>
        private readonly byte[] _content = content;

        /// <summary>
        /// Captures concurrent request paths without making the fake handler serialize calls.
        /// </summary>
        private readonly ConcurrentBag<string> _paths = [];

        /// <summary>
        /// Gets the endpoint-relative paths requested by the installer.
        /// </summary>
        internal IReadOnlyCollection<string> Paths => _paths;

        /// <summary>
        /// Records the requested sprite-sheet path and returns the configured successful response.
        /// </summary>
        /// <param name="request">The HTTP request issued by the installer.</param>
        /// <param name="cancellationToken">The token supplied for the request operation.</param>
        /// <returns>A completed task containing the deterministic PNG response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            _paths.Add(path[BasePath.Length..]);
            HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new ByteArrayContent(_content) };
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Keeps requests active until the installer cancels them after detecting the game process.
    /// </summary>
    private sealed class BlockingResponseHandler : HttpMessageHandler
    {
        /// <summary>
        /// Gets the signal completed when at least one request reaches the fake transport.
        /// </summary>
        internal TaskCompletionSource RequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Signals request arrival and waits indefinitely for the supplied cancellation token.
        /// </summary>
        /// <param name="request">The HTTP request issued by the installer.</param>
        /// <param name="cancellationToken">The token cancelled when Infinite Fusion starts.</param>
        /// <returns>A task that completes only if the request is not cancelled.</returns>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_png) };
        }
    }
}
