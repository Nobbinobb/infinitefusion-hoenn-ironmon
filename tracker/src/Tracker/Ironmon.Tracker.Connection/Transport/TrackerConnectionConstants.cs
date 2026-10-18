namespace Ironmon.Tracker.Connection.Transport;

/// <summary>
/// Defines stable connection implementation limits.
/// </summary>
internal static class TrackerConnectionConstants
{
    /// <summary>
    /// Gets the single-operation semaphore capacity used for ordered protocol writes and requests.
    /// </summary>
    internal const int SingleOperationCapacity = 1;
}
