namespace Ironmon.Tracker.Connection.Sprites;

/// <summary>
/// Reports custom sprite-sheet synchronization progress.
/// </summary>
public sealed record CustomSpriteInstallProgress(int CompletedSheetCount, int TotalSheetCount, long DownloadedBytes, int FailedSheetCount, int UnavailableSheetCount = 0);
