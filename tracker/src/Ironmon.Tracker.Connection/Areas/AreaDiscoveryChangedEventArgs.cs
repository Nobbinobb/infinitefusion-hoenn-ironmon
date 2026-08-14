namespace Ironmon.Tracker.Connection.Areas;

/// <summary>
/// Describes one persisted area-discovery change.
/// </summary>
/// <remarks>
/// Initializes one area-discovery change notification.
/// </remarks>
/// <param name="runId">The affected run identifier.</param>
/// <param name="areaId">The affected area identifier.</param>
/// <param name="category">The affected category.</param>
public sealed class AreaDiscoveryChangedEventArgs(string runId, string areaId, AreaContentCategory category) : EventArgs
{
    /// <summary>
    /// Gets the affected run identifier.
    /// </summary>
    public string RunId { get; } = runId;

    /// <summary>
    /// Gets the affected area identifier.
    /// </summary>
    public string AreaId { get; } = areaId;

    /// <summary>
    /// Gets the affected category.
    /// </summary>
    public AreaContentCategory Category { get; } = category;
}
