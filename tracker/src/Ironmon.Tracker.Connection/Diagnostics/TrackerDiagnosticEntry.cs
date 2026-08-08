namespace Ironmon.Tracker.Connection.Diagnostics;

/// <summary>
/// Represents one bounded connection or protocol diagnostic entry.
/// </summary>
/// <remarks>
/// Initializes one immutable diagnostic entry.
/// </remarks>
/// <param name="timestamp">The UTC time at which the entry was observed.</param>
/// <param name="direction">The entry source and protocol direction.</param>
/// <param name="name">The concise lifecycle, event, request, or response name.</param>
/// <param name="rawValue">The raw message JSON or lifecycle detail.</param>
public sealed class TrackerDiagnosticEntry(DateTimeOffset timestamp, TrackerDiagnosticDirection direction, string name, string rawValue)
{
    /// <summary>
    /// Gets the UTC observation time.
    /// </summary>
    public DateTimeOffset Timestamp { get; } = timestamp;

    /// <summary>
    /// Gets the entry source and direction.
    /// </summary>
    public TrackerDiagnosticDirection Direction { get; } = direction;

    /// <summary>
    /// Gets the concise entry name.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Gets the raw message JSON or lifecycle detail.
    /// </summary>
    public string RawValue { get; } = rawValue;
}
