using System.Collections.Concurrent;

namespace Ironmon.Tracker.Connection.Transport;

/// <summary>
/// Owns correlated tracker requests for the currently connected game writer.
/// </summary>
internal sealed class TrackerRequestSession : IDisposable
{
    private readonly Lock _sync = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TrackerMessage>> _pendingRequests = new();
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly SemaphoreSlim _writerLock = new(1, 1);
    private readonly TrackerDiagnosticsStore _diagnostics;
    private TrackerMessageWriter? _writer;
    private bool _disposed;

    /// <summary>
    /// Initializes an inactive tracker request session.
    /// </summary>
    /// <param name="diagnostics">The outgoing protocol diagnostic store.</param>
    /// <exception cref="ArgumentNullException">Thrown when diagnostics is null.</exception>
    internal TrackerRequestSession(TrackerDiagnosticsStore diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        _diagnostics = diagnostics;
    }

    /// <summary>
    /// Attaches the writer for one connected game session.
    /// </summary>
    /// <param name="writer">The active protocol writer.</param>
    /// <exception cref="ArgumentNullException">Thrown when the writer is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown after the session is disposed.</exception>
    internal void Connect(TrackerMessageWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _writer = writer;
        }
    }

    /// <summary>
    /// Completes a pending request from one correlated response.
    /// </summary>
    /// <param name="response">The response received from the game.</param>
    /// <returns>Whether a pending request accepted the response.</returns>
    internal bool TryComplete(TrackerMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.RequestId is not null
            && _pendingRequests.TryRemove(response.RequestId, out TaskCompletionSource<TrackerMessage>? completion)
            && completion.TrySetResult(response);
    }

    /// <summary>
    /// Sends one correlated request over the active game connection.
    /// </summary>
    /// <typeparam name="TRequest">The request payload type.</typeparam>
    /// <typeparam name="TResponse">The successful response payload type.</typeparam>
    /// <param name="command">The game command.</param>
    /// <param name="payload">The request payload.</param>
    /// <param name="runId">The run identifier when the request is scoped to a run.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The deserialized successful response.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no game is connected.</exception>
    /// <exception cref="TrackerProtocolException">Thrown when the game rejects the request.</exception>
    internal async Task<TResponse> SendAsync<TRequest, TResponse>(string command, TRequest payload, string? runId, CancellationToken cancellationToken)
    {
        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TrackerMessageWriter writer;
            lock (_sync)
                writer = _writer ?? throw new InvalidOperationException("The game is not connected.");

            string requestId = Guid.NewGuid().ToString("N");
            TaskCompletionSource<TrackerMessage> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pendingRequests.TryAdd(requestId, completion))
                throw new InvalidOperationException("The tracker could not reserve a request identifier.");

            try
            {
                TrackerMessage request = TrackerMessageFactory.CreateRequest(requestId, command, payload, runId);
                await WriteAsync(writer, request, cancellationToken).ConfigureAwait(false);
                TrackerMessage response = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                if (response.Success != true)
                    throw new TrackerProtocolException(response.Error?.Message ?? "The game rejected the tracker request.");

                return TrackerJson.DeserializePayload<TResponse>(response.Payload);
            }
            finally
            {
                _pendingRequests.TryRemove(requestId, out _);
            }
        }
        finally
        {
            _requestLock.Release();
        }
    }

    /// <summary>
    /// Serializes one request write and records its outgoing diagnostic entry.
    /// </summary>
    /// <param name="writer">The active game writer.</param>
    /// <param name="request">The correlated request.</param>
    /// <param name="cancellationToken">The token that cancels the write.</param>
    /// <returns>A task representing the write.</returns>
    private async Task WriteAsync(TrackerMessageWriter writer, TrackerMessage request, CancellationToken cancellationToken)
    {
        await _writerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
            _diagnostics.RecordOutgoing(request);
        }
        finally
        {
            _writerLock.Release();
        }
    }

    /// <summary>
    /// Detaches the writer and fails requests awaiting a disconnected game.
    /// </summary>
    internal void Disconnect()
    {
        lock (_sync)
            _writer = null;

        foreach ((string requestId, TaskCompletionSource<TrackerMessage> completion) in _pendingRequests)
        {
            if (_pendingRequests.TryRemove(requestId, out _))
                completion.TrySetException(new IOException("The game disconnected before completing the tracker request."));
        }
    }

    /// <summary>
    /// Disconnects the current session and releases synchronization resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        Disconnect();
        _requestLock.Dispose();
        _writerLock.Dispose();
        _disposed = true;
    }
}
