namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies bounded tracker-owned protocol and connection diagnostics.
/// </summary>
public sealed class TrackerDiagnosticsStoreTests
{
    /// <summary>
    /// Initializes tracker diagnostic store tests.
    /// </summary>
    public TrackerDiagnosticsStoreTests()
    {
    }

    /// <summary>
    /// Verifies history remains bounded and retains canonical raw protocol JSON.
    /// </summary>
    [Fact]
    public void HistoryRetainsNewestTwoHundredEntries()
    {
        TrackerDiagnosticsStore store = new();
        for (int index = 0; index < 205; index++)
        {
            TrackerMessage message = TrackerMessageFactory.CreateEvent("event", index, new { Index = index });
            store.RecordIncoming(message);
        }

        Assert.Equal(200, store.Entries.Count);
        Assert.Contains("\"index\":5", store.Entries[0].RawValue, StringComparison.Ordinal);
        Assert.Contains("\"index\":204", store.Entries[^1].RawValue, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the latest error is retained and clearing removes all diagnostics.
    /// </summary>
    [Fact]
    public void ErrorAndClearUpdateDiagnosticState()
    {
        TrackerDiagnosticsStore store = new();
        store.RecordLifecycle("Listening", "127.0.0.1:38521");
        store.RecordError(new IOException("Connection closed."));

        Assert.Equal("IOException: Connection closed.", store.LastProtocolError);
        Assert.Equal(2, store.Entries.Count);

        store.Clear();

        Assert.Null(store.LastProtocolError);
        Assert.Empty(store.Entries);
    }
}
