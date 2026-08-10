namespace Ironmon.Tracker.Connection.Diagnostics;

/// <summary>
/// Represents the durable diagnostic evidence captured when a protocol or connection error occurs.
/// </summary>
/// <remarks>
/// Initializes one automatic diagnostic snapshot.
/// </remarks>
/// <param name="capturedAt">The UTC time at which the error was captured.</param>
/// <param name="error">The complete exception text, including its stack trace when available.</param>
/// <param name="protocolHistory">The bounded protocol and connection history retained at capture time.</param>
public sealed class TrackerAutomaticDiagnosticSnapshot(DateTimeOffset capturedAt, string error, IReadOnlyList<TrackerDiagnosticEntry> protocolHistory)
{
    /// <summary>
    /// Gets the UTC capture time.
    /// </summary>
    public DateTimeOffset CapturedAt { get; } = capturedAt;

    /// <summary>
    /// Gets the complete exception text, including its stack trace when available.
    /// </summary>
    public string Error { get; } = error;

    /// <summary>
    /// Gets the bounded protocol and connection history retained at capture time.
    /// </summary>
    public IReadOnlyList<TrackerDiagnosticEntry> ProtocolHistory { get; } = protocolHistory;
}
