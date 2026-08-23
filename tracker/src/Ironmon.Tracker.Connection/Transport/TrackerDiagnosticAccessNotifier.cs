namespace Ironmon.Tracker.Connection.Transport;

/// <summary>
/// Serializes live diagnostic-capability notifications for one connection service.
/// </summary>
internal sealed class TrackerDiagnosticAccessNotifier : IAsyncDisposable
{
    private readonly DiagnosticAccessService _access;
    private readonly TrackerDiagnosticsStore _diagnostics;
    private readonly TrackerRequestClient _requestClient;
    private readonly TrackerRequestSession _requestSession;
    private readonly TrackerConnectionState _state;
    private readonly Lock _sync = new();
    private Task _pendingNotification = Task.CompletedTask;
    private bool _disposed;

    /// <summary>
    /// Initializes and subscribes the live diagnostic-capability notifier.
    /// </summary>
    /// <param name="access">The signed diagnostic-access service.</param>
    /// <param name="diagnostics">The connection diagnostic store.</param>
    /// <param name="state">The shared connection state.</param>
    /// <param name="requestSession">The active protocol request session.</param>
    /// <param name="requestClient">The request client that negotiates capabilities.</param>
    internal TrackerDiagnosticAccessNotifier(DiagnosticAccessService access, TrackerDiagnosticsStore diagnostics, TrackerConnectionState state, TrackerRequestSession requestSession, TrackerRequestClient requestClient)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(requestSession);
        ArgumentNullException.ThrowIfNull(requestClient);
        _access = access;
        _diagnostics = diagnostics;
        _state = state;
        _requestSession = requestSession;
        _requestClient = requestClient;
        _access.Changed += OnDiagnosticAccessChanged;
    }

    /// <summary>
    /// Unsubscribes and waits for the last queued capability notification.
    /// </summary>
    /// <returns>A task representing notifier shutdown.</returns>
    public async ValueTask DisposeAsync()
    {
        Task pendingNotification;
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _access.Changed -= OnDiagnosticAccessChanged;
            pendingNotification = _pendingNotification;
        }

        await pendingNotification.ConfigureAwait(false);
    }

    /// <summary>
    /// Queues capability replacement after any notification already in flight.
    /// </summary>
    /// <param name="sender">The diagnostic-access service.</param>
    /// <param name="eventArgs">The empty change arguments.</param>
    private void OnDiagnosticAccessChanged(object? sender, EventArgs eventArgs)
    {
        TrackerConnectionSnapshot snapshot = _state.Snapshot;
        if (snapshot.Status != TrackerConnectionStatus.Connected || snapshot.Game is null)
            return;

        DiagnosticAccessChangedPayload payload = new()
        {
            DiagnosticCapabilities = _requestClient.GetNegotiatedDiagnosticCapabilities(snapshot.Game)
        };

        string? runId = snapshot.CurrentState?.RunId ?? snapshot.Game.RunId;
        lock (_sync)
        {
            if (_disposed)
                return;

            _pendingNotification = SendAfterAsync(_pendingNotification, payload, runId);
        }
    }

    /// <summary>
    /// Sends captured effective capabilities after a preceding notification completes.
    /// </summary>
    /// <param name="precedingNotification">The preceding serialized notification.</param>
    /// <param name="payload">The capabilities captured for this change.</param>
    /// <param name="runId">The run active when this change occurred.</param>
    /// <returns>A task representing the queued notification.</returns>
    private async Task SendAfterAsync(Task precedingNotification, DiagnosticAccessChangedPayload payload, string? runId)
    {
        await precedingNotification.ConfigureAwait(false);
        try
        {
            await _requestSession.SendEventAsync(TrackerEvents.DiagnosticAccessChanged, payload, runId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or ObjectDisposedException)
        {
            _diagnostics.RecordError(exception);
        }
    }
}
