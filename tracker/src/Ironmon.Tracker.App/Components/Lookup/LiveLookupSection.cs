namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Identifies one active-run Lookup section.
/// </summary>
internal enum LiveLookupSection
{
    /// <summary>
    /// Trainer-area progress.
    /// </summary>
    Trainers = 0,

    /// <summary>
    /// Wild encounter-area progress.
    /// </summary>
    Encounters = 1,

    /// <summary>
    /// Ground-item-area progress.
    /// </summary>
    Items = 2,

    /// <summary>
    /// Aggregate attacking-type coverage.
    /// </summary>
    TypeCoverage = 3
}
