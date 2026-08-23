using System.Diagnostics.CodeAnalysis;

namespace Ironmon.Tracker.Core;

/// <summary>
/// Tracks backward and forward navigation while allowing navigation to commit only after a successful load.
/// </summary>
/// <typeparam name="T">The navigation target type.</typeparam>
public sealed class NavigationHistory<T>
{
    private readonly List<T> _back = [];
    private readonly List<T> _forward = [];

    /// <summary>
    /// Initializes empty navigation history.
    /// </summary>
    public NavigationHistory()
    {
    }

    /// <summary>
    /// Gets whether backward navigation is available.
    /// </summary>
    public bool CanGoBack => _back.Count > 0;

    /// <summary>
    /// Gets whether forward navigation is available.
    /// </summary>
    public bool CanGoForward => _forward.Count > 0;

    /// <summary>
    /// Clears all navigation history.
    /// </summary>
    public void Clear()
    {
        _back.Clear();
        _forward.Clear();
    }

    /// <summary>
    /// Records navigation from the current target to a newly loaded target.
    /// </summary>
    /// <param name="current">The target being left.</param>
    public void RecordNavigation(T current)
    {
        _back.Add(current);
        _forward.Clear();
    }

    /// <summary>
    /// Gets the next backward target without changing history.
    /// </summary>
    /// <param name="target">The next backward target when one exists.</param>
    /// <returns>Whether backward navigation is available.</returns>
    public bool TryPeekBack([MaybeNullWhen(false)] out T target)
        => TryPeek(_back, out target);

    /// <summary>
    /// Commits a successful backward navigation.
    /// </summary>
    /// <param name="current">The target being left.</param>
    public void CommitBack(T current)
    {
        EnsureAvailable(_back);
        _back.RemoveAt(_back.Count - 1);
        _forward.Add(current);
    }

    /// <summary>
    /// Gets the next forward target without changing history.
    /// </summary>
    /// <param name="target">The next forward target when one exists.</param>
    /// <returns>Whether forward navigation is available.</returns>
    public bool TryPeekForward([MaybeNullWhen(false)] out T target)
        => TryPeek(_forward, out target);

    /// <summary>
    /// Commits a successful forward navigation.
    /// </summary>
    /// <param name="current">The target being left.</param>
    public void CommitForward(T current)
    {
        EnsureAvailable(_forward);
        _forward.RemoveAt(_forward.Count - 1);
        _back.Add(current);
    }

    /// <summary>
    /// Gets the last target in one navigation direction.
    /// </summary>
    /// <param name="targets">The targets in navigation order.</param>
    /// <param name="target">The last target when one exists.</param>
    /// <returns>Whether a target is available.</returns>
    private static bool TryPeek(IReadOnlyList<T> targets, [MaybeNullWhen(false)] out T target)
    {
        if (targets.Count == 0)
        {
            target = default;
            return false;
        }

        target = targets[^1];
        return true;
    }

    /// <summary>
    /// Rejects committing navigation that was not previously available.
    /// </summary>
    /// <param name="targets">The navigation-direction targets.</param>
    private static void EnsureAvailable(IReadOnlyCollection<T> targets)
    {
        if (targets.Count == 0)
            throw new InvalidOperationException("No navigation target is available to commit.");
    }
}
