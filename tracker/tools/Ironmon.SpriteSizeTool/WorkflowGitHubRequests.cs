using Ironmon.Updater.Infrastructure;

namespace Ironmon.SpriteSizeTool;

/// <summary>
/// Retries workflow API lookups within a shared two-minute wait budget, without holding an HTTP request open.
/// </summary>
internal sealed class WorkflowGitHubRequests
{
    private const int MaximumAttempts = 3;
    private static readonly TimeSpan _maximumWait = TimeSpan.FromMinutes(2);
    private readonly TimeProvider _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Action<string> _log;
    private TimeSpan _waited;

    /// <summary>
    /// Creates the bounded workflow retry policy with ordinary cancellable delays.
    /// </summary>
    internal WorkflowGitHubRequests() : this(TimeProvider.System, Task.Delay, Console.WriteLine)
    {
    }

    /// <summary>
    /// Creates a deterministic retry fixture without real waits or network traffic.
    /// </summary>
    /// <param name="clock">The server-deadline clock.</param>
    /// <param name="delay">The cancellable wait implementation.</param>
    /// <param name="log">The credential-free status sink.</param>
    internal WorkflowGitHubRequests(TimeProvider clock, Func<TimeSpan, CancellationToken, Task> delay, Action<string> log)
    {
        _clock = clock;
        _delay = delay;
        _log = log;
    }

    /// <summary>
    /// Preserves signed release verification and translates cached retry timing into the workflow retry policy.
    /// </summary>
    /// <param name="discovery">The signed public release discovery service.</param>
    /// <param name="cancellationToken">The whole-scan cancellation token.</param>
    /// <returns>The discovery result after any permitted rate-limit retry.</returns>
    internal Task<ReleaseDiscoveryResult> CheckReleaseAsync(ReleaseDiscovery discovery, CancellationToken cancellationToken)
    {
        return RetryAsync(async () =>
        {
            var observation = await discovery.CheckAsync(true, cancellationToken).ConfigureAwait(false);
            if (observation.RetryAt is { } retryAt)
                throw new ReleaseRateLimitException(retryAt);

            return observation;
        }, cancellationToken);
    }

    /// <summary>
    /// Retries explicit rate limits only, preserving the last published feed when waiting would exceed the budget.
    /// </summary>
    /// <typeparam name="T">The complete lookup result.</typeparam>
    /// <param name="lookup">The lookup to repeat after a server-authorized delay.</param>
    /// <param name="cancellationToken">The whole-scan cancellation token.</param>
    /// <returns>The first successful result.</returns>
    internal async Task<T> RetryAsync<T>(Func<Task<T>> lookup, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await lookup().ConfigureAwait(false);
            }
            catch (ReleaseRateLimitException error)
            {
                var wait = error.RetryAt - _clock.GetUtcNow() + TimeSpan.FromSeconds(1);
                if (wait < TimeSpan.FromSeconds(1))
                    wait = TimeSpan.FromSeconds(1);

                if (attempt >= MaximumAttempts || wait > _maximumWait - _waited)
                    throw new IOException($"GitHub API rate limit: retry after {error.RetryAt:O}. The workflow retry budget is exhausted; the published estimate is unchanged.");

                _log($"GitHub API rate limited; waiting {Math.Ceiling(wait.TotalSeconds)} seconds before attempt {attempt + 1}/{MaximumAttempts}.");
                await _delay(wait, cancellationToken).ConfigureAwait(false);
                _waited += wait;
            }
        }
    }
}
