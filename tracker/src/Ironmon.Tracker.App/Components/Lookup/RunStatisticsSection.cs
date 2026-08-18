namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Identifies one completed-run statistics section.
/// </summary>
internal enum RunStatisticsSection
{
    /// <summary>
    /// General attempt progress.
    /// </summary>
    Overview = 0,

    /// <summary>
    /// Trainer encounter results.
    /// </summary>
    Trainers = 1,

    /// <summary>
    /// Item usage and healing.
    /// </summary>
    Items = 2
}
