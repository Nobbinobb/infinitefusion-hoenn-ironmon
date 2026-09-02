namespace Ironmon.Tracker.Connection.Sprites;

/// <summary>
/// Summarizes one completed custom sprite-sheet synchronization attempt.
/// </summary>
/// <remarks>Initializes an immutable synchronization result.</remarks>
/// <param name="DownloadedSheetCount">The missing or changed sheets downloaded from the official endpoint.</param>
/// <param name="UnchangedSheetCount">The local sheets confirmed current without downloading their content.</param>
/// <param name="FailedSheetCount">The sheets that could not be checked or downloaded.</param>
/// <param name="DownloadedBytes">The total response bytes promoted into the installation.</param>
/// <param name="UnavailableSheetCount">The missing sheets confirmed unavailable with HTTP 404.</param>
public sealed record CustomSpriteInstallResult(int DownloadedSheetCount, int UnchangedSheetCount, int FailedSheetCount, long DownloadedBytes, int UnavailableSheetCount = 0);
