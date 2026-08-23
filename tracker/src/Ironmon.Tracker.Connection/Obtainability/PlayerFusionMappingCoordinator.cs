using System.Collections.Concurrent;

namespace Ironmon.Tracker.Connection.Obtainability;

/// <summary>
/// Caches parallel player-fusion jobs and returns bounded result batches to the game.
/// </summary>
internal sealed class PlayerFusionMappingCoordinator
{
    private const int BackgroundBatchSize = 128;
    private const int ForegroundBatchSize = 8_192;
    private readonly PlayerFusionMappingWorkerCatalog _catalog;
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<PlayerFusionMappedPair>>> _jobs = new(StringComparer.Ordinal);
    private readonly PlayerFusionMappingWorker _worker;

    /// <summary>
    /// Initializes a coordinator from the embedded generated mapping catalog.
    /// </summary>
    internal PlayerFusionMappingCoordinator()
    {
        _catalog = PlayerFusionMappingWorkerCatalog.Load();
        _worker = new PlayerFusionMappingWorker(_catalog);
    }

    /// <summary>
    /// Gets the number of mapping jobs currently retained for batched delivery.
    /// </summary>
    internal int CachedJobCount => _jobs.Count;

    /// <summary>
    /// Calculates or reuses one compatible job and creates the next bounded result batch.
    /// </summary>
    /// <param name="work">The game-owned job description.</param>
    /// <param name="foreground">Whether an open dependent view permits an aggressive batch.</param>
    /// <param name="cancellationToken">The token that cancels waiting for the worker.</param>
    /// <returns>The next result batch, or null when the embedded catalog is incompatible.</returns>
    internal async Task<PlayerFusionMappingBatchPayload?> CreateBatchAsync(PlayerFusionMappingWorkPayload work, bool foreground, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (!Compatible(work))
            return null;

        Task<IReadOnlyList<PlayerFusionMappedPair>> job = _jobs.GetOrAdd(work.JobId, _ => Task.Run(
            () => _worker.MapAll(work.Seed, work.GeneratorVersion, work.MaterialIds, CancellationToken.None),
            CancellationToken.None));

        IReadOnlyList<PlayerFusionMappedPair> mappings = await job.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (mappings.Count != work.TotalPairs || work.ProcessedPairs < 0 || work.ProcessedPairs > mappings.Count)
            throw new InvalidDataException("The player-fusion mapping job no longer matches the game-owned pair range.");

        int end = Math.Min(work.ProcessedPairs + (foreground ? ForegroundBatchSize : BackgroundBatchSize), mappings.Count);
        List<int> packedPairs = new((end - work.ProcessedPairs) * 4);
        for (int index = work.ProcessedPairs; index < end; index++)
        {
            PlayerFusionMappedPair mapping = mappings[index];
            packedPairs.Add(mapping.FirstMaterialId);
            packedPairs.Add(mapping.SecondMaterialId);
            packedPairs.Add(mapping.FirstResultId);
            packedPairs.Add(mapping.SecondResultId);
        }

        if (end == mappings.Count)
            _jobs.TryRemove(work.JobId, out _);

        return packedPairs.Count == 0 ? null : new PlayerFusionMappingBatchPayload
        {
            JobId = work.JobId,
            Offset = work.ProcessedPairs,
            PackedPairs = packedPairs
        };
    }

    /// <summary>
    /// Determines whether one game-owned job matches the embedded generated catalog.
    /// </summary>
    private bool Compatible(PlayerFusionMappingWorkPayload work)
    {
        return work.GeneratorVersion == _catalog.PlayerFusionGeneratorVersion
            && work.BaseStatSourceFingerprint == _catalog.BaseStatSourceFingerprint
            && work.CustomFusionPoolVersion == _catalog.CustomFusionPoolVersion
            && work.CustomFusionPoolSize == _catalog.CustomFusionPool.Count
            && work.CustomFusionPoolFingerprint == _catalog.CustomFusionPoolFingerprint;
    }
}
