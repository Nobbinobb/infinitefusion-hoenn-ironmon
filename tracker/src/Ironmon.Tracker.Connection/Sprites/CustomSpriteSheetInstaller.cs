using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace Ironmon.Tracker.Connection.Sprites;

/// <summary>
/// Installs missing custom fusion sprite sheets without changing Infinite Fusion's download settings.
/// </summary>
public sealed partial class CustomSpriteSheetInstaller : IDisposable
{
    /// <summary>
    /// Defines the number of sheet requests allowed to run concurrently.
    /// This bounds network and disk pressure without imposing a requests-per-minute limit.
    /// </summary>
    private const int DefaultParallelDownloads = 4;

    /// <summary>
    /// Defines the total attempts allowed for a response that reports a retryable HTTP status.
    /// </summary>
    private const int MaximumDownloadAttempts = 4;

    /// <summary>
    /// Defines the asynchronous stream-copy buffer size in bytes.
    /// </summary>
    private const int CopyBufferSize = 81920;

    /// <summary>
    /// Defines how far installation discovery may walk above the tracker executable or working directory.
    /// </summary>
    private const int MaximumParentSearchDepth = 12;

    /// <summary>
    /// Defines how frequently an active installation checks whether Infinite Fusion has started.
    /// </summary>
    private static readonly TimeSpan GameProcessPollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Defines the executable required to identify an Infinite Fusion 2 installation.
    /// </summary>
    private const string GameExecutableName = "InfiniteFusion2.exe";

    /// <summary>
    /// Defines the Windows process name used to prevent writes while the game is running.
    /// </summary>
    private const string GameProcessName = "InfiniteFusion2";

    /// <summary>
    /// Defines the game-relative custom-sprite manifest used to derive distinct sheets.
    /// </summary>
    private const string CustomSpriteManifestRelativePath = "Data/sprites/CUSTOM_SPRITES";

    /// <summary>
    /// Defines the game-owned destination directory consumed by <c>CustomSpriteExtracter</c>.
    /// </summary>
    private const string CustomSpriteSheetFolderRelativePath = "Graphics/CustomBattlers/spritesheets/spritesheets_custom";

    /// <summary>
    /// Defines the suffix used for incomplete files so cancellation never exposes a partial PNG as installed.
    /// </summary>
    private const string PartialDownloadSuffix = ".ironmon-download";

    /// <summary>
    /// Defines the error raised when neither an explicit nor adjacent game installation can be validated.
    /// </summary>
    private const string GameRootNotFoundMessage = "The Infinite Fusion installation directory could not be found.";

    /// <summary>
    /// Defines the error raised when a candidate installation lacks its custom-sprite manifest.
    /// </summary>
    private const string InvalidManifestMessage = "The Infinite Fusion custom sprite manifest could not be found.";

    /// <summary>
    /// Defines the error raised before any write when the represented game process is active.
    /// </summary>
    private const string GameRunningMessage = "Close Infinite Fusion before downloading the custom sprite library.";

    /// <summary>
    /// Defines the error raised when a completed response does not begin with the PNG signature.
    /// </summary>
    private const string InvalidSpriteSheetMessage = "The downloaded file is not a valid PNG sprite sheet.";

    /// <summary>
    /// Defines the invariant HTTP failure message populated with the numeric response status.
    /// </summary>
    private const string RequestFailureMessage = "The sprite sheet request failed with status {0}.";

    /// <summary>
    /// Identifies Infinite Fusion's official true-size custom fusion spritesheet endpoint.
    /// </summary>
    private static readonly Uri CustomSpriteSheetBaseUri = new("https://infinitefusion.net/customsprites/spritesheets/spritesheets_custom/", UriKind.Absolute);

    /// <summary>
    /// Contains the eight-byte signature required at the beginning of every installed PNG.
    /// </summary>
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>
    /// Sends streaming sheet requests and is injectable for deterministic transport tests.
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Determines whether the target installation is currently in use by Infinite Fusion.
    /// </summary>
    private readonly Func<string, bool> _gameRunning;

