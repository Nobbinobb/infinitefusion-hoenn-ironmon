namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Bounds retries for ordinary Windows sharing failures without treating permanent filesystem errors as success.
/// </summary>
internal static class WindowsFileContention
{
    private const int _maximumRetries = 10;
    private const int _retryMilliseconds = 100;
    private const int _accessDenied = 5;
    private const int _sharingViolation = 32;
    private const int _lockViolation = 33;
    private const int _unableToRemoveReplaced = 1175;
    private const int _nativeErrorMask = 0xffff;

    /// <summary>
    /// Repeats a checked ordinary filesystem operation for at most one second of short Windows contention waits.
    /// </summary>
    /// <param name="operation">The operation, receiving a zero-based attempt number and revalidating its owned paths on every call.</param>
    internal static void Retry(Action<int> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                operation(attempt);
                return;
            }
            catch (Exception error) when (attempt < _maximumRetries && IsTransient(error))
            {
                Thread.Sleep(_retryMilliseconds);
            }
        }
    }

    /// <summary>
    /// Repeats a cancellable checked operation with the same bounded Windows contention policy used by durable transactions.
    /// </summary>
    /// <param name="operation">The operation, receiving a zero-based attempt number and revalidating authenticated bytes and paths on retries.</param>
    /// <param name="cancellationToken">The token cancelling an operation or its contention wait.</param>
    /// <param name="clock">The retry timer provider, or null to use the system clock.</param>
    /// <returns>Completion after the operation succeeds, or its final retained error or cancellation.</returns>
    internal static async Task RetryAsync(Func<int, Task> operation, CancellationToken cancellationToken, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        for (int attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await operation(attempt).ConfigureAwait(false);
                return;
            }
            catch (Exception error) when (attempt < _maximumRetries && IsTransient(error))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(_retryMilliseconds), clock ?? TimeProvider.System, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Selects Windows contention codes whose failed operation can be repeated after its ownership and contents are checked again.
    /// </summary>
    /// <remarks>
    /// Other ReplaceFile errors can leave different name mappings and must retain their recovery failure instead of being replayed.
    /// </remarks>
    /// <param name="error">The ordinary filesystem operation's failure.</param>
    /// <returns>Whether the native error is eligible for a bounded retry.</returns>
    private static bool IsTransient(Exception error)
        => OperatingSystem.IsWindows() && (error is IOException or UnauthorizedAccessException) && (error.HResult & _nativeErrorMask) is _accessDenied or _sharingViolation or _lockViolation or _unableToRemoveReplaced;
}
