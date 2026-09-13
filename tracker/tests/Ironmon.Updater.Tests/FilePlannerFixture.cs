using System.Security.Cryptography;
using Ironmon.Updater.Core;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Supplies small exact byte fingerprints and complete directory snapshots for planner tests.
/// </summary>
internal static class FilePlannerFixture
{
    internal const string GamePath = "Data/Scripts/game.rb";
    internal const string OtherPath = "Data/Scripts/other.rb";
    internal const string ExtraPath = "Data/Scripts/personal.rb";
    internal const string ScriptPath = "Data/Scripts/997_Ironmon/001_Core.rb";
    internal const string ProfilePath = "Data/Ironmon/generation_profiles/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/profile.json";

    /// <summary>
    /// Hashes a one-byte fixture payload; zero is reserved for absence in test tables.
    /// </summary>
    /// <param name="value">The nonzero fixture byte.</param>
    /// <returns>The exact fixture content fingerprint.</returns>
    internal static GameFileContent Content(int value)
        => new(1, Convert.ToHexString(SHA256.HashData([(byte)value])));

    /// <summary>
    /// Creates one authenticated synthetic game-file entry.
    /// </summary>
    /// <param name="path">The relative file path.</param>
    /// <param name="value">The file's one-byte payload.</param>
    /// <returns>The managed fixture file.</returns>
    internal static ManagedFile File(string path, int value)
        => new(path, Content(value), ManagedFileOwner.Game);

    /// <summary>
    /// Creates a file snapshot with all explicit ancestor directories.
    /// </summary>
    /// <param name="files">Relative file paths and their fixture payloads.</param>
    /// <returns>A sorted complete snapshot.</returns>
    internal static LocalFileEntry[] Snapshot(params (string Path, int Value)[] files)
    {
        var entries = new Dictionary<string, LocalFileEntry>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            entries[file.Path] = new LocalFileEntry(file.Path, Content(file.Value));
            for (var slash = file.Path.LastIndexOf('/'); slash >= 0; slash = file.Path.LastIndexOf('/', slash - 1))
                entries.TryAdd(file.Path[..slash], new LocalFileEntry(file.Path[..slash], null));
        }

        return [.. entries.Values.OrderBy(entry => entry.Path, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Creates a planner with mandatory application-owned protections.
    /// </summary>
    /// <returns>The isolated pure planner.</returns>
    internal static FileUpdatePlanner Planner()
        => new(new FileManagementPolicy());
}
