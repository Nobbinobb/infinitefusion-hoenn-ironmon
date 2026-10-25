using Ironmon.Tracker.Collection.History;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Ironmon.Tracker.Collection.Storage;

/// <summary>
/// Rehearses history maintenance on a disposable consistent database copy to measure physical disk savings.
/// </summary>
internal sealed partial class CollectionDatabase
{
    private const string _historyPreviewPrefix = "IronmonHistoryPreview-";
    private const string _historyPreviewProfile = "profile";
    private const string _historyPreviewArchive = "preview.irmhistory";
    private const string _historyWalSuffix = "-wal";
    private const string _historyShmSuffix = "-shm";
    private const long _historyPreviewReserveBytes = 65536;
    private const long _historyPreviewRoundingBytes = 1000;

    /// <summary>
    /// Measures current physical database and journal lengths without checkpointing the live profile.
    /// </summary>
    /// <param name="databasePath">The exact database filename whose owned sidecars are included.</param>
    /// <returns>The combined database, WAL and shared-memory bytes currently present on disk.</returns>
    private static long HistoryStorageBytes(string databasePath)
    {
        string[] paths = [databasePath, databasePath + _historyWalSuffix, databasePath + _historyShmSuffix];
        return paths.Sum(path => File.Exists(path) ? new FileInfo(path).Length : 0L);
    }

    /// <summary>
    /// Includes archive storage in a conservative estimate while excluding uncertain recipe-deletion savings.
    /// </summary>
    /// <param name="runDirectory">The optional recipe root, read without modifying its files.</param>
    /// <param name="eligibleBytes">The eligible serialized bytes, used only to detect an empty export.</param>
    /// <returns>A review containing physical sizes and rounded-down net savings with a storage-variation reserve.</returns>
    private HistoryMaintenanceResult PreviewHistoryStorage(string? runDirectory, long eligibleBytes)
    {
        long before = HistoryStorageBytes(_databasePath);
        if (eligibleBytes == 0 || _profile is null || _epoch is null)
            return new(before, 0, 0, eligibleBytes, EstimatedFreedBytes: 0);

        DirectoryInfo temporary = Directory.CreateTempSubdirectory(_historyPreviewPrefix);
        string temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!temporary.FullName.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The history preview directory is outside temporary storage.");

        try
        {
            string profileDirectory = Path.Combine(temporary.FullName, _historyPreviewProfile);
            Directory.CreateDirectory(profileDirectory);
            string databasePath = Path.Combine(profileDirectory, _databaseName);
            using (SqliteConnection backup = new(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString()))
            {
                backup.Open();
                _db.BackupDatabase(backup);
            }

            using CollectionDatabase preview = new(profileDirectory);
            string archivePath = Path.Combine(temporary.FullName, _historyPreviewArchive);
            HistoryMaintenanceResult result = preview.ExportHistory(archivePath, runDirectory, false).Deserialize<HistoryMaintenanceResult>()!;
            long estimated = Math.Max(0, before - result.DatabaseBytes - result.ArchiveBytes - _historyPreviewReserveBytes);
            estimated = estimated / _historyPreviewRoundingBytes * _historyPreviewRoundingBytes;
            return new(before, result.ArchiveBytes, 0, eligibleBytes, EstimatedFreedBytes: estimated);
        }
        finally
        {
            temporary.Delete(true);
        }
    }
}
