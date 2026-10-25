namespace Ironmon.Tracker.Connection.Transport;

/// <summary>
/// Owns replaceable asynchronous requests while allowing independent request keys to run concurrently.
/// </summary>
/// <typeparam name="TKey">The type used to identify independent request slots.</typeparam>
public sealed class LatestRequestCoordinator<TKey> : IDisposable where TKey : notnull
{
    private readonly Dictionary<TKey, LatestRequestLease<TKey>> _requests = [];
    private readonly Lock _sync = new();
    private bool _disposed;

    /// <summary>
    /// Initializes an empty request coordinator.
    /// </summary>
    public LatestRequestCoordinator()
    {
    }

    /// <summary>
    /// Begins a request and cancels the previous request using the same key.
    /// </summary>
    /// <param name="key">The request slot to replace.</param>
    /// <returns>A lease that identifies and cancels the new request.</returns>
    public LatestRequestLease<TKey> Begin(TKey key)
    {
        LatestRequestLease<TKey>? previous;
        LatestRequestLease<TKey> request;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _requests.TryGetValue(key, out previous);
            request = new LatestRequestLease<TKey>(this, key);
            _requests[key] = request;
        }

        previous?.Cancel();
        return request;
    }

    /// <summary>
    /// Cancels and removes every active request.
    /// </summary>
    public void CancelAll()
    {
        LatestRequestLease<TKey>[] requests;
        lock (_sync)
        {
            requests = [.. _requests.Values];
            _requests.Clear();
        }

        foreach (LatestRequestLease<TKey> request in requests)
            request.Cancel();
    }

    /// <summary>
    /// Cancels active work and prevents new requests from beginning.
    /// </summary>
    public void Dispose()
    {
        LatestRequestLease<TKey>[] requests;
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            requests = [.. _requests.Values];
            _requests.Clear();
        }

        foreach (LatestRequestLease<TKey> request in requests)
            request.Cancel();
    }

    /// <summary>
    /// Determines whether a lease still owns its keyed request slot.
    /// </summary>
    /// <param name="request">The lease to inspect.</param>
    /// <returns>Whether the lease is current and has not been canceled.</returns>
    internal bool IsCurrent(LatestRequestLease<TKey> request)
    {
        lock (_sync)
        {
            return !_disposed
                && _requests.TryGetValue(request.Key, out LatestRequestLease<TKey>? current)
                && ReferenceEquals(current, request)
                && !request.IsCancellationRequested;
        }
    }

    /// <summary>
    /// Removes a completed lease only when no newer request has replaced it.
    /// </summary>
    /// <param name="request">The completed request lease.</param>
    /// <returns>Whether the lease owned its slot when completion was reported.</returns>
    internal bool Complete(LatestRequestLease<TKey> request)
    {
        lock (_sync)
        {
            if (!_requests.TryGetValue(request.Key, out LatestRequestLease<TKey>? current) || !ReferenceEquals(current, request))
                return false;

            _requests.Remove(request.Key);
            return !_disposed && !request.IsCancellationRequested;
        }
    }
}

/// <summary>
/// Represents one replaceable request owned by a <see cref="LatestRequestCoordinator{TKey}"/>.
/// </summary>
/// <typeparam name="TKey">The type used to identify the request slot.</typeparam>
public sealed class LatestRequestLease<TKey> : IDisposable where TKey : notnull
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly LatestRequestCoordinator<TKey> _owner;
    private readonly Lock _sync = new();
    private volatile bool _canceled;
    private bool _completed;

    /// <summary>
    /// Initializes a request lease owned by the supplied coordinator.
    /// </summary>
    /// <param name="owner">The coordinator that owns the request slot.</param>
    /// <param name="key">The request slot key.</param>
    internal LatestRequestLease(LatestRequestCoordinator<TKey> owner, TKey key)
    {
        _owner = owner;
        Key = key;
    }

    /// <summary>
    /// Gets the token canceled when the request is replaced or its coordinator is reset.
    /// </summary>
    public CancellationToken CancellationToken => _cancellation.Token;

    /// <summary>
    /// Gets whether this request still owns its keyed result surface.
    /// </summary>
    public bool IsCurrent => _owner.IsCurrent(this);

    /// <summary>
    /// Gets the request slot key.
    /// </summary>
    internal TKey Key { get; }

    /// <summary>
    /// Gets whether replacement or reset canceled this request.
    /// </summary>
    internal bool IsCancellationRequested => _canceled;

    /// <summary>
    /// Reports completion and releases the request's cancellation resources.
    /// </summary>
    /// <returns>Whether this lease still owned its request slot at completion.</returns>
    public bool Complete()
    {
        bool current = _owner.Complete(this);
        lock (_sync)
        {
            if (_completed)
                return false;

            _completed = true;
            _cancellation.Dispose();
        }

        return current;
    }

    /// <summary>
    /// Reports completion and releases the request's cancellation resources.
    /// </summary>
    public void Dispose()
        => Complete();

    /// <summary>
    /// Cancels this request unless its owner has already completed it.
    /// </summary>
    internal void Cancel()
    {
        lock (_sync)
        {
            if (!_completed)
            {
                _canceled = true;
                _cancellation.Cancel();
            }
        }
    }
}
