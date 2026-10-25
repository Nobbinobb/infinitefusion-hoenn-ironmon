namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Identifies one completed-run Archive section.
/// </summary>
internal enum ArchiveSection
{
    /// <summary>
    /// Run statistics and seed export.
    /// </summary>
    Summary = 0,

    /// <summary>
    /// Completed-run world areas.
    /// </summary>
    Areas = 1,

    /// <summary>
    /// Completed-run Pokemon lookup.
    /// </summary>
    Pokemon = 2
}
