using System.Collections.Concurrent;
using System.Net;
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
    /// Defines the expected game-owned destination for downloaded custom sheets.
    /// </summary>
    private const string SheetFolderRelativePath = "Graphics/CustomBattlers/spritesheets/spritesheets_custom";

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
    /// Verifies duplicate manifest entries collapse to one sheet and valid local sheets are skipped.
    /// </summary>
    [Fact]
    public void PlanDeduplicatesSheetsAndSkipsValidLocalFiles()
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
        Assert.Equal(1, plan.PendingSheetCount);
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
        Assert.Equal(0, result.FailedSheetCount);
        Assert.Equal(_png.Length * 2, result.DownloadedBytes);
        Assert.Equal(["1/1.png", "1/1a.png"], handler.Paths.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(_png, File.ReadAllBytes(Path.Combine(_root, SheetFolderRelativePath, "1", "1.png")));
        Assert.Equal(_png, File.ReadAllBytes(Path.Combine(_root, SheetFolderRelativePath, "1", "1a.png")));
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
    }

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
