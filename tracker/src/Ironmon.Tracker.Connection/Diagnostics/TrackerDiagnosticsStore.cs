using System.Text.Json;

namespace Ironmon.Tracker.Connection.Diagnostics;

/// <summary>
/// Retains a bounded tracker-owned history of connection and protocol activity.
/// </summary>
public sealed class TrackerDiagnosticsStore
{
    private static readonly JsonSerializerOptions AutomaticSnapshotJsonOptions = new(TrackerJson.Options) { WriteIndented = true };
    private readonly List<TrackerDiagnosticEntry> _entries = [];
    private readonly string? _automaticErrorPath;
    private readonly Lock _persistenceSync = new();
    private readonly Lock _sync = new();
    private string? _lastProtocolError;

    /// <summary>
    /// Initializes an empty diagnostic history.
    /// </summary>
    public TrackerDiagnosticsStore()
    {
    }

    /// <summary>
    /// Initializes an empty diagnostic history with automatic error persistence.
    /// </summary>
    /// <param name="options">The tracker-owned persistence options.</param>
    /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
    public TrackerDiagnosticsStore(TrackerKnowledgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _automaticErrorPath = Path.Combine(options.RootDirectory, TrackerStorageNames.DiagnosticsDirectory, TrackerStorageNames.LatestProtocolErrorFile);
    }

    /// <summary>
    /// Occurs after diagnostic history or the last protocol error changes.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the retained entries in observation order.
    /// </summary>
    public IReadOnlyList<TrackerDiagnosticEntry> Entries
    {
        get
        {
            lock (_sync)
                return [.. _entries];
        }
    }

    /// <summary>
    /// Gets the most recent protocol or connection error.
    /// </summary>
    public string? LastProtocolError
    {
        get
        {
            lock (_sync)
                return _lastProtocolError;
        }
    }

    /// <summary>
    /// Gets the automatic diagnostic snapshot path, or null when persistence is disabled.
    /// </summary>
    public string? AutomaticErrorPath => _automaticErrorPath;

    /// <summary>
    /// Records one incoming validated protocol message.
    /// </summary>
    /// <param name="message">The received protocol message.</param>
    public void RecordIncoming(TrackerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Add(TrackerDiagnosticDirection.Incoming, GetMessageName(message), TrackerMessageCodec.Serialize(message));
    }

    /// <summary>
    /// Records one outgoing validated protocol message.
    /// </summary>
    /// <param name="message">The sent protocol message.</param>
    public void RecordOutgoing(TrackerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Add(TrackerDiagnosticDirection.Outgoing, GetMessageName(message), TrackerMessageCodec.Serialize(message));
    }

    /// <summary>
    /// Records one connection lifecycle transition.
    /// </summary>
    /// <param name="name">The concise transition name.</param>
    /// <param name="detail">The transition detail.</param>
    public void RecordLifecycle(string name, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(detail);
        Add(TrackerDiagnosticDirection.Lifecycle, name, detail);
    }

    /// <summary>
    /// Records the most recent non-fatal protocol or connection error.
    /// </summary>
    /// <param name="exception">The observed exception.</param>
    public void RecordError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        string detail = $"{exception.GetType().Name}: {exception.Message}";
        lock (_sync)
            _lastProtocolError = detail;

        Add(TrackerDiagnosticDirection.Lifecycle, TrackerDiagnosticConstants.Error, detail);
        PersistError(exception);
    }

    /// <summary>
    /// Clears retained entries and the last error.
    /// </summary>
    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
            _lastProtocolError = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Adds one bounded history entry.
    /// </summary>
    /// <param name="direction">The entry source and direction.</param>
    /// <param name="name">The concise entry name.</param>
    /// <param name="rawValue">The raw entry value.</param>
    private void Add(TrackerDiagnosticDirection direction, string name, string rawValue)
    {
        TrackerDiagnosticEntry entry = new(DateTimeOffset.UtcNow, direction, name, rawValue);
        lock (_sync)
        {
            if (_entries.Count >= TrackerDiagnosticConstants.EntryLimit)
                _entries.RemoveAt(0);

            _entries.Add(entry);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Atomically persists the latest error and retained protocol history without masking the original failure.
    /// </summary>
    /// <param name="exception">The observed exception.</param>
    private void PersistError(Exception exception)
    {
        if (_automaticErrorPath is null)
            return;

        IReadOnlyList<TrackerDiagnosticEntry> entries;
        lock (_sync)
            entries = [.. _entries];

        TrackerAutomaticDiagnosticSnapshot snapshot = new(DateTimeOffset.UtcNow, exception.ToString(), entries);
        lock (_persistenceSync)
        {
            try
            {
                string directory = Path.GetDirectoryName(_automaticErrorPath) ?? throw new IOException("The automatic diagnostic path has no parent directory.");
                Directory.CreateDirectory(directory);
                string temporaryPath = _automaticErrorPath + TrackerStorageNames.TemporaryExtension;
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, AutomaticSnapshotJsonOptions));
                File.Move(temporaryPath, _automaticErrorPath, true);
            }
            catch (Exception)
            {
                // Diagnostic persistence must never replace the protocol failure it is trying to preserve.
            }
        }
    }

    /// <summary>
    /// Gets the stable display name for one protocol message.
    /// </summary>
    /// <param name="message">The protocol message.</param>
    /// <returns>The event, command, or response name.</returns>
    private static string GetMessageName(TrackerMessage message) => message.Type switch
    {
        TrackerMessageType.Event => message.Event ?? TrackerDiagnosticConstants.EventFallback,
        TrackerMessageType.Request => message.Command ?? TrackerDiagnosticConstants.RequestFallback,
        TrackerMessageType.Response => message.Success == true ? TrackerDiagnosticConstants.Response : TrackerDiagnosticConstants.ErrorResponse,
        _ => TrackerDiagnosticConstants.MessageFallback
    };
}
