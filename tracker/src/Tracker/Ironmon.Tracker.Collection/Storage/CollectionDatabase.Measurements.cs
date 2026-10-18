using System.Text.Json;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Records operational measurements without storing another gameplay archive.
/// </summary>
internal sealed partial class CollectionDatabase
{

    /// <summary>
    /// Returns persisted growth measurements and current-worker complete transaction timings.
    /// </summary>
    /// <returns>Operational JSON with current storage counts and worker timings; no gameplay records are exported.</returns>
    public JsonElement Measurements()
    {
        JsonElement persisted = GameplayCollectionInspector.Read(Path.GetDirectoryName(_databasePath)!);
        return JsonSerializer.SerializeToElement(new { storage = persisted, worker = new { sqlite_version = _sqliteVersion, accepted_batches = _acceptedBatches, accepted_events = _acceptedEvents, duplicate_events = _duplicateEvents, received_bytes = _receivedBytes, last_apply_commit_ms = _lastApplyMilliseconds, total_apply_commit_ms = _totalApplyMilliseconds, maximum_apply_commit_ms = _maximumApplyMilliseconds } });
    }
}
