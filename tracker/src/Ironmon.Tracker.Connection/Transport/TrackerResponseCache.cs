using CacheKey = (System.Type ResponseType, string Scope, string Key);

namespace Ironmon.Tracker.Connection.Transport;

/// <summary>
/// Stores a bounded set of typed request responses using least-recently-used eviction.
/// </summary>
internal sealed class TrackerResponseCache
{
    private const int _defaultCapacity = 256;
    private readonly Dictionary<CacheKey, (object Value, LinkedListNode<CacheKey> Node)> _entries = [];
    private readonly LinkedList<CacheKey> _recency = [];
    private readonly Lock _sync = new();
    private readonly int _capacity;

    /// <summary>
    /// Initializes a bounded response cache.
    /// </summary>
    /// <param name="capacity">The maximum number of responses retained across all request scopes.</param>
    internal TrackerResponseCache(int capacity = _defaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    /// <summary>
    /// Gets the number of retained responses.
    /// </summary>
    internal int Count
    {
        get
        {
            lock (_sync)
                return _entries.Count;
        }
    }

    /// <summary>
    /// Gets a cached response and marks it as recently used.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="scope">The request command or other stable cache scope.</param>
    /// <param name="key">The normalized request key within the scope.</param>
    /// <param name="response">The cached response when found.</param>
    /// <returns>Whether a matching response was found.</returns>
    internal bool TryGet<TResponse>(string scope, string key, out TResponse response) where TResponse : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        CacheKey cacheKey = (typeof(TResponse), scope, key);
        lock (_sync)
        {
            if (!_entries.TryGetValue(cacheKey, out (object Value, LinkedListNode<CacheKey> Node) entry))
            {
                response = null!;
                return false;
            }

            _recency.Remove(entry.Node);
            _recency.AddLast(entry.Node);
            response = (TResponse)entry.Value;
            return true;
        }
    }

    /// <summary>
    /// Adds or replaces a response and evicts the least recently used entry when full.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="scope">The request command or other stable cache scope.</param>
    /// <param name="key">The normalized request key within the scope.</param>
    /// <param name="response">The response to retain.</param>
    internal void Set<TResponse>(string scope, string key, TResponse response) where TResponse : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(response);
        CacheKey cacheKey = (typeof(TResponse), scope, key);
        lock (_sync)
        {
            if (_entries.Remove(cacheKey, out (object Value, LinkedListNode<CacheKey> Node) existing))
                _recency.Remove(existing.Node);

            while (_entries.Count >= _capacity)
            {
                LinkedListNode<CacheKey> oldest = _recency.First!;
                _recency.RemoveFirst();
                _entries.Remove(oldest.Value);
            }

            LinkedListNode<CacheKey> node = _recency.AddLast(cacheKey);
            _entries[cacheKey] = (response, node);
        }
    }

    /// <summary>
    /// Removes every retained response.
    /// </summary>
    internal void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
            _recency.Clear();
        }
    }
}
