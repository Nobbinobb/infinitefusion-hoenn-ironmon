namespace Ironmon.Tracker.Tests;

/// <summary>
/// Provides machine-independent filesystem paths for tracker tests.
/// </summary>
internal static class TrackerTestPaths
{
    private const string _gameDirectoryName = "Game";
    private const string _testDirectoryName = "IronmonTrackerTests";

    /// <summary>
    /// Gets a platform-native game root suitable for protocol fixtures.
    /// </summary>
    internal static string GameRoot { get; } = Path.Combine(Path.GetTempPath(), _testDirectoryName, _gameDirectoryName);
}
