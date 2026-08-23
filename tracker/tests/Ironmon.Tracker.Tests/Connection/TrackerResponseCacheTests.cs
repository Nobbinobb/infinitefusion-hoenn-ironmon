namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies bounded typed response caching and least-recently-used eviction.
/// </summary>
public sealed class TrackerResponseCacheTests
{
    /// <summary>
    /// Verifies that the oldest response is removed when capacity is reached.
    /// </summary>
    [Fact]
    public void SetEvictsLeastRecentlyUsedResponse()
    {
        TrackerResponseCache cache = new(2);
        object first = new();
        object second = new();
        object third = new();
        cache.Set("lookup", "first", first);
        cache.Set("lookup", "second", second);

        Assert.True(cache.TryGet("lookup", "first", out object retainedFirst));
        Assert.Same(first, retainedFirst);
        cache.Set("lookup", "third", third);

        Assert.False(cache.TryGet("lookup", "second", out object _));
        Assert.True(cache.TryGet("lookup", "first", out retainedFirst));
        Assert.Same(first, retainedFirst);
        Assert.True(cache.TryGet("lookup", "third", out object retainedThird));
        Assert.Same(third, retainedThird);
        Assert.Equal(2, cache.Count);
    }

    /// <summary>
    /// Verifies that command scope prevents otherwise identical keys from colliding.
    /// </summary>
    [Fact]
    public void ScopeSeparatesMatchingKeys()
    {
        TrackerResponseCache cache = new(2);
        object search = new();
        object lookup = new();
        cache.Set("search", "same", search);
        cache.Set("lookup", "same", lookup);

        Assert.True(cache.TryGet("search", "same", out object retainedSearch));
        Assert.Same(search, retainedSearch);
        Assert.True(cache.TryGet("lookup", "same", out object retainedLookup));
        Assert.Same(lookup, retainedLookup);
    }

    /// <summary>
    /// Verifies that response type prevents otherwise identical entries from colliding.
    /// </summary>
    [Fact]
    public void ResponseTypeSeparatesMatchingKeys()
    {
        TrackerResponseCache cache = new(2);
        object untyped = new();
        string typed = "response";
        cache.Set("lookup", "same", untyped);
        cache.Set("lookup", "same", typed);

        Assert.True(cache.TryGet("lookup", "same", out object retainedObject));
        Assert.Same(untyped, retainedObject);
        Assert.True(cache.TryGet("lookup", "same", out string retainedString));
        Assert.Same(typed, retainedString);
    }

    /// <summary>
    /// Verifies that clearing removes all scopes and allows the cache to be reused.
    /// </summary>
    [Fact]
    public void ClearRemovesAllResponses()
    {
        TrackerResponseCache cache = new(2);
        cache.Set("search", "first", new object());
        cache.Set("lookup", "second", new object());

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet("search", "first", out object _));
        object replacement = new();
        cache.Set("search", "replacement", replacement);
        Assert.True(cache.TryGet("search", "replacement", out object retained));
        Assert.Same(replacement, retained);
    }
}
