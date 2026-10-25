using System.Text.Json;

namespace Ironmon.Tracker.Connection.Diagnostics;

/// <summary>
/// Builds capability-filtered tracker-owned diagnostic reports.
/// </summary>
public static class TrackerDiagnosticReportBuilder
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new(TrackerJson.Options) { WriteIndented = true };

    /// <summary>
    /// Serializes only the tracker diagnostic groups authorized by the supplied access snapshot.
    /// </summary>
    /// <param name="access">The current tracker-owned diagnostic access.</param>
    /// <param name="connection">The raw connection snapshot.</param>
    /// <param name="runState">The raw tracker-run snapshot.</param>
    /// <param name="knowledge">The persisted knowledge snapshot.</param>
    /// <param name="lastProtocolError">The latest retained protocol error.</param>
    /// <param name="protocolHistory">The retained protocol history.</param>
    /// <param name="generatedAt">The report generation time.</param>
    /// <returns>The formatted diagnostic report JSON.</returns>
    public static string Build(DiagnosticAccessSnapshot access, object connection, object runState, object knowledge, string? lastProtocolError, IReadOnlyList<TrackerDiagnosticEntry> protocolHistory, DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(runState);
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(protocolHistory);

        Dictionary<string, object?> report = new() { ["generated_at"] = generatedAt };
        if (access.HasCapability(DiagnosticCapabilities.TrackerRawState))
        {
            report["connection"] = connection;
            report["run_state"] = runState;
        }

        if (access.HasCapability(DiagnosticCapabilities.TrackerPersistedKnowledge))
            report["knowledge"] = knowledge;

        if (access.HasCapability(DiagnosticCapabilities.TrackerProtocolHistory))
        {
            report["last_protocol_error"] = lastProtocolError;
            report["protocol_history"] = protocolHistory;
        }

        return JsonSerializer.Serialize(report, ReportJsonOptions);
    }
}
