using Ironmon.Tracker.Protocol;

namespace Ironmon.Tracker.Connection;

/// <summary>
/// Retains a bounded tracker-owned history of connection and protocol activity.
/// </summary>
public sealed class TrackerDiagnosticsStore
{
    private const int EntryLimit = 200;
    private readonly List<TrackerDiagnosticEntry> _entries = [];
    private readonly object _sync = new();
    private string? _lastProtocolError;

    /// <summary>
    /// Initializes an empty diagnostic history.
    /// </summary>
    public TrackerDiagnosticsStore()
    {
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

        Add(TrackerDiagnosticDirection.Lifecycle, "Error", detail);
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
            if (_entries.Count >= EntryLimit)
                _entries.RemoveAt(0);

            _entries.Add(entry);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gets the stable display name for one protocol message.
    /// </summary>
    /// <param name="message">The protocol message.</param>
    /// <returns>The event, command, or response name.</returns>
    private static string GetMessageName(TrackerMessage message) => message.Type switch
    {
        TrackerMessageType.Event => message.Event ?? "event",
        TrackerMessageType.Request => message.Command ?? "request",
        TrackerMessageType.Response => message.Success == true ? "response" : "error_response",
        _ => "message"
    };
}
