namespace Ironmon.Tracker.App.History;

/// <summary>
/// Selects external history archives whose protection is managed automatically by the tracker.
/// </summary>
public interface IHistoryArchivePicker
{
    /// <summary>
    /// Selects an export destination through the native file picker.
    /// </summary>
    /// <returns>The absolute destination or null when cancelled.</returns>
    Task<string?> ChooseExportAsync();

    /// <summary>
    /// Selects an existing encrypted archive through the native file picker.
    /// </summary>
    /// <returns>The absolute archive path or null when cancelled.</returns>
    Task<string?> ChooseImportAsync();
}
