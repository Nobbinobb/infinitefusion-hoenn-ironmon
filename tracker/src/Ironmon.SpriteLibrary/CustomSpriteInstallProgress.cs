namespace Ironmon.SpriteLibrary;

/// <summary>
/// Reports custom sprite-sheet synchronization progress.
/// </summary>
/// <remarks>
/// Captures completed sheet work independently of the disposable installer or tracker UI.
/// </remarks>
/// <param name="CompletedSheetCount">The number of completed sheet operations.</param>
/// <param name="TotalSheetCount">The total number of selected sheet operations.</param>
/// <param name="DownloadedBytes">The bytes downloaded during this synchronization.</param>
/// <param name="FailedSheetCount">The sheet operations that can be retried.</param>
/// <param name="UnavailableSheetCount">The resources confirmed as unavailable by the sprite server.</param>
public sealed record CustomSpriteInstallProgress(int CompletedSheetCount, int TotalSheetCount, long DownloadedBytes, int FailedSheetCount, int UnavailableSheetCount = 0);
