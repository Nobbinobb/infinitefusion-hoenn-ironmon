using Ironmon.Tracker.Collection;
using Ironmon.Tracker.Connection.Areas;
using Ironmon.Tracker.Connection.CompletedRuns;
using Ironmon.Tracker.Connection.Diagnostics;
using Ironmon.Tracker.Connection.Knowledge;
using Ironmon.Tracker.Connection.RunState;
using Ironmon.Tracker.Connection.Transport;
using System.Diagnostics;
using System.Text.Json;

namespace Ironmon.Collection.SocketHost;

/// <summary>
/// Hosts the real loopback tracker receiver for a disposable bundled-Ruby integration rehearsal.
/// </summary>
internal static class Program
{
    private const string _temporarySuffix = ".tmp";
    private const string _summarySuffix = ".summary.json";
    private const string _trackerVersion = "collection-integration-test";
    private const int _deadlineSeconds = 120;

    /// <summary>
    /// Starts the actual receiver, publishes its port, and exits after the exact stop marker or deadline.
    /// </summary>
    /// <param name="args">Profile directory, ready file and stop file.</param>
    /// <returns>Zero after orderly requested shutdown, or two when the test deadline expires.</returns>
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 3)
            throw new ArgumentException("Expected profileDirectory, readyFile and stopFile arguments.");

        string profile = Path.GetFullPath(args[0]);
        string ready = Path.GetFullPath(args[1]);
        string stop = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(ready)!);
        TrackerKnowledgeOptions storage = new(profile);
        TrackerDiagnosticsStore diagnostics = new();
        TrackerConnectionState state = new();
        TrackerConnectionOptions options = new(0, _trackerVersion, false, TimeSpan.FromSeconds(10));
        await using GameplayCollectionStore collection = new(profile);
        await collection.GetMeasurementsAsync();
        await using TrackerConnectionService connection = new(options, diagnostics, state, new TrackerRunState(), new TrackerKnowledgeStore(storage), new AreaDiscoveryStore(storage), new CompletedRunArchive(storage), collection: collection);
        connection.Start();
        if (connection.BoundPort == 0)
            throw new InvalidOperationException("The disposable tracker receiver did not bind a loopback port.");

        string readiness = JsonSerializer.Serialize(new { port = connection.BoundPort, process_id = Environment.ProcessId });
        await File.WriteAllTextAsync(ready + _temporarySuffix, readiness);
        File.Move(ready + _temporarySuffix, ready, true);
        Stopwatch elapsed = Stopwatch.StartNew();
        while (!File.Exists(stop) && elapsed.Elapsed < TimeSpan.FromSeconds(_deadlineSeconds))
            await Task.Delay(100);

        bool timedOut = !File.Exists(stop);
        await connection.StopAsync();
        JsonElement measurements = await collection.GetMeasurementsAsync();
        var earned = await collection.GetAchievementsAsync();
        var pending = await collection.GetAchievementsAsync(true);
        string summary = JsonSerializer.Serialize(new { timed_out = timedOut, last_protocol_error = diagnostics.LastProtocolError, diagnostic_entry_count = diagnostics.Entries.Count, collection_error = collection.LastError, unlocked_achievement_numbers = earned.Select(item => item.Number), pending_notices = pending.Count, measurements });
        await File.WriteAllTextAsync(ready + _summarySuffix, summary);
        Console.WriteLine(summary);
        return timedOut ? 2 : 0;
    }
}
