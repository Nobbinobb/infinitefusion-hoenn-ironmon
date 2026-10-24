namespace Ironmon.Tracker.App.Tests;

/// <summary>
/// Identifies tests that require generated game data rather than detached smoke fixtures.
/// </summary>
internal static class TrackerTestCategories
{
    /// <summary>
    /// Gets the xUnit category trait name.
    /// </summary>
    internal const string Category = "Category";

    /// <summary>
    /// Gets the category for tests that construct services backed by generated game catalogs.
    /// </summary>
    internal const string GameCatalog = "GameCatalog";
}
