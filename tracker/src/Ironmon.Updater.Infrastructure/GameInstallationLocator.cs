using Ironmon.Updater.Core;
namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Locates an existing game from a tracker executable and checks its candidate identity without running it.
/// </summary>
public static class GameInstallationLocator
{
    internal const string GameExecutable = "InfiniteFusion2.exe";
    internal const string GameIni = "Game.ini";
    private const string TrackerExecutable = "Ironmon Tracker.exe";
    private const string GameSection = "[Game]";
    private const string TitleKey = "Title";
    private const string ExpectedTitle = "infinitefusion-hoenn";
    private const int MaximumParents = 12;
    private const int MaximumIniBytes = 65536;

    /// <summary>
    /// Finds the nearest ancestor with a valid executable header and unambiguous game INI title.
    /// </summary>
    /// <param name="trackerExecutable">The fully qualified path of the existing installed tracker executable.</param>
    /// <returns>The candidate game root; a baseline inventory must still authenticate its content.</returns>
    public static string FindFromTracker(string trackerExecutable)
    {
        var tracker = PlainPaths.Full(trackerExecutable);
        if (!File.Exists(tracker) || !Path.GetFileName(tracker).Equals(TrackerExecutable, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(UpdaterText.GameInstallationLocatorTheInstalledTrackerExecutableWasNotFound);

        var directory = Path.GetDirectoryName(tracker);
        for (var depth = 0; depth < MaximumParents && directory is not null; depth++, directory = Path.GetDirectoryName(directory))
        {
            if (!Path.Exists(Path.Combine(directory, GameIni)) && !Path.Exists(Path.Combine(directory, GameExecutable)))
                continue;

            ValidateCandidate(directory);
            return directory;
        }

        throw new DirectoryNotFoundException(UpdaterText.GameInstallationLocatorNoSupportedGameWasFoundAboveTheTracker);
    }

    /// <summary>
    /// Checks candidate identity while leaving full content recognition to the trusted inventory.
    /// </summary>
    /// <param name="root">The fully qualified candidate game directory.</param>
    public static void ValidateCandidate(string root)
    {
        var ini = PlainPaths.Child(root, GameIni);
        var executable = PlainPaths.Child(root, GameExecutable);
        if (!File.Exists(ini) || new FileInfo(ini).Length > MaximumIniBytes || !File.Exists(executable))
            throw new InvalidDataException(UpdaterText.GameInstallationLocatorTheFolderDoesNotContainTheExpectedGameExecutable);

        using var input = File.OpenRead(executable);
        if (input.ReadByte() != 'M' || input.ReadByte() != 'Z')
            throw new InvalidDataException(UpdaterText.GameInstallationLocatorTheGameExecutableHasAnUnsupportedHeader);

        var inGame = false;
        var sections = 0;
        string? title = null;
        foreach (var raw in File.ReadLines(ini))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                continue;

            if (line.StartsWith('['))
            {
                inGame = line.Equals(GameSection, StringComparison.OrdinalIgnoreCase);
                if (inGame && ++sections > 1)
                    throw new InvalidDataException(UpdaterText.GameInstallationLocatorTheGameINIHasDuplicateGameSections);

                continue;
            }

            var pair = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (!inGame || pair.Length != 2 || !pair[0].Equals(TitleKey, StringComparison.OrdinalIgnoreCase))
                continue;

            if (title is not null)
                throw new InvalidDataException(UpdaterText.GameInstallationLocatorTheGameINIHasDuplicateTitles);

            title = pair[1];
        }

        if (title != ExpectedTitle)
            throw new InvalidDataException(UpdaterText.GameInstallationLocatorTheGameINIDoesNotIdentifyTheSupportedInfinite);
    }
}
