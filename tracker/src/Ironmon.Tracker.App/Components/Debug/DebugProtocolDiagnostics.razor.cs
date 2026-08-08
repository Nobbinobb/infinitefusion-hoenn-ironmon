using Microsoft.AspNetCore.Components;
using System.Text.Json;

namespace Ironmon.Tracker.App.Components.Debug;

/// <summary>
/// Presents tracker-owned connection, state, knowledge, and raw protocol diagnostics.
/// </summary>
public partial class DebugProtocolDiagnostics : IDisposable
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new(TrackerJson.Options) { WriteIndented = true };
    private IReadOnlyList<TrackerDiagnosticEntry> _entries = [];
    private string _connectionJson = "{}";
    private string _runStateJson = "{}";
    private string _knowledgeJson = "{}";
    private string? _lastProtocolError;
    private string? _status;

    /// <summary>
    /// Gets or initializes the connection diagnostic history.
    /// </summary>
    [Inject]
    private TrackerDiagnosticsStore Diagnostics { get; set; } = null!;

    /// <summary>
    /// Gets or initializes the shared connection state.
    /// </summary>
    [Inject]
    private TrackerConnectionState ConnectionState { get; set; } = null!;

    /// <summary>
    /// Gets or initializes the shared live run state.
    /// </summary>
    [Inject]
    private TrackerRunState RunState { get; set; } = null!;

    /// <summary>
    /// Gets or initializes tracker-owned persisted knowledge.
    /// </summary>
    [Inject]
    private TrackerKnowledgeStore Knowledge { get; set; } = null!;

    /// <summary>
    /// Loads diagnostic values and subscribes to their stores.
    /// </summary>
    protected override void OnInitialized()
    {
        Refresh();
        Diagnostics.Changed += HandleDiagnosticsChanged;
        ConnectionState.Changed += HandleStateChanged;
        RunState.Changed += HandleStateChanged;
        Knowledge.Changed += HandleStateChanged;
    }

    /// <summary>
    /// Refreshes serialized tracker-owned diagnostic values.
    /// </summary>
    private void Refresh()
    {
        _entries = Diagnostics.Entries;
        _lastProtocolError = Diagnostics.LastProtocolError;
        _connectionJson = Serialize(ConnectionState.Snapshot);
        _runStateJson = Serialize(RunState.Snapshot);
        _knowledgeJson = Serialize(Knowledge.GetDiagnosticSnapshot());
    }

    /// <summary>
    /// Copies the complete diagnostic report to the system clipboard.
    /// </summary>
    /// <returns>A task representing the clipboard operation.</returns>
    private Task CopyReportAsync()
        => CopyAsync(BuildReport(), "Diagnostic report copied.");

    /// <summary>
    /// Copies the current connection state to the system clipboard.
    /// </summary>
    /// <returns>A task representing the clipboard operation.</returns>
    private Task CopyConnectionAsync()
        => CopyAsync(_connectionJson, "Connection state copied.");

    /// <summary>
    /// Copies the current tracker run state to the system clipboard.
    /// </summary>
    /// <returns>A task representing the clipboard operation.</returns>
    private Task CopyRunStateAsync()
        => CopyAsync(_runStateJson, "Tracker state copied.");

    /// <summary>
    /// Copies persisted tracker knowledge to the system clipboard.
    /// </summary>
    /// <returns>A task representing the clipboard operation.</returns>
    private Task CopyKnowledgeAsync()
        => CopyAsync(_knowledgeJson, "Knowledge copied.");

    /// <summary>
    /// Copies one raw history entry to the system clipboard.
    /// </summary>
    /// <param name="entry">The selected diagnostic entry.</param>
    /// <returns>A task representing the clipboard operation.</returns>
    private Task CopyEntryAsync(TrackerDiagnosticEntry entry)
        => CopyAsync(entry.RawValue, "Entry copied.");

    /// <summary>
    /// Exports the complete diagnostic report to tracker-owned local storage.
    /// </summary>
    /// <returns>A task representing the export operation.</returns>
    private async Task ExportReportAsync()
    {
        try
        {
            string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string directory = Path.Combine(localData, "IronmonTracker", "diagnostics");
            Directory.CreateDirectory(directory);
            string fileName = $"ironmon-diagnostic-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json";
            string path = Path.Combine(directory, fileName);
            await File.WriteAllTextAsync(path, BuildReport());
            _status = $"Exported to {path}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _status = $"Export failed: {exception.Message}";
        }
    }

    /// <summary>
    /// Copies one diagnostic value to the system clipboard.
    /// </summary>
    /// <param name="value">The value to copy.</param>
    /// <param name="successStatus">The status shown after success.</param>
    /// <returns>A task representing the clipboard operation.</returns>
    private async Task CopyAsync(string value, string successStatus)
    {
        try
        {
            await Clipboard.Default.SetTextAsync(value);
            _status = successStatus;
        }
        catch (Exception exception)
        {
            _status = $"Copy failed: {exception.Message}";
        }
    }

    /// <summary>
    /// Clears retained connection and protocol history.
    /// </summary>
    private void ClearHistory()
    {
        Diagnostics.Clear();
        _status = "Diagnostic history cleared.";
    }

    /// <summary>
    /// Builds one complete self-contained diagnostic report.
    /// </summary>
    /// <returns>The formatted diagnostic report JSON.</returns>
    private string BuildReport()
    {
        object report = new
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Connection = ConnectionState.Snapshot,
            RunState = RunState.Snapshot,
            Knowledge = Knowledge.GetDiagnosticSnapshot(),
            Diagnostics.LastProtocolError,
            ProtocolHistory = Diagnostics.Entries
        };

        return JsonSerializer.Serialize(report, ReportJsonOptions);
    }

    /// <summary>
    /// Serializes one diagnostic value as formatted JSON.
    /// </summary>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The formatted JSON.</returns>
    private static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, ReportJsonOptions);

    /// <summary>
    /// Refreshes the component after diagnostic history changes.
    /// </summary>
    /// <param name="sender">The store raising the event.</param>
    /// <param name="args">The change event arguments.</param>
    private void HandleDiagnosticsChanged(object? sender, EventArgs args)
    {
        Refresh();
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Refreshes the component after tracker state or knowledge changes.
    /// </summary>
    /// <param name="sender">The store raising the event.</param>
    /// <param name="args">The change event arguments.</param>
    private void HandleStateChanged(object? sender, EventArgs args)
    {
        Refresh();
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Removes tracker-owned diagnostic subscriptions.
    /// </summary>
    public void Dispose()
    {
        Diagnostics.Changed -= HandleDiagnosticsChanged;
        ConnectionState.Changed -= HandleStateChanged;
        RunState.Changed -= HandleStateChanged;
        Knowledge.Changed -= HandleStateChanged;
    }
}
