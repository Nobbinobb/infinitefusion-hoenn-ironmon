using System.Text.Json;
using Ironmon.Tracker.Collection.History;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Serializes history maintenance with normal collection writes.
/// </summary>
public sealed partial class GameplayCollectionStore
{
    /// <summary>
    /// Rehearses export on a temporary database copy while preserving every live collected row and recipe.
    /// </summary>
    /// <param name="cancellationToken">Cancels queue admission or the caller's wait.</param>
    /// <returns>Current physical storage, measured archive size and conservative net disk savings.</returns>
    public async Task<HistoryMaintenanceResult> InspectHistoryAsync(CancellationToken cancellationToken = default)
    {
        JsonElement result = await EnqueueAsync(database => database.InspectHistory(_historyRunDirectory), cancellationToken).ConfigureAwait(false);
        return result.Deserialize<HistoryMaintenanceResult>()!;
    }

    /// <summary>
    /// Verifies an external archive protected by the tracker before removing unrelated source history.
    /// </summary>
    /// <param name="path">A new archive path outside the active collection folder.</param>
    /// <param name="cancellationToken">Cancels admission or waiting, never a committed export.</param>
    /// <returns>The actual archive size and database bytes reclaimed.</returns>
    public async Task<HistoryMaintenanceResult> ExportHistoryAsync(string path, CancellationToken cancellationToken = default)
    {
        JsonElement result = await EnqueueAsync(database => database.ExportHistory(path, _historyRunDirectory), cancellationToken).ConfigureAwait(false);
        return result.Deserialize<HistoryMaintenanceResult>()!;
    }

    /// <summary>
    /// Restores unrelated history exactly once while merging activity recorded after export.
    /// </summary>
    /// <param name="path">The existing encrypted history archive.</param>
    /// <param name="cancellationToken">Cancels queue admission or waiting, never a committed import.</param>
    /// <returns>Current storage measurements and the duplicate-import outcome.</returns>
    public async Task<HistoryMaintenanceResult> ImportHistoryAsync(string path, CancellationToken cancellationToken = default)
    {
        JsonElement result = await EnqueueAsync(database => database.ImportHistory(path, _historyRunDirectory), cancellationToken).ConfigureAwait(false);
        return result.Deserialize<HistoryMaintenanceResult>()!;
    }
}
