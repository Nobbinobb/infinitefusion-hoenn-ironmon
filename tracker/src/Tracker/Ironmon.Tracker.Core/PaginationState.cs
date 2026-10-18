namespace Ironmon.Tracker.Core;

/// <summary>
/// Tracks a zero-based page and centralizes bounded paging calculations.
/// </summary>
public sealed class PaginationState
{
    /// <summary>
    /// Initializes paging with a fixed number of items per page.
    /// </summary>
    /// <param name="pageSize">The positive number of items per page.</param>
    public PaginationState(int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        PageSize = pageSize;
    }

    /// <summary>
    /// Gets the fixed number of items per page.
    /// </summary>
    public int PageSize { get; }

    /// <summary>
    /// Gets the selected zero-based page index.
    /// </summary>
    public int PageIndex { get; private set; }

    /// <summary>
    /// Gets the zero-based item offset for the selected page.
    /// </summary>
    public int Offset => checked(PageIndex * PageSize);

    /// <summary>
    /// Gets whether a page precedes the selected page.
    /// </summary>
    public bool HasPrevious => PageIndex > 0;

    /// <summary>
    /// Selects the first page.
    /// </summary>
    public void Reset()
        => PageIndex = 0;

    /// <summary>
    /// Selects a non-negative page without applying a result-count bound.
    /// </summary>
    /// <param name="pageIndex">The zero-based page index.</param>
    public void Select(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        PageIndex = pageIndex;
    }

    /// <summary>
    /// Selects the preceding page when one exists.
    /// </summary>
    public void Previous()
        => PageIndex = Math.Max(0, PageIndex - 1);

    /// <summary>
    /// Selects the following page without passing the last result page.
    /// </summary>
    /// <param name="totalCount">The non-negative result count.</param>
    public void Next(int totalCount)
        => PageIndex = Math.Min(GetMaximumPage(totalCount), PageIndex + 1);

    /// <summary>
    /// Moves the selected page inside the current result range.
    /// </summary>
    /// <param name="totalCount">The non-negative result count.</param>
    public void Clamp(int totalCount)
        => PageIndex = Math.Min(PageIndex, GetMaximumPage(totalCount));

    /// <summary>
    /// Gets whether results remain after the visible items on the selected page.
    /// </summary>
    /// <param name="totalCount">The non-negative result count.</param>
    /// <param name="visibleCount">The non-negative number of items returned for the selected page.</param>
    /// <returns>Whether a later result page exists.</returns>
    public bool HasNext(int totalCount, int visibleCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        ArgumentOutOfRangeException.ThrowIfNegative(visibleCount);
        return Offset + visibleCount < totalCount;
    }

    /// <summary>
    /// Gets whether a full or partial page follows the selected page.
    /// </summary>
    /// <param name="totalCount">The non-negative result count.</param>
    /// <returns>Whether a later result page exists.</returns>
    public bool HasNext(int totalCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        return Offset + PageSize < totalCount;
    }

    /// <summary>
    /// Gets the last valid zero-based page for a result count.
    /// </summary>
    /// <param name="totalCount">The non-negative result count.</param>
    /// <returns>The last valid page index.</returns>
    public int GetMaximumPage(int totalCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        return Math.Max(0, (totalCount - 1) / PageSize);
    }

    /// <summary>
    /// Gets the items on the selected page.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">All available items.</param>
    /// <returns>A materialized page of items.</returns>
    public IReadOnlyList<T> GetPage<T>(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return [.. items.Skip(Offset).Take(PageSize)];
    }

    /// <summary>
    /// Gets the inclusive one-based item range represented by a visible page.
    /// </summary>
    /// <param name="totalCount">The non-negative result count.</param>
    /// <param name="visibleCount">The non-negative number of visible items.</param>
    /// <returns>The first and last visible item numbers, or zeroes for an empty page.</returns>
    public (int First, int Last) GetRange(int totalCount, int visibleCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        ArgumentOutOfRangeException.ThrowIfNegative(visibleCount);
        if (totalCount == 0 || visibleCount == 0)
            return (0, 0);

        return (Offset + 1, Math.Min(totalCount, Offset + visibleCount));
    }

    /// <summary>
    /// Gets the inclusive one-based item range for the selected page.
    /// </summary>
    /// <param name="totalCount">The non-negative result count.</param>
    /// <returns>The first and last visible item numbers, or zeroes for an empty page.</returns>
    public (int First, int Last) GetRange(int totalCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        int visibleCount = Math.Min(PageSize, Math.Max(0, totalCount - Offset));
        return GetRange(totalCount, visibleCount);
    }
}
