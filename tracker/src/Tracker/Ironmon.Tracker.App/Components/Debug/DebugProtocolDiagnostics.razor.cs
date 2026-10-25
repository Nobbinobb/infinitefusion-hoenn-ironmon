using Microsoft.AspNetCore.Components;
using System.Text.Json;

namespace Ironmon.Tracker.App.Components.Debug;

/// <summary>
/// Presents tracker-owned connection, state, knowledge, and raw protocol diagnostics.
/// </summary>
public partial class DebugProtocolDiagnostics : IDisposable
{
    private const string _timeFormat = "HH:mm:ss.fff";
    private static readonly JsonSerializerOptions DisplayJsonOptions = new(TrackerJson.Options) { WriteIndented = true };
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
    /// Gets or initializes tracker-owned diagnostic access.
    /// </summary>
    [Inject]
    private DiagnosticAccessService AccessService { get; set; } = null!;

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
        AccessService.Changed += HandleStateChanged;
    }

    /// <summary>
    /// Refreshes serialized tracker-owned diagnostic values.
    /// </summary>
    private void Refresh()
    {
        _entries = HasProtocolHistory ? Diagnostics.Entries : [];
        _lastProtocolError = HasProtocolHistory ? Diagnostics.LastProtocolError : null;
        _connectionJson = HasRawState ? Serialize(ConnectionState.Snapshot) : "{}";
        _runStateJson = HasRawState ? Serialize(RunState.Snapshot) : "{}";
        _knowledgeJson = HasPersistedKnowledge ? Serialize(Knowledge.GetDiagnosticSnapshot()) : "{}";
    }

    /// <summary>
    /// Copies the complete diagnostic report to the system clipboard.
    /// </summary>
    /// <returns>A task representing the clipboard operation.</returns>
    private Task CopyReportAsync()
        => HasAnyDiagnosticGroup ? CopyAsync(BuildReport(), Text["Debug.Protocol.DiagnosticReportCopied"]) : Task.CompletedTask;

    /// <summary>
    /// Copies the current connection state to the system clipboard.
    /// </summary>
    /// <returns>A task representing the clipboard operation.</returns>
    private Task CopyConnectionAsync()
        => HasRawState ? CopyAsync(_connectionJson, Text["Debug.Protocol.ConnectionStateCopied"]) : Task.CompletedTask;

    /// <summary>
    /// Copies the current tracker run state to the system clipboard.
    /// </summary>
    /// <returns>A task representing the clipboard operation.</returns>
    private Task CopyRunStateAsync()
        => HasRawState ? CopyAsync(_runStateJson, Text["Debug.Protocol.TrackerStateCopied"]) : Task.CompletedTask;

    /// <summary>
    /// Copies persisted tracker knowledge to the system clipboard.
    /// </summary>
    /// <returns>A task representing the clipboard operation.</returns>
    private Task CopyKnowledgeAsync()
        => HasPersistedKnowledge ? CopyAsync(_knowledgeJson, Text["Debug.Protocol.KnowledgeCopied"]) : Task.CompletedTask;

    /// <summary>
    /// Copies one raw history entry to the system clipboard.
    /// </summary>
    /// <param name="entry">The selected diagnostic entry.</param>
    /// <returns>A task representing the clipboard operation.</returns>
    private Task CopyEntryAsync(TrackerDiagnosticEntry entry)
        => HasProtocolHistory ? CopyAsync(entry.RawValue, Text["Debug.Protocol.EntryCopied"]) : Task.CompletedTask;

    /// <summary>
    /// Exports the complete diagnostic report to tracker-owned local storage.
    /// </summary>
    /// <returns>A task representing the export operation.</returns>
    private async Task ExportReportAsync()
    {
        if (!HasAnyDiagnosticGroup)
            return;

        try
        {
            string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string directory = Path.Combine(localData, TrackerStorageNames.RootDirectory, TrackerStorageNames.DiagnosticsDirectory);
            Directory.CreateDirectory(directory);
            string timestamp = DateTimeOffset.Now.ToString(TrackerStorageNames.DiagnosticTimestampFormat, System.Globalization.CultureInfo.InvariantCulture);
            string fileName = $"{TrackerStorageNames.DiagnosticFilePrefix}{timestamp}{TrackerStorageNames.JsonExtension}";
            string path = Path.Combine(directory, fileName);
            await File.WriteAllTextAsync(path, BuildReport());
            _status = Text["Debug.Protocol.ExportedTo", path];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _status = Text["Debug.Protocol.ExportFailed", exception.Message];
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
            _status = Text["Debug.Protocol.CopyFailed", exception.Message];
        }
    }

    /// <summary>
    /// Clears retained connection and protocol history.
    /// </summary>
    private void ClearHistory()
    {
        if (!HasProtocolHistory)
            return;

        Diagnostics.Clear();
        _status = Text["Debug.Protocol.DiagnosticHistoryCleared"];
    }

    /// <summary>
    /// Builds one complete self-contained diagnostic report.
    /// </summary>
    /// <returns>The formatted diagnostic report JSON.</returns>
    private string BuildReport()
        => TrackerDiagnosticReportBuilder.Build(AccessService.Snapshot, ConnectionState.Snapshot, RunState.Snapshot, Knowledge.GetDiagnosticSnapshot(), Diagnostics.LastProtocolError, Diagnostics.Entries, DateTimeOffset.UtcNow);

    /// <summary>
    /// Gets whether connection and protocol history is locally authorized.
    /// </summary>
    private bool HasProtocolHistory
        => AccessService.Snapshot.HasCapability(DiagnosticCapabilities.TrackerProtocolHistory);

    /// <summary>
    /// Gets whether raw connection and run state is locally authorized.
    /// </summary>
    private bool HasRawState
        => AccessService.Snapshot.HasCapability(DiagnosticCapabilities.TrackerRawState);

    /// <summary>
    /// Gets whether persisted tracker knowledge is locally authorized.
    /// </summary>
    private bool HasPersistedKnowledge
        => AccessService.Snapshot.HasCapability(DiagnosticCapabilities.TrackerPersistedKnowledge);

    /// <summary>
    /// Gets whether any report group is locally authorized.
    /// </summary>
    private bool HasAnyDiagnosticGroup
        => HasProtocolHistory || HasRawState || HasPersistedKnowledge;

    /// <summary>
    /// Gets the localized source or direction of a diagnostic entry.
    /// </summary>
    /// <param name="direction">The entry source.</param>
    /// <returns>The localized direction label.</returns>
    private string GetDirectionText(TrackerDiagnosticDirection direction) => direction switch
    {
        TrackerDiagnosticDirection.Incoming => Text["Redesign.Tools.Incoming"],
        TrackerDiagnosticDirection.Outgoing => Text["Redesign.Tools.Outgoing"],
        _ => Text["Redesign.Tools.Lifecycle"]
    };

    /// <summary>
    /// Serializes one diagnostic value as formatted JSON.
    /// </summary>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The formatted JSON.</returns>
    private static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, DisplayJsonOptions);

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
        AccessService.Changed -= HandleStateChanged;
    }
}
