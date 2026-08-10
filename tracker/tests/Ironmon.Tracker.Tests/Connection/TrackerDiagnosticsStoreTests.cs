using Ironmon.Tracker.Connection;

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

    /// <summary>
    /// Verifies an error atomically preserves its complete detail and preceding protocol history.
    /// </summary>
    [Fact]
    public void ErrorPersistsLatestAutomaticDiagnosticSnapshot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"ironmon-diagnostics-{Guid.NewGuid():N}");
        try
        {
            TrackerDiagnosticsStore store = new(new TrackerKnowledgeOptions(root));
            TrackerMessage request = TrackerMessageFactory.CreateRequest("request-1", "pokemon_lookup", new { Query = "B445H175:0" });
            store.RecordOutgoing(request);
            store.RecordError(new TrackerProtocolException("The tracker message exceeds the framing limit."));

            string path = Path.Combine(root, TrackerStorageNames.DiagnosticsDirectory, TrackerStorageNames.LatestProtocolErrorFile);
            Assert.Equal(path, store.AutomaticErrorPath);
            Assert.True(File.Exists(path));
            string json = File.ReadAllText(path);
            Assert.Contains("TrackerProtocolException", json, StringComparison.Ordinal);
            Assert.Contains("framing limit", json, StringComparison.Ordinal);
            Assert.Contains("pokemon_lookup", json, StringComparison.Ordinal);
            Assert.Contains("B445H175:0", json, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
