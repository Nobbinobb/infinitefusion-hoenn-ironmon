namespace Ironmon.Tracker.Tests.Core;

/// <summary>
/// Verifies shared page selection, bounds, ranges, and materialization.
/// </summary>
public sealed class PaginationStateTests
{
    /// <summary>
    /// Initializes pagination state tests.
    /// </summary>
    public PaginationStateTests()
    {
    }

    /// <summary>
    /// Verifies that pagination requires a positive page size.
    /// </summary>
    [Fact]
    public void ConstructorRejectsNonPositivePageSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PaginationState(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PaginationState(-1));
    }

    /// <summary>
    /// Verifies bounded movement across full and partial pages.
    /// </summary>
    [Fact]
    public void MovementRemainsInsideResultBounds()
    {
        PaginationState pagination = new(10);

        pagination.Previous();
        Assert.Equal(0, pagination.PageIndex);

        pagination.Next(21);
        pagination.Next(21);
        pagination.Next(21);
        Assert.Equal(2, pagination.PageIndex);
        Assert.Equal(20, pagination.Offset);
        Assert.False(pagination.HasNext(21, 1));
        Assert.False(pagination.HasNext(21));

        pagination.Clamp(9);
        Assert.Equal(0, pagination.PageIndex);
    }

    /// <summary>
    /// Verifies page materialization and inclusive display ranges.
    /// </summary>
    [Fact]
    public void SelectedPageProvidesItemsAndDisplayRange()
    {
        PaginationState pagination = new(3);
        pagination.Select(1);

        IReadOnlyList<int> page = pagination.GetPage([1, 2, 3, 4, 5]);
        (int first, int last) = pagination.GetRange(5, page.Count);

        Assert.Equal([4, 5], page);
        Assert.Equal(4, first);
        Assert.Equal(5, last);
        Assert.False(pagination.HasNext(5, page.Count));
    }

    /// <summary>
    /// Verifies that empty pages report an empty display range.
    /// </summary>
    [Fact]
    public void EmptyPageReportsZeroRange()
    {
        PaginationState pagination = new(10);

        Assert.Equal((0, 0), pagination.GetRange(0, 0));
    }
}
