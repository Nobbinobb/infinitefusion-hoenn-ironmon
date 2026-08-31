using System.Net;
using System.Net.Http.Headers;
using Ironmon.Tracker.Connection.Sprites;

namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies persistent 404 suppression without hiding other failures or preventing deliberate rechecks.
/// </summary>
public sealed partial class CustomSpriteSheetInstallerTests
{
    private const string FirstSheetRelativePath = "1/1.png";
    private const string OtherEndpoint = "https://example.invalid/sprites/1/1.png";
    private const string InvalidCacheContents = "{invalid";

    /// <summary>
    /// Verifies 404s survive installer restarts, stay out of installed counts, and can be explicitly recovered.
    /// </summary>
    [Fact]
    public async Task MissingSheetsAreRememberedUntilAnExplicitRecheckRecoversThem()
    {
        PrepareInstallation();
        using HttpClient missingClient = new(new StatusResponseHandler(HttpStatusCode.NotFound));
        using (CustomSpriteSheetInstaller installer = new(missingClient, static _ => false))
        {
            CustomSpriteInstallResult result = await installer.InstallAsync(installer.CreatePlan(_root));
            Assert.Equal(0, result.DownloadedSheetCount);
            Assert.Equal(0, result.FailedSheetCount);
            Assert.Equal(2, result.UnavailableSheetCount);
        }

        StaticResponseHandler recovered = new(_png);
        using HttpClient client = new(recovered);
        using CustomSpriteSheetInstaller restarted = new(client, static _ => false);
        CustomSpriteInstallPlan ordinary = restarted.CreatePlan(_root);
        Assert.Equal(2, ordinary.TotalSheetCount);
        Assert.Equal(0, ordinary.ExistingSheetCount);
        Assert.Equal(0, ordinary.PendingSheetCount);
        Assert.Equal(2, ordinary.UnavailableSheetCount);
        CustomSpriteInstallResult skipped = await restarted.InstallAsync(ordinary);
        Assert.Equal(2, skipped.UnavailableSheetCount);
        Assert.Empty(recovered.Paths);

        CustomSpriteInstallPlan recheck = restarted.CreatePlan(_root, includeUnavailable: true);
        Assert.Equal(2, recheck.PendingSheetCount);
        Assert.Equal(0, recheck.UnavailableSheetCount);
        Assert.Equal(0, restarted.CreatePlan(_root).PendingSheetCount);
        CustomSpriteInstallResult retried = await restarted.InstallAsync(recheck);
        Assert.Equal(2, retried.DownloadedSheetCount);
        Assert.Equal(0, retried.UnavailableSheetCount);
        Assert.Equal(2, restarted.CreatePlan(_root).ExistingSheetCount);

        File.Delete(Path.Combine(_root, SheetFolderRelativePath, FirstSheetRelativePath));
        Assert.Equal(1, restarted.CreatePlan(_root).PendingSheetCount);
    }

    /// <summary>
    /// Verifies all unsuccessful statuses except 404 stay eligible for subsequent ordinary attempts.
    /// </summary>
    /// <param name="status">The unsuccessful server response.</param>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task OtherHttpFailuresRemainRetryable(HttpStatusCode status)
    {
        PrepareInstallation();
        using HttpClient client = new(new StatusResponseHandler(status));
        using CustomSpriteSheetInstaller installer = new(client, static _ => false);
        CustomSpriteInstallResult result = await installer.InstallAsync(installer.CreatePlan(_root));
        Assert.Equal(2, result.FailedSheetCount);
        Assert.Equal(0, result.UnavailableSheetCount);
        Assert.Equal(2, installer.CreatePlan(_root).PendingSheetCount);
    }

