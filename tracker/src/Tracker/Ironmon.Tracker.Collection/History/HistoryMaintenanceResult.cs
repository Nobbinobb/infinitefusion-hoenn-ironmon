namespace Ironmon.Tracker.Collection.History;

/// <summary>
/// Reports physical storage measurements and conservative history-maintenance preview savings.
/// </summary>
/// <remarks>
/// Initializes measurements without revealing private achievement conditions.
/// </remarks>
/// <param name="DatabaseBytes">Current database, WAL and shared-memory bytes.</param>
/// <param name="ArchiveBytes">Encrypted archive bytes, measured on a temporary copy during inspection.</param>
/// <param name="RemovedBytes">Signed physical reduction in database, journal and recipe bytes; negative for growth and zero during inspection.</param>
/// <param name="EligibleBytes">Uncompressed eligible history bytes considered by this operation.</param>
/// <param name="AlreadyImported">Whether an authenticated archive was previously imported.</param>
/// <param name="Notice">Optional guidance when newer or locked recipe files were retained.</param>
/// <param name="EstimatedFreedBytes">Conservative net disk bytes recoverable with the archive on the same drive, or null outside inspection.</param>
public sealed record HistoryMaintenanceResult(long DatabaseBytes, long ArchiveBytes, long RemovedBytes, long EligibleBytes, bool AlreadyImported = false, string? Notice = null, long? EstimatedFreedBytes = null);
