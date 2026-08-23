namespace Ironmon.Tracker.Tests.Core;

/// <summary>
/// Verifies two-direction navigation history and delayed commits.
/// </summary>
public sealed class NavigationHistoryTests
{
    /// <summary>
    /// Initializes navigation history tests.
    /// </summary>
    public NavigationHistoryTests()
    {
    }

    /// <summary>
    /// Verifies that peeking leaves history unchanged until navigation succeeds.
    /// </summary>
    [Fact]
    public void BackNavigationCommitsAfterSuccessfulLoad()
    {
        NavigationHistory<string> history = new();
        history.RecordNavigation("bulbasaur");

        Assert.True(history.TryPeekBack(out string? target));
        Assert.Equal("bulbasaur", target);
        Assert.True(history.CanGoBack);
        Assert.False(history.CanGoForward);

        history.CommitBack("ivysaur");

        Assert.False(history.CanGoBack);
        Assert.True(history.TryPeekForward(out target));
        Assert.Equal("ivysaur", target);
    }

    /// <summary>
    /// Verifies that forward navigation restores the previous backward entry.
    /// </summary>
    [Fact]
    public void ForwardNavigationRestoresBackHistory()
    {
        NavigationHistory<string> history = new();
        history.RecordNavigation("bulbasaur");
        history.CommitBack("ivysaur");

        Assert.True(history.TryPeekForward(out string? target));
        Assert.Equal("ivysaur", target);

        history.CommitForward("bulbasaur");

        Assert.True(history.TryPeekBack(out target));
        Assert.Equal("bulbasaur", target);
        Assert.False(history.CanGoForward);
    }

    /// <summary>
    /// Verifies that deliberate navigation starts a new forward-history branch.
    /// </summary>
    [Fact]
    public void NewNavigationClearsForwardHistory()
    {
        NavigationHistory<string> history = new();
        history.RecordNavigation("bulbasaur");
        history.CommitBack("ivysaur");

        history.RecordNavigation("bulbasaur");

        Assert.False(history.CanGoForward);
        Assert.True(history.CanGoBack);
    }

    /// <summary>
    /// Verifies that clearing removes targets in both directions.
    /// </summary>
    [Fact]
    public void ClearRemovesAllHistory()
    {
        NavigationHistory<string> history = new();
        history.RecordNavigation("bulbasaur");
        history.CommitBack("ivysaur");

        history.Clear();

        Assert.False(history.CanGoBack);
        Assert.False(history.CanGoForward);
    }
}