    /// <summary>
    /// Stores the validated maximum number of concurrent sheet operations.
    /// </summary>
    private readonly int _parallelDownloads;

    /// <summary>
    /// Records whether this instance created and therefore must dispose its HTTP client.
    /// </summary>
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Initializes a production custom sprite-sheet installer.
    /// </summary>
    public CustomSpriteSheetInstaller()
        : this(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }, IsInfiniteFusionRunning, DefaultParallelDownloads, true)
    {
    }

    /// <summary>
    /// Initializes an installer with controlled transport and process detection dependencies.
    /// </summary>
    /// <param name="httpClient">The client that returns sprite-sheet responses.</param>
    /// <param name="gameRunning">The target-installation process detector.</param>
    /// <param name="parallelDownloads">The maximum number of concurrent sheet operations.</param>
    internal CustomSpriteSheetInstaller(HttpClient httpClient, Func<string, bool> gameRunning, int parallelDownloads = DefaultParallelDownloads)
        : this(httpClient, gameRunning, parallelDownloads, false)
    {
    }

    /// <summary>
    /// Applies shared dependency validation and ownership semantics for production and tests.
    /// </summary>
    /// <param name="httpClient">The client that returns sprite-sheet responses.</param>
    /// <param name="gameRunning">The target-installation process detector.</param>
    /// <param name="parallelDownloads">The maximum number of concurrent sheet operations.</param>
    /// <param name="ownsHttpClient">Whether disposal of this installer also disposes the client.</param>
    private CustomSpriteSheetInstaller(HttpClient httpClient, Func<string, bool> gameRunning, int parallelDownloads, bool ownsHttpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(gameRunning);
        ArgumentOutOfRangeException.ThrowIfLessThan(parallelDownloads, 1);
        _httpClient = httpClient;
        _gameRunning = gameRunning;
        _parallelDownloads = parallelDownloads;
        _ownsHttpClient = ownsHttpClient;
    }

    /// <summary>
    /// Builds a resumable installation plan from the game's current custom-sprite manifest.
    /// </summary>
    /// <param name="gameRoot">The connected game root, or <see langword="null"/> to locate it beside the tracker.</param>
    /// <param name="includeUnavailable">Whether to also retry missing resources previously confirmed as HTTP 404.</param>
    /// <returns>The sheets already present and still missing.</returns>
    /// <exception cref="InvalidOperationException">The game root or custom-sprite manifest cannot be located.</exception>
    /// <exception cref="IOException">The manifest or an existing candidate sheet cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The installation does not permit local inspection.</exception>
    public CustomSpriteInstallPlan CreatePlan(string? gameRoot = null, bool includeUnavailable = false)
    {
        string resolvedRoot = ResolveGameRoot(gameRoot);
        string manifestPath = Path.Combine(resolvedRoot, CustomSpriteManifestRelativePath);
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException(InvalidManifestMessage);

        string destinationRoot = Path.Combine(resolvedRoot, CustomSpriteSheetFolderRelativePath);
        List<CustomSpriteSheetTarget> allSheets = [.. File.ReadLines(manifestPath)
            .Select(CreateTarget)
            .Where(static target => target is not null)
            .Select(target => target!)
            .DistinctBy(static target => target.RelativePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static target => target.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(target => target with { DestinationPath = Path.Combine(destinationRoot, target.RelativePath.Replace('/', Path.DirectorySeparatorChar)) })];

        HashSet<string> unavailable = CustomSpriteUnavailableStore.Read(resolvedRoot);
        List<CustomSpriteSheetTarget> missingSheets = [.. allSheets.Where(static target => !IsValidPng(target.DestinationPath))];
        List<CustomSpriteSheetTarget> pendingSheets = [.. missingSheets.Where(target => includeUnavailable || !unavailable.Contains(new Uri(CustomSpriteSheetBaseUri, target.RelativePath).AbsoluteUri))];
        return new CustomSpriteInstallPlan(resolvedRoot, pendingSheets, allSheets.Count, missingSheets.Count - pendingSheets.Count);
    }

    /// <summary>
    /// Downloads every missing sheet in a prepared plan with bounded concurrency and retry backoff.
    /// </summary>
    /// <param name="plan">The prepared installation plan.</param>
    /// <param name="progress">The optional progress observer.</param>
    /// <param name="cancellationToken">The token that cancels the installation.</param>
    /// <returns>The installation result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="plan"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Infinite Fusion is running from the plan's installation directory.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
    public async Task<CustomSpriteInstallResult> InstallAsync(CustomSpriteInstallPlan plan, IProgress<CustomSpriteInstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (_gameRunning(plan.GameRoot))
            throw new InvalidOperationException(GameRunningMessage);

        HashSet<string> previouslyUnavailable = CustomSpriteUnavailableStore.Read(plan.GameRoot);
        using CancellationTokenSource downloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        int gameStarted = 0;
        bool StopIfGameStarted()
        {
            if (!_gameRunning(plan.GameRoot))
                return false;

            Interlocked.Exchange(ref gameStarted, 1);
            downloadCancellation.Cancel();
            return true;
        }

        Task gameMonitor = MonitorGameProcessAsync(StopIfGameStarted, downloadCancellation.Token);
        int completed = 0;
        int downloaded = 0;
        int failed = 0;
        int unavailable = plan.UnavailableSheetCount;
        long downloadedBytes = 0;
        progress?.Report(new CustomSpriteInstallProgress(0, plan.PendingSheetCount, 0, 0, unavailable));
        ParallelOptions options = new() { CancellationToken = downloadCancellation.Token, MaxDegreeOfParallelism = _parallelDownloads };
        try
        {
            await Parallel.ForEachAsync(plan.PendingSheets, options, async (target, token) =>
            {
                Uri resource = new(CustomSpriteSheetBaseUri, target.RelativePath);
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (previouslyUnavailable.Contains(resource.AbsoluteUri))
                        CustomSpriteUnavailableStore.SetUnavailable(plan.GameRoot, resource, false);

                    try
                    {
                        long bytes = await DownloadSheetAsync(target, StopIfGameStarted, token).ConfigureAwait(false);
                        Interlocked.Add(ref downloadedBytes, bytes);
                        Interlocked.Increment(ref downloaded);
                    }
                    catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
                    {
                        CustomSpriteUnavailableStore.SetUnavailable(plan.GameRoot, resource, true);
                        Interlocked.Increment(ref unavailable);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref failed);
                }
                catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException)
                {
                    Interlocked.Increment(ref failed);
                }
                finally
                {
                    int currentCompleted = Interlocked.Increment(ref completed);
                    progress?.Report(new CustomSpriteInstallProgress(currentCompleted, plan.PendingSheetCount, Interlocked.Read(ref downloadedBytes), Volatile.Read(ref failed), Volatile.Read(ref unavailable)));
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (Volatile.Read(ref gameStarted) != 0)
        {
            throw new InvalidOperationException(GameRunningMessage);
        }
        finally
        {
            downloadCancellation.Cancel();
            try
            {
                await gameMonitor.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (downloadCancellation.IsCancellationRequested)
            {
            }
        }

        return new CustomSpriteInstallResult(downloaded, failed, downloadedBytes, unavailable);
    }

    /// <summary>
    /// Downloads, validates, and atomically promotes one sprite sheet, retrying transient HTTP responses.
    /// </summary>
    /// <param name="target">The endpoint-relative resource and absolute installation destination.</param>
    /// <param name="stopIfGameStarted">Checks for a newly started game and cancels all installation work when found.</param>
    /// <param name="cancellationToken">The token that cancels streaming or retry delay.</param>
    /// <returns>The number of response bytes promoted into the installation.</returns>
    /// <exception cref="HttpRequestException">The endpoint returns a terminal failure or exhausts retry attempts.</exception>
    /// <exception cref="IOException">The response cannot be streamed, validated, or promoted.</exception>
    /// <exception cref="UnauthorizedAccessException">The destination directory does not permit writes.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
    private async Task<long> DownloadSheetAsync(CustomSpriteSheetTarget target, Func<bool> stopIfGameStarted, CancellationToken cancellationToken)
    {
        string? destinationFolder = Path.GetDirectoryName(target.DestinationPath);
        Directory.CreateDirectory(destinationFolder!);
        string partialPath = target.DestinationPath + PartialDownloadSuffix;
        try
        {
            for (int attempt = 1; attempt <= MaximumDownloadAttempts; attempt++)
            {
                if (stopIfGameStarted())
                    cancellationToken.ThrowIfCancellationRequested();

                using HttpRequestMessage request = new(HttpMethod.Get, new Uri(CustomSpriteSheetBaseUri, target.RelativePath));
                using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    if (attempt < MaximumDownloadAttempts && ShouldRetry(response.StatusCode))
                    {
                        await Task.Delay(GetRetryDelay(response.Headers.RetryAfter, attempt), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw new HttpRequestException(string.Format(System.Globalization.CultureInfo.InvariantCulture, RequestFailureMessage, (int)response.StatusCode), null, response.StatusCode);
                }

                await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using (FileStream destination = new(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    await source.CopyToAsync(destination, CopyBufferSize, cancellationToken).ConfigureAwait(false);

                if (!IsValidPng(partialPath))
                    throw new IOException(InvalidSpriteSheetMessage);

                if (stopIfGameStarted())
                    cancellationToken.ThrowIfCancellationRequested();

                long bytes = new FileInfo(partialPath).Length;
                File.Move(partialPath, target.DestinationPath, true);
                return bytes;
            }
        }
        finally
        {
            File.Delete(partialPath);
        }

        throw new HttpRequestException(RequestFailureMessage);
    }

    /// <summary>
    /// Polls for a newly started Infinite Fusion process throughout an active installation.
    /// </summary>
    /// <param name="stopIfGameStarted">Cancels installation work and reports whether the game is running.</param>
    /// <param name="cancellationToken">The token that stops monitoring after completion or user cancellation.</param>
    /// <returns>A task representing the monitoring lifetime.</returns>
    private static async Task MonitorGameProcessAsync(Func<bool> stopIfGameStarted, CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(GameProcessPollInterval, cancellationToken).ConfigureAwait(false);
            if (stopIfGameStarted())
                return;
        }
    }

    /// <summary>
    /// Converts one individual custom-sprite manifest entry into its shared head-and-variant sheet.
    /// Multiple body sprites intentionally resolve to the same relative target and are deduplicated later.
    /// </summary>
    /// <param name="manifestEntry">One raw line from <c>Data/sprites/CUSTOM_SPRITES</c>.</param>
    /// <returns>The derived target, or <see langword="null"/> when the line is not a supported sprite filename.</returns>
    private static CustomSpriteSheetTarget? CreateTarget(string manifestEntry)
    {
        Match match = CustomSpriteManifestEntry().Match(manifestEntry.Trim());
        if (!match.Success)
            return null;

        string head = match.Groups[1].Value;
        string variant = match.Groups[2].Value.ToLowerInvariant();
        string relativePath = $"{head}/{head}{variant}.png";
        return new CustomSpriteSheetTarget(relativePath, string.Empty);
    }

    /// <summary>
    /// Validates an explicitly supplied game root or discovers one above the tracker and working directories.
    /// </summary>
    /// <param name="requestedRoot">The preferred connected-game root.</param>
    /// <returns>The normalized validated installation directory.</returns>
    /// <exception cref="InvalidOperationException">No valid installation can be found.</exception>
    private static string ResolveGameRoot(string? requestedRoot)
    {
        if (!string.IsNullOrWhiteSpace(requestedRoot))
        {
            string explicitRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(requestedRoot));
            if (IsGameRoot(explicitRoot))
                return explicitRoot;
        }

        string? locatedRoot = FindGameRoot(AppContext.BaseDirectory) ?? FindGameRoot(Directory.GetCurrentDirectory());
        return locatedRoot ?? throw new InvalidOperationException(GameRootNotFoundMessage);
    }

    /// <summary>
    /// Walks a bounded parent chain to locate the nearest Infinite Fusion installation.
    /// </summary>
    /// <param name="startingDirectory">The directory from which parent discovery begins.</param>
    /// <returns>The normalized game root, or <see langword="null"/> when none is found.</returns>
    internal static string? FindGameRoot(string startingDirectory)
    {
        DirectoryInfo? directory = new(Path.GetFullPath(startingDirectory));
        for (int depth = 0; directory is not null && depth <= MaximumParentSearchDepth; depth++, directory = directory.Parent)
        {
            if (IsGameRoot(directory.FullName))
                return Path.TrimEndingDirectorySeparator(directory.FullName);
        }

        return null;
    }

    /// <summary>
    /// Determines whether a directory contains both the expected executable and sprite manifest.
    /// </summary>
    /// <param name="path">The candidate installation directory.</param>
    /// <returns><see langword="true"/> when the directory is a usable Infinite Fusion 2 root.</returns>
    private static bool IsGameRoot(string path)
        => File.Exists(Path.Combine(path, GameExecutableName)) && File.Exists(Path.Combine(path, CustomSpriteManifestRelativePath));

    /// <summary>
    /// Determines whether the Infinite Fusion process associated with the target root is active.
    /// Inaccessible process metadata is treated as running so installation fails safely.
    /// </summary>
    /// <param name="gameRoot">The normalized target installation directory.</param>
    /// <returns><see langword="true"/> when the game is running or cannot be safely distinguished.</returns>
    private static bool IsInfiniteFusionRunning(string gameRoot)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
        foreach (Process process in Process.GetProcessesByName(GameProcessName))
        {
            using (process)
            {
                try
                {
                    string? executable = process.MainModule?.FileName;
                    if (executable is null || string.Equals(Path.GetDirectoryName(executable), normalizedRoot, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Performs the lightweight PNG signature check used for resumable existing and completed files.
    /// </summary>
    /// <param name="path">The candidate local sheet path.</param>
    /// <returns><see langword="true"/> when the file exists and begins with the PNG signature.</returns>
    private static bool IsValidPng(string path)
    {
        if (!File.Exists(path))
            return false;

        Span<byte> signature = stackalloc byte[PngSignature.Length];
        using FileStream stream = File.OpenRead(path);
        return stream.Read(signature) == signature.Length && signature.SequenceEqual(PngSignature);
    }

    /// <summary>
    /// Classifies endpoint responses that can reasonably succeed after a delay.
    /// </summary>
    /// <param name="statusCode">The unsuccessful HTTP response code.</param>
    /// <returns><see langword="true"/> for throttling, request timeout, and server errors.</returns>
    private static bool ShouldRetry(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests || statusCode == HttpStatusCode.RequestTimeout || (int)statusCode >= 500;

    /// <summary>
    /// Chooses the server-provided retry delay when available, otherwise applying exponential backoff.
    /// </summary>
    /// <param name="retryAfter">The optional HTTP <c>Retry-After</c> value.</param>
    /// <param name="attempt">The one-based failed attempt number.</param>
    /// <returns>The delay before the next request.</returns>
    private static TimeSpan GetRetryDelay(RetryConditionHeaderValue? retryAfter, int attempt)
    {
        if (retryAfter?.Delta is TimeSpan delta)
            return delta;
        if (retryAfter?.Date is DateTimeOffset date)
            return date > DateTimeOffset.UtcNow ? date - DateTimeOffset.UtcNow : TimeSpan.Zero;
        return TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
    }

    /// <summary>
    /// Disposes the production-owned HTTP client while preserving clients supplied by tests or other callers.
    /// </summary>
    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    /// <summary>
    /// Gets the compiled parser for <c>head.bodyVariant.png</c> manifest entries.
    /// </summary>
    /// <returns>The culture-invariant manifest filename expression.</returns>
    [GeneratedRegex(@"^(\d+)\.\d+([A-Za-z]*)\.png$", RegexOptions.CultureInvariant)]
    private static partial Regex CustomSpriteManifestEntry();
}
