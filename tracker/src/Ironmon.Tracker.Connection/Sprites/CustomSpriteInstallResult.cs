namespace Ironmon.Tracker.Connection.Sprites;

/// <summary>
/// Summarizes one completed custom sprite-sheet installation attempt.
/// </summary>
public sealed record CustomSpriteInstallResult(int DownloadedSheetCount, int FailedSheetCount, long DownloadedBytes);