    /// <summary>
    /// Verifies a previously missing resource is no longer suppressed when its recheck has a different failure.
    /// </summary>
    [Fact]
    public async Task RecheckWithDifferentFailureRestoresOrdinaryRetryEligibility()
    {
        PrepareInstallation();
        using HttpClient missingClient = new(new StatusResponseHandler(HttpStatusCode.NotFound));
        using CustomSpriteSheetInstaller initial = new(missingClient, static _ => false);
        await initial.InstallAsync(initial.CreatePlan(_root));

        using HttpClient deniedClient = new(new StatusResponseHandler(HttpStatusCode.Forbidden));
        using CustomSpriteSheetInstaller recheck = new(deniedClient, static _ => false);
        CustomSpriteInstallResult result = await recheck.InstallAsync(recheck.CreatePlan(_root, includeUnavailable: true));
        Assert.Equal(2, result.FailedSheetCount);
        Assert.Equal(0, result.UnavailableSheetCount);
        Assert.Equal(2, recheck.CreatePlan(_root).PendingSheetCount);
    }

    /// <summary>
    /// Verifies transport failures and invalid image content never enter the unavailable cache.
    /// </summary>
    /// <param name="transportFailure">Whether to fail transport instead of returning malformed image bytes.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TransportAndInvalidImageFailuresRemainRetryable(bool transportFailure)
    {
        PrepareInstallation();
        using HttpClient client = new(new DelegateResponseHandler((_, _) => transportFailure
            ? Task.FromException<HttpResponseMessage>(new HttpRequestException("Network unavailable."))
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) })));

        using CustomSpriteSheetInstaller installer = new(client, static _ => false);
        CustomSpriteInstallResult result = await installer.InstallAsync(installer.CreatePlan(_root));
        Assert.Equal(2, result.FailedSheetCount);
        Assert.Equal(0, result.UnavailableSheetCount);
        Assert.Equal(2, installer.CreatePlan(_root).PendingSheetCount);
    }

    /// <summary>
    /// Verifies a per-request timeout is retryable without being mistaken for user cancellation or HTTP 404.
    /// </summary>
    [Fact]
    public async Task RequestTimeoutRemainsRetryable()
    {
        PrepareInstallation();
        using HttpClient client = new(new DelegateResponseHandler((_, _) => Task.FromException<HttpResponseMessage>(new TaskCanceledException("Request timed out."))));
        using CustomSpriteSheetInstaller installer = new(client, static _ => false);
        CustomSpriteInstallResult result = await installer.InstallAsync(installer.CreatePlan(_root));
        Assert.Equal(2, result.FailedSheetCount);
        Assert.Equal(0, result.UnavailableSheetCount);
        Assert.Equal(2, installer.CreatePlan(_root).PendingSheetCount);
    }

    /// <summary>
    /// Verifies cancellation does not discard a 404 confirmed before another request was cancelled.
    /// </summary>
    [Fact]
    public async Task CancellationKeepsAlreadyConfirmedMissingResources()
    {
        PrepareInstallation();
        TaskCompletionSource requestBlocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int requests = 0;
        using HttpClient client = new(new DelegateResponseHandler(async (_, token) =>
        {
            if (Interlocked.Increment(ref requests) == 1)
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            requestBlocked.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        using CustomSpriteSheetInstaller installer = new(client, static _ => false, parallelDownloads: 1);
        using CancellationTokenSource cancellation = new();
        Task<CustomSpriteInstallResult> download = installer.InstallAsync(installer.CreatePlan(_root), cancellationToken: cancellation.Token);
        await requestBlocked.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
        CustomSpriteInstallPlan remaining = installer.CreatePlan(_root);
        Assert.Equal(1, remaining.PendingSheetCount);
        Assert.Equal(1, remaining.UnavailableSheetCount);
        Assert.Equal(0, remaining.ExistingSheetCount);
    }

    /// <summary>
    /// Verifies a valid manually installed sheet takes precedence over an earlier 404.
    /// </summary>
    [Fact]
    public async Task LocalValidSheetsAreInstalledEvenWhenPreviouslyUnavailable()
    {
        PrepareInstallation();
        using HttpClient client = new(new StatusResponseHandler(HttpStatusCode.NotFound));
        using CustomSpriteSheetInstaller installer = new(client, static _ => false);
        await installer.InstallAsync(installer.CreatePlan(_root));
        File.WriteAllBytes(Path.Combine(_root, SheetFolderRelativePath, FirstSheetRelativePath), _png);
        CustomSpriteInstallPlan plan = installer.CreatePlan(_root);
        Assert.Equal(1, plan.ExistingSheetCount);
        Assert.Equal(1, plan.UnavailableSheetCount);
        Assert.Equal(0, plan.PendingSheetCount);
        Assert.Equal(1, installer.CreatePlan(_root, includeUnavailable: true).PendingSheetCount);
    }

    /// <summary>
    /// Verifies cached URLs from a different host cannot hide official sprite resources.
    /// </summary>
    [Fact]
    public void UnavailableCacheIsScopedToTheExactResourceUrl()
    {
        PrepareInstallation();
        CustomSpriteUnavailableStore.SetUnavailable(_root, new Uri(OtherEndpoint), true);
        using HttpClient client = new(new StaticResponseHandler(_png));
        using CustomSpriteSheetInstaller installer = new(client, static _ => false);
        Assert.Equal(2, installer.CreatePlan(_root).PendingSheetCount);
    }

    /// <summary>
    /// Verifies malformed cache data cannot permanently hide missing files.
    /// </summary>
    [Fact]
    public void MalformedUnavailableCacheFallsBackToOrdinaryDownloads()
    {
        PrepareInstallation();
        string path = Path.Combine(_root, CustomSpriteUnavailableStore.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, InvalidCacheContents);
        using HttpClient client = new(new StaticResponseHandler(_png));
        using CustomSpriteSheetInstaller installer = new(client, static _ => false);
        Assert.Equal(2, installer.CreatePlan(_root).PendingSheetCount);
    }

    /// <summary>
    /// Verifies persistence failures are reported as retryable instead of falsely claiming a durable 404 exclusion.
    /// </summary>
    [Fact]
    public async Task UnwritableUnavailableCacheDoesNotHideFailures()
    {
        PrepareInstallation();
        Directory.CreateDirectory(Path.Combine(_root, CustomSpriteUnavailableStore.RelativePath));
        using HttpClient client = new(new StatusResponseHandler(HttpStatusCode.NotFound));
        using CustomSpriteSheetInstaller installer = new(client, static _ => false);
        CustomSpriteInstallResult result = await installer.InstallAsync(installer.CreatePlan(_root));
        Assert.Equal(2, result.FailedSheetCount);
        Assert.Equal(0, result.UnavailableSheetCount);
        Assert.Equal(2, installer.CreatePlan(_root).PendingSheetCount);
    }

    /// <summary>
    /// Supplies unsuccessful responses with immediate retry permission for bounded retry tests.
    /// </summary>
    /// <remarks>Initializes the handler with the server status to return.</remarks>
    /// <param name="status">The status returned by every request.</param>
    private sealed class StatusResponseHandler(HttpStatusCode status) : HttpMessageHandler
    {
        /// <summary>Returns the selected HTTP status without imposing real retry delays.</summary>
        /// <param name="request">The outgoing sheet request.</param>
        /// <param name="cancellationToken">The request cancellation token.</param>
        /// <returns>A completed response task.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(status);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        }
    }

    /// <summary>Supplies controlled transport, content, and cancellation behavior.</summary>
    /// <remarks>Initializes the handler with the asynchronous response factory.</remarks>
    /// <param name="respond">The factory used for each outgoing request.</param>
    private sealed class DelegateResponseHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        /// <summary>Invokes the controlled response factory.</summary>
        /// <param name="request">The outgoing sheet request.</param>
        /// <param name="cancellationToken">The request cancellation token.</param>
        /// <returns>The controlled response task.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request, cancellationToken);
    }
}
