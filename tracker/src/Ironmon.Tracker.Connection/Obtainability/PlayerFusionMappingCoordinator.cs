using System.Collections.Concurrent;
using System.Buffers.Binary;

namespace Ironmon.Tracker.Connection.Obtainability;

/// <summary>
/// Caches parallel player-fusion jobs and returns one compact final result to the game.
/// </summary>
internal sealed class PlayerFusionMappingCoordinator
{
    private const char FusionBodyPrefix = 'B';
    private const char FusionHeadSeparator = 'H';
    private const string FusionBodyLexicalSuffix = "H";
    private const int MaximumCachedEvolutionJobs = 2;
    private const int MaximumCachedMaterialJobs = 2;
    private const int MaximumPlansPerSpecies = 8;
    private static readonly IReadOnlyDictionary<string, string> _emptyConstraints = new Dictionary<string, string>(StringComparer.Ordinal);
    private static readonly IReadOnlyDictionary<string, int> _emptyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly PlayerFusionMappingWorkerCatalog _catalog;
    private readonly ConcurrentDictionary<string, PlayerFusionWorkerContext> _workerContexts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<EvolutionAssignmentKey, Lazy<Task<FusionEvolutionAssignmentIndex>>> _evolutionJobs = new();
    private readonly ConcurrentDictionary<PlayerFusionMaterialKey, Lazy<Task<PlayerFusionMaterialIndex>>> _materialJobs = new();
    private readonly ConcurrentDictionary<string, Task<PlayerFusionWorkerResult>> _jobs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, EvolutionAssignmentKey> _runEvolutionKeys = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PlayerFusionMaterialKey> _runMaterialKeys = new(StringComparer.Ordinal);
    private readonly ObtainabilitySourceCatalog _sourceCatalog;

    /// <summary>
    /// Initializes a coordinator from the embedded generated mapping catalog.
    /// </summary>
    internal PlayerFusionMappingCoordinator()
    {
        _catalog = PlayerFusionMappingWorkerCatalog.Load();
        _sourceCatalog = ObtainabilitySourceCatalog.Load();
        _workerContexts[_catalog.CustomFusionPoolFingerprint] = new PlayerFusionWorkerContext(_catalog);
    }

    /// <summary>
    /// Gets the number of mapping jobs currently retained for delivery.
    /// </summary>
    internal int CachedJobCount => _jobs.Count;

    /// <summary>
    /// Starts or reuses both native indexes as soon as an authorized active run is recovered.
    /// </summary>
    /// <param name="runId">The active run identifier.</param>
    /// <param name="recipe">The deterministic assignment recipe supplied by the game.</param>
    /// <returns>True when the recipe matches the embedded tracker catalog; otherwise false.</returns>
    internal bool PrepareActiveFusionAssignments(string runId, FusionAssignmentRecipePayload recipe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(recipe);
        PlayerFusionWorkerContext? context = ResolveWorkerContext(recipe);
        if (context is null || !Compatible(recipe, context.Catalog))
            return false;

        EvolutionAssignmentKey key = EvolutionAssignmentKey.From(recipe);
        PlayerFusionMaterialKey materialKey = PlayerFusionMaterialKey.From(recipe);
        _runEvolutionKeys[runId] = key;
        _runMaterialKeys[runId] = materialKey;
        _ = GetEvolutionAssignmentIndex(key);
        _ = GetMaterialIndex(materialKey);
        return true;
    }

    /// <summary>
    /// Calculates or reuses one compatible job and creates its compact final result.
    /// </summary>
    /// <param name="work">The game-owned job description.</param>
    /// <param name="cancellationToken">The token that cancels waiting for the worker.</param>
    /// <param name="runId">The run identifier that may reuse the assignment index for graph lookups.</param>
    /// <returns>The final result, or null when the embedded catalog is incompatible.</returns>
    internal async Task<PlayerFusionClosureResultPayload?> CreateResultAsync(PlayerFusionClosureWorkPayload work, CancellationToken cancellationToken, string? runId = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (work.SourceCatalogFingerprint != _sourceCatalog.Fingerprint)
            throw new InvalidOperationException("The game and tracker obtainability source catalogs do not match.");

        PlayerFusionWorkerContext? context = ResolveWorkerContext(work);
        if (context is null)
            return null;

        EvolutionAssignmentKey evolutionKey = EvolutionAssignmentKey.From(work);
        if (!string.IsNullOrWhiteSpace(runId))
        {
            _runEvolutionKeys[runId] = evolutionKey;
            _runMaterialKeys[runId] = new PlayerFusionMaterialKey(work.Seed, work.GeneratorVersion, work.BaseStatSourceFingerprint, work.CustomFusionPoolFingerprint);
        }

        Task<PlayerFusionWorkerResult> job = _jobs.GetOrAdd(work.JobId, _ => work.DirectEncounterOnly
            ? BuildDirectEncounterResultAsync(work, evolutionKey, context)
            : BuildResultAsync(work, evolutionKey, context));
        try
        {
            PlayerFusionWorkerResult result = await job.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new PlayerFusionClosureResultPayload
            {
                JobId = work.JobId,
                ObtainableFusionWords = result.ObtainableFusionWords,
                DirectEncounterFusionIds = result.DirectEncounterFusionIds,
                PackedExecutableEvolutionEdges = result.PackedExecutableEvolutionEdges,
                ObtainableCount = result.ObtainableCount
            };
        }
        finally
        {
            if (job.IsCompleted && _jobs.TryGetValue(work.JobId, out Task<PlayerFusionWorkerResult>? retainedJob) && ReferenceEquals(retainedJob, job))
                _jobs.TryRemove(work.JobId, out _);
        }
    }

    /// <summary>
    /// Determines whether an area request can share compatible tracker-owned fusion state.
    /// </summary>
    /// <param name="runId">The requested run.</param>
    /// <param name="recipe">The archived recipe, or null for active lookup.</param>
    /// <returns>Whether native mapping is available without exposing a new active-run recipe.</returns>
    internal bool CanMapAreaFusions(string runId, CompletedRunRecipePayload? recipe)
        => recipe is null ? _runMaterialKeys.ContainsKey(runId) : Compatible(recipe);

    /// <summary>
    /// Reuses the shared reverse index to identify every ordered normal pair producing a location-search target.
    /// </summary>
    /// <param name="runId">The active or archived run identifier.</param>
    /// <param name="recipe">The archived recipe, or null for active lookup.</param>
    /// <param name="speciesId">The target fusion identity.</param>
    /// <param name="cancellationToken">The token cancelling this request's wait.</param>
    /// <returns>A fixed-size material membership bitset, or null when native mapping is unavailable or unnecessary.</returns>
    internal async Task<byte[]?> GetOccurrenceFusionMaterialsAsync(string? runId, CompletedRunRecipePayload? recipe, string speciesId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runId) || !TryParseFusionTargetId(speciesId, out int targetId) || !CanMapAreaFusions(runId, recipe))
            return null;

        PlayerFusionMaterialKey key = recipe is null ? _runMaterialKeys[runId] : PlayerFusionMaterialKey.From(recipe);
        PlayerFusionMaterialIndex index = await GetMaterialIndex(key).WaitAsync(cancellationToken).ConfigureAwait(false);
        PlayerFusionWorkerContext context = WorkerContext(key.TargetPoolFingerprint);
        int count = context.Catalog.NormalSpeciesCount;
        byte[] membership = new byte[(count * count + 7) / 8];
        if (!index.PackedAssignmentsByTarget.TryGetValue(targetId, out IReadOnlyList<uint>? assignments))
            return membership;

        foreach (uint value in assignments)
        {
            int position = ((int)(value >> 10) - 1) * count + (int)(value & 0x3FF) - 1;
            membership[position / 8] |= (byte)(1 << (position % 8));
        }

        return membership;
    }

    /// <summary>
    /// Resolves only a disclosed area's requested page using the shared obtainability mapping worker.
    /// </summary>
    /// <param name="runId">The requested run.</param>
    /// <param name="recipe">The archived recipe, or null for active lookup.</param>
    /// <param name="materials">The bounded game-authorized material pairs.</param>
    /// <param name="cancellationToken">The token cancelling this page's wait.</param>
    /// <returns>The ordered results, or null when no compatible native recipe is available.</returns>
    internal async Task<IReadOnlyList<AreaFusionResultPayload>?> MapAreaFusionsAsync(string runId, CompletedRunRecipePayload? recipe, IReadOnlyList<FusionMaterialAssignmentPayload> materials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(materials.Count, TrackerProtocol.MaximumSearchPageSize);
        if (!CanMapAreaFusions(runId, recipe))
            return null;

        PlayerFusionMaterialKey key = recipe is null ? _runMaterialKeys[runId] : PlayerFusionMaterialKey.From(recipe);
        return await Task.Run<IReadOnlyList<AreaFusionResultPayload>>(() =>
        {
            List<AreaFusionResultPayload> results = [];
            foreach (FusionMaterialAssignmentPayload pair in materials)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(new AreaFusionResultPayload
                {
                    BodyId = pair.BodyId,
                    HeadId = pair.HeadId,
                    SpeciesNumber = WorkerContext(key.TargetPoolFingerprint).MappingWorker.MapOrderedPair(key.Seed, key.GeneratorVersion, pair.BodyId, pair.HeadId)
                });
            }

            return results;
        }, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the exact seeded fusion sources assigned to one completed-run target.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <param name="speciesId">The target species identifier.</param>
    /// <param name="cancellationToken">The token that cancels waiting for shared tracker work.</param>
    /// <returns>The exact assignments, an empty list for a fusion without predecessors, or null when tracker-side generation is incompatible or unnecessary.</returns>
    internal async Task<IReadOnlyList<EvolutionPredecessorAssignmentPayload>?> GetEvolutionPredecessorAssignmentsAsync(CompletedRunRecipePayload recipe, string speciesId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        if (!TryParseFusionTargetId(speciesId, out int targetId) || !Compatible(recipe))
            return null;

        EvolutionAssignmentKey key = EvolutionAssignmentKey.From(recipe);
        _runEvolutionKeys[recipe.RunId] = key;
        FusionEvolutionAssignmentIndex index = await GetEvolutionAssignmentIndex(key).WaitAsync(cancellationToken).ConfigureAwait(false);
        return CreatePredecessorPayloads(index, targetId);
    }

    /// <summary>
    /// Generates the exact outgoing assignments for one completed-run fusion without waiting for the run-wide reverse index.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <param name="speciesId">The source species identifier.</param>
    /// <returns>The exact assignments, an empty list for a fusion without targets, or null when native generation is incompatible or unnecessary.</returns>
    internal IReadOnlyList<EvolutionTargetAssignmentPayload>? GetEvolutionTargetAssignments(CompletedRunRecipePayload recipe, string speciesId)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        if (!TryParseFusionComponents(speciesId, out int bodyId, out int headId) || !Compatible(recipe))
            return null;

        EvolutionAssignmentKey key = EvolutionAssignmentKey.From(recipe);
        _runEvolutionKeys[recipe.RunId] = key;
        return CreateTargetPayloads(WorkerContext(key.TargetPoolFingerprint).EvolutionWorker.Generate(key.Seed, PackComponents(bodyId, headId)));
    }

    /// <summary>
    /// Generates the exact outgoing assignments for one active-run fusion without waiting for the run-wide reverse index.
    /// </summary>
    /// <param name="runId">The active run identifier.</param>
    /// <param name="speciesId">The source species identifier.</param>
    /// <returns>The exact assignments, an empty list for a fusion without targets, or null when no compatible active recipe exists.</returns>
    internal IReadOnlyList<EvolutionTargetAssignmentPayload>? GetActiveEvolutionTargetAssignments(string? runId, string speciesId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        if (string.IsNullOrWhiteSpace(runId) || !TryParseFusionComponents(speciesId, out int bodyId, out int headId) || !_runEvolutionKeys.TryGetValue(runId, out EvolutionAssignmentKey? key))
            return null;

        return CreateTargetPayloads(WorkerContext(key.TargetPoolFingerprint).EvolutionWorker.Generate(key.Seed, PackComponents(bodyId, headId)));
    }

    /// <summary>
    /// Generates every valid candidate for one completed-run fusion component.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <param name="speciesId">The source species identifier.</param>
    /// <param name="side">The displayed component whose evolution candidates are requested.</param>
    /// <returns>The valid candidates, an empty list when the component cannot evolve, or null when native generation is incompatible or unnecessary.</returns>
    internal byte[]? GetEvolutionCandidateAssignments(CompletedRunRecipePayload recipe, string speciesId, EvolutionCandidateSide side)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        if (!TryGetFusionComponentSide(side, out FusionComponentSide componentSide) || !TryParseFusionComponents(speciesId, out int bodyId, out int headId) || !Compatible(recipe))
            return null;

        EvolutionAssignmentKey key = EvolutionAssignmentKey.From(recipe);
        _runEvolutionKeys[recipe.RunId] = key;
        return PackCandidateTargets(WorkerContext(key.TargetPoolFingerprint).EvolutionWorker.GetCandidateTargets(key.Seed, PackComponents(bodyId, headId), componentSide));
    }

    /// <summary>
    /// Generates every valid candidate for one active-run fusion component.
    /// </summary>
    /// <param name="runId">The active run identifier.</param>
    /// <param name="speciesId">The source species identifier.</param>
    /// <param name="side">The displayed component whose evolution candidates are requested.</param>
    /// <returns>The valid candidates, an empty list when the component cannot evolve, or null when no compatible active recipe exists.</returns>
    internal byte[]? GetActiveEvolutionCandidateAssignments(string? runId, string speciesId, EvolutionCandidateSide side)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        if (string.IsNullOrWhiteSpace(runId) || !TryGetFusionComponentSide(side, out FusionComponentSide componentSide)
            || !TryParseFusionComponents(speciesId, out int bodyId, out int headId)
            || !_runEvolutionKeys.TryGetValue(runId, out EvolutionAssignmentKey? key))
        {
            return null;
        }

        return PackCandidateTargets(WorkerContext(key.TargetPoolFingerprint).EvolutionWorker.GetCandidateTargets(key.Seed, PackComponents(bodyId, headId), componentSide));
    }

    /// <summary>
    /// Returns one page of exact material assignments for a completed-run fusion.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <param name="speciesId">The target fusion species identifier.</param>
    /// <param name="offset">The zero-based assignment offset.</param>
    /// <param name="limit">The maximum number of assignments to return.</param>
    /// <param name="cancellationToken">The token that cancels waiting for native mapping work.</param>
    /// <returns>The assignment page, or null when native generation is incompatible or unnecessary.</returns>
    internal async Task<PlayerFusionMaterialPage?> GetFusionMaterialAssignmentsAsync(CompletedRunRecipePayload recipe, string speciesId, int offset, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        if (!TryParseFusionTargetId(speciesId, out int targetId) || !Compatible(recipe))
            return null;

        PlayerFusionMaterialKey key = PlayerFusionMaterialKey.From(recipe);
        _runMaterialKeys[recipe.RunId] = key;
        PlayerFusionMaterialIndex index = await GetMaterialIndex(key).WaitAsync(cancellationToken).ConfigureAwait(false);
        return CreateMaterialPage(index, targetId, offset, limit);
    }

    /// <summary>
    /// Returns one page of exact material assignments for an active-run fusion.
    /// </summary>
    /// <param name="runId">The active run identifier.</param>
    /// <param name="speciesId">The target fusion species identifier.</param>
    /// <param name="offset">The zero-based assignment offset.</param>
    /// <param name="limit">The maximum number of assignments to return.</param>
    /// <param name="cancellationToken">The token that cancels waiting for native mapping work.</param>
    /// <returns>The assignment page, or null when no compatible active recipe exists.</returns>
    internal async Task<PlayerFusionMaterialPage?> GetActiveFusionMaterialAssignmentsAsync(string? runId, string speciesId, int offset, int limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        if (string.IsNullOrWhiteSpace(runId) || !TryParseFusionTargetId(speciesId, out int targetId) || !_runMaterialKeys.TryGetValue(runId, out PlayerFusionMaterialKey? key))
            return null;

        PlayerFusionMaterialIndex index = await GetMaterialIndex(key).WaitAsync(cancellationToken).ConfigureAwait(false);
        return CreateMaterialPage(index, targetId, offset, limit);
    }

    /// <summary>
    /// Reads a reverse partner from completed mapping work without starting or waiting for preparation.
    /// </summary>
    /// <param name="runId">The active or archived run identifier.</param>
    /// <param name="recipe">The archived recipe, or null for active lookup.</param>
    /// <param name="speciesId">The selected fusion identity.</param>
    /// <returns>The prepared numeric reverse partner, or null when no compatible result is ready.</returns>
    internal int? GetPreparedReverseFusion(string? runId, CompletedRunRecipePayload? recipe, string speciesId)
    {
        if (string.IsNullOrWhiteSpace(runId) || !TryParseFusionTargetId(speciesId, out int targetId) || !CanMapAreaFusions(runId, recipe))
            return null;

        PlayerFusionMaterialKey key = recipe is null ? _runMaterialKeys[runId] : PlayerFusionMaterialKey.From(recipe);
        if (!_workerContexts.TryGetValue(key.TargetPoolFingerprint, out PlayerFusionWorkerContext? context))
            return null;

        return context.MappingWorker.GetPreparedReversePartner(key.Seed, key.GeneratorVersion, targetId);
    }

    /// <summary>
    /// Starts or reuses the active run's exact fusion assignments for one debug graph target when available.
    /// </summary>
    /// <param name="runId">The active run identifier.</param>
    /// <param name="speciesId">The target species identifier.</param>
    /// <param name="cancellationToken">The token that cancels waiting for shared tracker work.</param>
    /// <returns>The exact assignments, an empty list for a fusion without predecessors, or null when no compatible run recipe exists.</returns>
    internal async Task<IReadOnlyList<EvolutionPredecessorAssignmentPayload>?> GetActiveEvolutionPredecessorAssignmentsAsync(string? runId, string speciesId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        if (string.IsNullOrWhiteSpace(runId) || !TryParseFusionTargetId(speciesId, out int targetId)
            || !_runEvolutionKeys.TryGetValue(runId, out EvolutionAssignmentKey? key))
        {
            return null;
        }

        FusionEvolutionAssignmentIndex index = await GetEvolutionAssignmentIndex(key).WaitAsync(cancellationToken).ConfigureAwait(false);
        return CreatePredecessorPayloads(index, targetId);
    }

    /// <summary>
    /// Builds the compact run-wide worker result without synchronously waiting between parallel worker jobs.
    /// </summary>
    /// <param name="work">The validated game-owned job description.</param>
    /// <param name="evolutionKey">The compatible deterministic assignment identity.</param>
    /// <param name="context">The workers bound to the pinned target pool.</param>
    /// <returns>The final obtainability membership and executable-edge indexes.</returns>
    private async Task<PlayerFusionWorkerResult> BuildResultAsync(PlayerFusionClosureWorkPayload work, EvolutionAssignmentKey evolutionKey, PlayerFusionWorkerContext context)
    {
        PlayerFusionMaterialKey materialKey = new(work.Seed, work.GeneratorVersion, work.BaseStatSourceFingerprint, work.CustomFusionPoolFingerprint);
        bool mappingsAlreadyPreparing = _materialJobs.TryGetValue(materialKey, out Lazy<Task<PlayerFusionMaterialIndex>>? retainedMaterialJob)
            && retainedMaterialJob.IsValueCreated;
        Task<PlayerFusionMaterialIndex> materialJob = GetMaterialIndex(materialKey);
        Task<PlayerFusionDirectProofResult> directProofJob = mappingsAlreadyPreparing
            ? BuildDirectProofFromMaterialIndexAsync(work, context, materialJob)
            : Task.Run(() => BuildDirectProof(work, context), CancellationToken.None);
        Task<FusionEvolutionAssignmentIndex> evolutionJob = GetEvolutionAssignmentIndex(evolutionKey);
        Task firstCompletedJob = await Task.WhenAny(directProofJob, evolutionJob).ConfigureAwait(false);
        await firstCompletedJob.ConfigureAwait(false);

        PlayerFusionDirectProofResult provenDirect = await directProofJob.ConfigureAwait(false);
        IReadOnlyList<FusionEvolutionSourceAssignment> generatedAssignments = (await evolutionJob.ConfigureAwait(false)).Assignments;
        await materialJob.ConfigureAwait(false);

        IReadOnlyList<FusionEvolutionSourceAssignment> executableAssignments = CloseFusionEvolutions(provenDirect.PlansBySpecies, generatedAssignments, work.ResourceSupply, provenDirect.NextPlanOrder, context);
        uint[] obtainableFusionWords = BuildObtainableFusionWords(provenDirect.PlansBySpecies);
        byte[] packedExecutableEdges = PackExecutableEvolutionEdges(executableAssignments);
        int obtainableCount = provenDirect.PlansBySpecies.Count(entry => entry.Value.Count > 0);

        return new PlayerFusionWorkerResult(obtainableFusionWords, provenDirect.DirectEncounterFusionIds, packedExecutableEdges, obtainableCount);
    }

    /// <summary>
    /// Reuses the complete mappings retained by an already-started reverse
    /// material index before building the final direct proof set.
    /// </summary>
    /// <param name="work">The validated game-owned mapping input.</param>
    /// <param name="context">The workers bound to the pinned target pool.</param>
    /// <param name="materialJob">The retained reverse material-index task.</param>
    /// <returns>The proof states needed by tracker-owned closure.</returns>
    private static async Task<PlayerFusionDirectProofResult> BuildDirectProofFromMaterialIndexAsync(PlayerFusionClosureWorkPayload work, PlayerFusionWorkerContext context, Task<PlayerFusionMaterialIndex> materialJob)
    {
        PlayerFusionMaterialIndex materialIndex = await materialJob.ConfigureAwait(false);
        return await Task.Run(() => BuildDirectProof(work, context, materialIndex), CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Maps only the derived wild-encounter pairs needed before game-owned
    /// caught-fusion transformations, then starts the reusable full indexes
    /// without delaying delivery of the bounded result.
    /// </summary>
    /// <param name="work">The validated first-stage game-owned mapping input.</param>
    /// <param name="evolutionKey">The compatible deterministic assignment identity.</param>
    /// <param name="context">The workers bound to the pinned target pool.</param>
    /// <returns>The derived encounter fusion identifiers without a full closure summary.</returns>
    private async Task<PlayerFusionWorkerResult> BuildDirectEncounterResultAsync(PlayerFusionClosureWorkPayload work, EvolutionAssignmentKey evolutionKey, PlayerFusionWorkerContext context)
    {
        _ = GetEvolutionAssignmentIndex(evolutionKey);
        _ = GetMaterialIndex(new PlayerFusionMaterialKey(work.Seed, work.GeneratorVersion, work.BaseStatSourceFingerprint, work.CustomFusionPoolFingerprint));
        IReadOnlyList<int> directEncounterFusionIds = await Task.Run(() => MapDirectEncounterFusions(work, context), CancellationToken.None).ConfigureAwait(false);

        return new PlayerFusionWorkerResult([], directEncounterFusionIds, [], 0);
    }

    /// <summary>
    /// Resolves only the triangular material offsets used by derived wild encounters.
    /// </summary>
    /// <param name="work">The validated first-stage game-owned mapping input.</param>
    /// <param name="context">The workers bound to the pinned target pool.</param>
    /// <returns>The distinct mapped encounter fusion identifiers in numeric order.</returns>
    private static IReadOnlyList<int> MapDirectEncounterFusions(PlayerFusionClosureWorkPayload work, PlayerFusionWorkerContext context)
    {
        int materialCount = work.MaterialIds.Count;
        int expectedPairCount = checked(materialCount * (materialCount + 1) / 2);
        if (work.TotalPairs != expectedPairCount)
            throw new InvalidDataException("The derived-encounter mapping job no longer matches the game-owned pair count.");

        if (!work.MaterialIds.SequenceEqual(work.MaterialIds.Distinct().Order()))
            throw new InvalidDataException("The derived-encounter material list is not in stable numeric order.");

        HashSet<int> directOffsets = [.. work.DirectPairOffsets];
        if (directOffsets.Count == 0 || directOffsets.Count != work.DirectPairOffsets.Count
            || directOffsets.Any(offset => offset < 0 || offset >= expectedPairCount))
        {
            throw new InvalidDataException("The derived-encounter mapping job contains invalid pair offsets.");
        }

        HashSet<int> results = [];
        int offset = 0;
        for (int firstIndex = 0; firstIndex < materialCount; firstIndex++)
        {
            for (int secondIndex = firstIndex; secondIndex < materialCount; secondIndex++)
            {
                if (directOffsets.Contains(offset))
                {
                    int first = work.MaterialIds[firstIndex];
                    int second = work.MaterialIds[secondIndex];
                    results.Add(context.MappingWorker.MapOrderedPair(work.Seed, work.GeneratorVersion, first, second));
                    if (first != second)
                        results.Add(context.MappingWorker.MapOrderedPair(work.Seed, work.GeneratorVersion, second, first));
                }

                offset++;
            }
        }

        return [.. results.Order()];
    }

    /// <summary>
    /// Gets or starts one shared exact assignment index for a compatible seed and catalog.
    /// </summary>
    /// <param name="key">The validated deterministic assignment identity.</param>
    /// <returns>The shared assignment-index task.</returns>
    private Task<FusionEvolutionAssignmentIndex> GetEvolutionAssignmentIndex(EvolutionAssignmentKey key)
    {
        Lazy<Task<FusionEvolutionAssignmentIndex>> lazyJob = _evolutionJobs.GetOrAdd(key, CreateEvolutionJob);
        TrimEvolutionJobs(key);
        return lazyJob.Value;
    }

    /// <summary>
    /// Gets or starts the native reverse material index for one compatible run recipe.
    /// </summary>
    /// <param name="key">The deterministic player-fusion identity.</param>
    /// <returns>The shared reverse material-index task.</returns>
    private Task<PlayerFusionMaterialIndex> GetMaterialIndex(PlayerFusionMaterialKey key)
    {
        Lazy<Task<PlayerFusionMaterialIndex>> lazyJob = _materialJobs.GetOrAdd(key, CreateMaterialJob);
        TrimMaterialJobs(key);
        return lazyJob.Value;
    }

    /// <summary>
    /// Creates one deferred native reverse material-index task.
    /// </summary>
    /// <param name="key">The deterministic player-fusion identity.</param>
    /// <returns>The deferred material-index task.</returns>
    private Lazy<Task<PlayerFusionMaterialIndex>> CreateMaterialJob(PlayerFusionMaterialKey key)
        => new(() => Task.Run(() => BuildMaterialIndex(key), CancellationToken.None), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Maps every normal pair once and groups ordered material assignments by target fusion.
    /// </summary>
    /// <param name="key">The compatible deterministic player-fusion identity.</param>
    /// <returns>The reverse assignment index.</returns>
    private PlayerFusionMaterialIndex BuildMaterialIndex(PlayerFusionMaterialKey key)
    {
        PlayerFusionWorkerContext context = WorkerContext(key.TargetPoolFingerprint);
        int[] materialIds = [.. Enumerable.Range(1, context.Catalog.NormalSpeciesCount)];
        IReadOnlyList<PlayerFusionMappedPair> mappings = context.MappingWorker.MapAll(key.Seed, key.GeneratorVersion, materialIds, CancellationToken.None);
        Dictionary<int, List<uint>> assignments = [];
        foreach (PlayerFusionMappedPair mapping in mappings)
        {
            AddMaterialAssignment(assignments, mapping.FirstResultId, mapping.FirstMaterialId, mapping.SecondMaterialId);
            if (mapping.FirstMaterialId != mapping.SecondMaterialId)
                AddMaterialAssignment(assignments, mapping.SecondResultId, mapping.SecondMaterialId, mapping.FirstMaterialId);
        }

        Dictionary<int, IReadOnlyList<uint>> orderedAssignments = assignments.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<uint>)[.. entry.Value.Order()]);
        return new PlayerFusionMaterialIndex(mappings, orderedAssignments);
    }

    /// <summary>
    /// Adds one ordered normal-material assignment to its target fusion.
    /// </summary>
    /// <param name="assignments">The mutable reverse assignment index.</param>
    /// <param name="targetId">The numeric custom-fusion target.</param>
    /// <param name="bodyId">The numeric Body material.</param>
    /// <param name="headId">The numeric Head material.</param>
    private static void AddMaterialAssignment(Dictionary<int, List<uint>> assignments, int targetId, int bodyId, int headId)
    {
        if (!assignments.TryGetValue(targetId, out List<uint>? targetAssignments))
        {
            targetAssignments = [];
            assignments.Add(targetId, targetAssignments);
        }

        targetAssignments.Add(checked((uint)(bodyId << 10 | headId)));
    }

    /// <summary>
    /// Creates a bounded protocol page from one retained reverse material index.
    /// </summary>
    /// <param name="index">The retained reverse material index.</param>
    /// <param name="targetId">The numeric custom-fusion target.</param>
    /// <param name="offset">The zero-based assignment offset.</param>
    /// <param name="limit">The maximum number of assignments to return.</param>
    /// <returns>The bounded assignment page.</returns>
    private static PlayerFusionMaterialPage CreateMaterialPage(PlayerFusionMaterialIndex index, int targetId, int offset, int limit)
    {
        if (!index.PackedAssignmentsByTarget.TryGetValue(targetId, out IReadOnlyList<uint>? assignments))
            return new PlayerFusionMaterialPage([], 0);

        IReadOnlyList<FusionMaterialAssignmentPayload> page = [.. assignments.Skip(offset).Take(limit).Select(value => new FusionMaterialAssignmentPayload
        {
            BodyId = (int)(value >> 10),
            HeadId = (int)(value & 0x3FF)
        })];

        return new PlayerFusionMaterialPage(page, assignments.Count);
    }

    /// <summary>
    /// Bounds retained reverse material indexes while preserving the requested entry.
    /// </summary>
    /// <param name="currentKey">The material index currently being requested.</param>
    private void TrimMaterialJobs(PlayerFusionMaterialKey currentKey)
    {
        if (_materialJobs.Count <= MaximumCachedMaterialJobs)
            return;

        foreach (PlayerFusionMaterialKey key in _materialJobs.Keys.Where(key => key != currentKey).Take(_materialJobs.Count - MaximumCachedMaterialJobs))
            _materialJobs.TryRemove(key, out _);
    }

    /// <summary>
    /// Creates one deferred shared assignment-index task without starting losing concurrent value factories.
    /// </summary>
    /// <param name="key">The deterministic assignment identity.</param>
    /// <returns>The deferred assignment-index task.</returns>
    private Lazy<Task<FusionEvolutionAssignmentIndex>> CreateEvolutionJob(EvolutionAssignmentKey key)
        => new(() => Task.Run(() => BuildEvolutionAssignmentIndex(key), CancellationToken.None), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Generates all exact assignments once and builds their reverse target index.
    /// </summary>
    /// <param name="key">The compatible run and target-pool identity.</param>
    /// <returns>The exact assignments and reverse branches grouped by assigned target.</returns>
    private FusionEvolutionAssignmentIndex BuildEvolutionAssignmentIndex(EvolutionAssignmentKey key)
    {
        PlayerFusionWorkerContext context = WorkerContext(key.TargetPoolFingerprint);
        PlayerFusionMappingWorkerCatalog catalog = context.Catalog;
        int[] packedSources = [.. catalog.CustomFusionPool.Select(target => target.PackedComponents)];
        IReadOnlyList<FusionEvolutionSourceAssignment> assignments = context.EvolutionWorker.GenerateAll(key.Seed, packedSources, CancellationToken.None);

        Dictionary<int, int> bodyRanks = catalog.NormalSpecies
            .OrderBy(species => $"{species.Id}{FusionBodyLexicalSuffix}", StringComparer.Ordinal)
            .Select((species, index) => (species.Id, index))
            .ToDictionary(entry => entry.Id, entry => entry.index);

        Dictionary<int, int> headRanks = catalog.NormalSpecies
            .OrderBy(species => species.Id.ToString(), StringComparer.Ordinal)
            .Select((species, index) => (species.Id, index))
            .ToDictionary(entry => entry.Id, entry => entry.index);

        FusionEvolutionSourceAssignment[] branchfulAssignments = [.. assignments.Where(assignment => assignment.Branches.Count > 0)];
        Dictionary<int, List<FusionEvolutionIndexedBranch>> assignmentsByTarget = [];
        foreach (FusionEvolutionSourceAssignment assignment in branchfulAssignments)
        {
            int bodyId = assignment.PackedSourceComponents >> 10;
            int headId = assignment.PackedSourceComponents & 0x3FF;
            int sourceId = checked(bodyId * catalog.NormalSpeciesCount + headId);
            int rank = checked(bodyRanks[bodyId] * catalog.NormalSpeciesCount + headRanks[headId]);
            for (int branchIndex = 0; branchIndex < assignment.Branches.Count; branchIndex++)
            {
                FusionEvolutionAssignedBranch branch = assignment.Branches[branchIndex];
                if (!assignmentsByTarget.TryGetValue(branch.TargetId, out List<FusionEvolutionIndexedBranch>? targetAssignments))
                {
                    targetAssignments = [];
                    assignmentsByTarget.Add(branch.TargetId, targetAssignments);
                }

                targetAssignments.Add(new FusionEvolutionIndexedBranch(assignment, branch, sourceId, rank, branchIndex));
            }
        }

        Dictionary<int, IReadOnlyList<FusionEvolutionIndexedBranch>> predecessorAssignments = [];
        foreach ((int targetId, List<FusionEvolutionIndexedBranch> targetAssignments) in assignmentsByTarget)
        {
            predecessorAssignments.Add(targetId, [.. targetAssignments
                .OrderBy(value => value.SourceRank)
                .ThenBy(value => value.SourceId)
                .ThenBy(value => value.BranchIndex)
                ]);
        }

        return new FusionEvolutionAssignmentIndex(branchfulAssignments, predecessorAssignments);
    }

    /// <summary>
    /// Creates the small protocol branch list for one target from the retained tracker-owned reverse index.
    /// </summary>
    /// <param name="index">The exact seeded assignment index.</param>
    /// <param name="targetId">The numeric fusion target identifier.</param>
    /// <returns>The exact display-formatting assignments for the target.</returns>
    private static IReadOnlyList<EvolutionPredecessorAssignmentPayload> CreatePredecessorPayloads(FusionEvolutionAssignmentIndex index, int targetId)
    {
        if (!index.PredecessorAssignments.TryGetValue(targetId, out IReadOnlyList<FusionEvolutionIndexedBranch>? assignments))
            return [];

        return [.. assignments.Select(assignment => new EvolutionPredecessorAssignmentPayload
        {
            SourceId = assignment.SourceId,
            SourceBaseStatTotal = assignment.Source.SourceBst,
            ComponentSide = assignment.Branch.Side == FusionComponentSide.Body ? EvolutionCandidateSide.Body : EvolutionCandidateSide.Head,
            ComponentBranchIdentity = assignment.Branch.ComponentBranchIdentity
        })];
    }

    /// <summary>
    /// Creates the small protocol branch list for one source assignment.
    /// </summary>
    /// <param name="assignment">The exact native source assignment.</param>
    /// <returns>The game-formatting target assignments.</returns>
    private static IReadOnlyList<EvolutionTargetAssignmentPayload> CreateTargetPayloads(FusionEvolutionSourceAssignment assignment)
    {
        return [.. assignment.Branches.Select(branch => new EvolutionTargetAssignmentPayload
        {
            TargetId = branch.TargetId,
            TargetBaseStatTotal = branch.TargetBst,
            ComponentSide = branch.Side == FusionComponentSide.Body ? EvolutionCandidateSide.Body : EvolutionCandidateSide.Head,
            ComponentBranchIdentity = branch.ComponentBranchIdentity
        })];
    }

    /// <summary>
    /// Packs the native candidate list into fixed little-endian target and base-stat words.
    /// </summary>
    /// <param name="candidates">The valid native candidates.</param>
    /// <returns>The compact game-formatting candidate assignments.</returns>
    private static byte[] PackCandidateTargets(IReadOnlyList<FusionEvolutionCandidateTarget> candidates)
    {
        const int CandidateBstBits = 11;
        const int BytesPerCandidate = sizeof(uint);
        byte[] result = new byte[checked(candidates.Count * BytesPerCandidate)];
        for (int index = 0; index < candidates.Count; index++)
        {
            FusionEvolutionCandidateTarget candidate = candidates[index];
            uint packed = checked((uint)candidate.TargetId << CandidateBstBits | (uint)candidate.TargetBst);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(index * BytesPerCandidate, BytesPerCandidate), packed);
        }

        return result;
    }

    /// <summary>
    /// Removes completed seed indexes beyond the small active/archive reuse window.
    /// </summary>
    /// <param name="retainedKey">The index required by the current caller.</param>
    private void TrimEvolutionJobs(EvolutionAssignmentKey retainedKey)
    {
        if (_evolutionJobs.Count <= MaximumCachedEvolutionJobs)
            return;

        foreach ((EvolutionAssignmentKey key, Lazy<Task<FusionEvolutionAssignmentIndex>> job) in _evolutionJobs)
        {
            if (_evolutionJobs.Count <= MaximumCachedEvolutionJobs)
                break;

            if (key != retainedKey && job.IsValueCreated && job.Value.IsCompleted)
                _evolutionJobs.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Parses a standard custom-fusion identity into the numeric species identifier used by generated assignments.
    /// </summary>
    /// <param name="speciesId">The stable species and form identifier.</param>
    /// <param name="targetId">The parsed numeric target identifier.</param>
    /// <returns>True when the identifier represents two valid normal components.</returns>
    private bool TryParseFusionTargetId(string speciesId, out int targetId)
    {
        targetId = 0;
        if (!TryParseFusionComponents(speciesId, out int bodyId, out int headId))
            return false;

        targetId = checked(bodyId * _catalog.NormalSpeciesCount + headId);
        return true;
    }

    /// <summary>
    /// Parses a standard custom-fusion identity into its normal component identifiers.
    /// </summary>
    /// <param name="speciesId">The stable species and form identifier.</param>
    /// <param name="bodyId">The parsed body identifier.</param>
    /// <param name="headId">The parsed head identifier.</param>
    /// <returns>True when the identifier represents two valid normal components.</returns>
    private bool TryParseFusionComponents(string speciesId, out int bodyId, out int headId)
    {
        bodyId = 0;
        headId = 0;
        string identity = speciesId.Split(':', 2)[0];
        int separator = identity.IndexOf(FusionHeadSeparator, 1);
        return identity.Length >= 4 && identity[0] == FusionBodyPrefix && separator >= 2
            && int.TryParse(identity.AsSpan(1, separator - 1), out bodyId)
            && int.TryParse(identity.AsSpan(separator + 1), out headId)
            && bodyId > 0 && bodyId <= _catalog.NormalSpeciesCount
            && headId > 0 && headId <= _catalog.NormalSpeciesCount;
    }

    /// <summary>
    /// Converts a protocol candidate side into the worker's component side.
    /// </summary>
    /// <param name="side">The requested protocol side.</param>
    /// <param name="componentSide">The corresponding worker side.</param>
    /// <returns>True for Head or Body; otherwise false.</returns>
    private static bool TryGetFusionComponentSide(EvolutionCandidateSide side, out FusionComponentSide componentSide)
    {
        componentSide = side == EvolutionCandidateSide.Body ? FusionComponentSide.Body : FusionComponentSide.Head;
        return side is EvolutionCandidateSide.Head or EvolutionCandidateSide.Body;
    }

    /// <summary>
    /// Packs normal body and head identifiers into the worker's source representation.
    /// </summary>
    /// <param name="bodyId">The body identifier.</param>
    /// <param name="headId">The head identifier.</param>
    /// <returns>The packed source components.</returns>
    private static int PackComponents(int bodyId, int headId)
        => checked(bodyId << 10 | headId);

    /// <summary>
    /// Packs final custom-fusion membership into fixed numeric bit words.
    /// </summary>
    private uint[] BuildObtainableFusionWords(IReadOnlyDictionary<int, List<PlayerFusionProofPlan>> plansBySpecies)
    {
        int maximumSpeciesId = checked(_catalog.NormalSpeciesCount * _catalog.NormalSpeciesCount + _catalog.NormalSpeciesCount);
        uint[] words = new uint[(maximumSpeciesId >> 5) + 1];
        foreach ((int speciesId, List<PlayerFusionProofPlan> plans) in plansBySpecies)
        {
            if (speciesId <= _catalog.NormalSpeciesCount || speciesId > maximumSpeciesId || plans.Count == 0)
                continue;

            words[speciesId >> 5] |= 1U << (speciesId & 31);
        }

        return words;
    }

    /// <summary>
    /// Packs executable source-target pairs into one compact sorted binary index.
    /// </summary>
    private byte[] PackExecutableEvolutionEdges(IReadOnlyList<FusionEvolutionSourceAssignment> assignments)
    {
        long[] edges = [.. assignments.SelectMany(assignment =>
        {
            int body = assignment.PackedSourceComponents >> 10;
            int head = assignment.PackedSourceComponents & 0x3FF;
            uint source = checked((uint)(body * _catalog.NormalSpeciesCount + head));
            return assignment.Branches.Select(branch => ((long)source << 32) | (uint)branch.TargetId);
        }).Distinct().Order()];

        List<byte> packed = new(edges.Length * 4);
        ulong previous = 0;
        foreach (long edge in edges)
        {
            ulong current = checked((ulong)edge);
            ulong delta = current - previous;
            do
            {
                byte value = (byte)(delta & 0x7F);
                delta >>= 7;
                packed.Add(delta == 0 ? value : (byte)(value | 0x80));
            }
            while (delta != 0);
            previous = current;
        }

        return [.. packed];
    }

    /// <summary>
    /// Maps every material pair once and builds its compatible acquisition proofs.
    /// </summary>
    /// <param name="work">The validated game-owned mapping input.</param>
    /// <param name="context">The workers bound to the pinned target pool.</param>
    /// <param name="materialIndex">The optional retained complete mapping index.</param>
    /// <returns>The proof states needed by tracker-owned closure.</returns>
    private static PlayerFusionDirectProofResult BuildDirectProof(PlayerFusionClosureWorkPayload work, PlayerFusionWorkerContext context, PlayerFusionMaterialIndex? materialIndex = null)
    {
        IReadOnlyList<PlayerFusionMappedPair> mappings = materialIndex is null
            ? context.MappingWorker.MapAll(work.Seed, work.GeneratorVersion, work.MaterialIds, CancellationToken.None)
            : SelectMaterialMappings(work.MaterialIds, context.Catalog.NormalSpeciesCount, materialIndex.Mappings);
        if (mappings.Count != work.TotalPairs)
            throw new InvalidDataException("The player-fusion mapping job no longer matches the game-owned pair count.");

        HashSet<int> excludedOffsets = [.. work.ExcludedPairOffsets];
        if (excludedOffsets.Any(offset => offset < 0 || offset >= mappings.Count))
            throw new InvalidDataException("The player-fusion mapping job contains an invalid excluded pair offset.");

        HashSet<int> directOffsets = [.. work.DirectPairOffsets];
        if (directOffsets.Any(offset => offset < 0 || offset >= mappings.Count))
            throw new InvalidDataException("The player-fusion mapping job contains an invalid direct pair offset.");

        Dictionary<int, List<PlayerFusionProofPlan>> plansBySpecies = BuildBasePlans(work.BaseProofs, out long nextPlanOrder);
        HashSet<int> directEncounterFusionIds = [];
        AddCaughtFusionReversalProofs(work, plansBySpecies, ref nextPlanOrder, context);
        HashSet<int> itemFreeDirectProofIds =
        [
            .. plansBySpecies.Where(entry => entry.Value.Any(plan => plan.Items.Count == 0 && plan.Constraints.Count == 0 && plan.SourceUses.Count == 0)).Select(entry => entry.Key)
        ];
        for (int index = 0; index < mappings.Count; index++)
        {
            PlayerFusionMappedPair mapping = mappings[index];
            if (directOffsets.Contains(index))
            {
                directEncounterFusionIds.Add(mapping.FirstResultId);
                AddDirectEncounterProof(plansBySpecies, mapping.FirstResultId, ref nextPlanOrder);
                itemFreeDirectProofIds.Add(mapping.FirstResultId);
                if (mapping.FirstMaterialId != mapping.SecondMaterialId)
                {
                    directEncounterFusionIds.Add(mapping.SecondResultId);
                    AddDirectEncounterProof(plansBySpecies, mapping.SecondResultId, ref nextPlanOrder);
                    itemFreeDirectProofIds.Add(mapping.SecondResultId);
                }
            }

            if (excludedOffsets.Contains(index))
                continue;

            AddDirectFusionProof(plansBySpecies, itemFreeDirectProofIds, mapping.FirstResultId, mapping.FirstMaterialId, mapping.SecondMaterialId, work.ResourceSupply, ref nextPlanOrder);
            if (mapping.FirstMaterialId != mapping.SecondMaterialId)
                AddDirectFusionProof(plansBySpecies, itemFreeDirectProofIds, mapping.SecondResultId, mapping.SecondMaterialId, mapping.FirstMaterialId, work.ResourceSupply, ref nextPlanOrder);
        }

        return new PlayerFusionDirectProofResult(plansBySpecies, [.. directEncounterFusionIds.Order()], nextPlanOrder);
    }

    /// <summary>
    /// Selects one stable triangular material subset from the complete retained mapping index.
    /// </summary>
    /// <param name="materialIds">The distinct normal materials in numeric order.</param>
    /// <param name="normalSpeciesCount">The complete normal-species count.</param>
    /// <param name="completeMappings">The full triangular mapping index.</param>
    /// <returns>The requested mappings in subset-triangular order.</returns>
    private static IReadOnlyList<PlayerFusionMappedPair> SelectMaterialMappings(IReadOnlyList<int> materialIds, int normalSpeciesCount, IReadOnlyList<PlayerFusionMappedPair> completeMappings)
    {
        if (!materialIds.SequenceEqual(materialIds.Distinct().Order()) || materialIds.Any(value => value <= 0 || value > normalSpeciesCount))
            throw new InvalidDataException("The player-fusion material list is not in stable numeric order.");

        int expectedCompleteCount = checked(normalSpeciesCount * (normalSpeciesCount + 1) / 2);
        if (completeMappings.Count != expectedCompleteCount)
            throw new InvalidDataException("The retained player-fusion mapping index is incomplete.");

        PlayerFusionMappedPair[] result = new PlayerFusionMappedPair[checked(materialIds.Count * (materialIds.Count + 1) / 2)];
        int output = 0;
        for (int firstIndex = 0; firstIndex < materialIds.Count; firstIndex++)
        {
            int first = materialIds[firstIndex];
            int completeRow = checked((first - 1) * normalSpeciesCount - (first - 1) * (first - 2) / 2);
            for (int secondIndex = firstIndex; secondIndex < materialIds.Count; secondIndex++)
            {
                int second = materialIds[secondIndex];
                result[output++] = completeMappings[checked(completeRow + second - first)];
            }
        }

        return result;
    }

    /// <summary>
    /// Adds the global reverse partner of every directly caught fusion without consuming another source or item.
    /// </summary>
    /// <param name="work">The game-owned closure input.</param>
    /// <param name="plansBySpecies">The mutable run-wide proof states.</param>
    /// <param name="nextPlanOrder">The next stable plan order.</param>
    /// <param name="context">The workers bound to the pinned target pool.</param>
    private static void AddCaughtFusionReversalProofs(PlayerFusionClosureWorkPayload work, Dictionary<int, List<PlayerFusionProofPlan>> plansBySpecies, ref long nextPlanOrder, PlayerFusionWorkerContext context)
    {
        if (work.ReversibleFusionIds.Count != work.ReversibleFusionIds.Distinct().Count())
            throw new InvalidDataException("The caught-fusion reversal list contains duplicate species.");

        Dictionary<int, PlayerFusionProofPlan[]> seedPlans = [];
        foreach (int sourceId in work.ReversibleFusionIds)
        {
            if (!plansBySpecies.TryGetValue(sourceId, out List<PlayerFusionProofPlan>? plans))
                throw new InvalidDataException("A caught fusion marked for reversal has no base proof.");

            seedPlans.Add(sourceId, [.. plans]);
        }

        foreach ((int sourceId, PlayerFusionProofPlan[] plans) in seedPlans)
        {
            int reverseId = context.MappingWorker.ReversePartner(work.Seed, work.GeneratorVersion, sourceId);
            foreach (PlayerFusionProofPlan plan in plans)
            {
                PlayerFusionProofPlan reversed = new(plan.Items, plan.Constraints, plan.SourceUses, checked(plan.PathLength + 1), nextPlanOrder++);
                AddPlan(plansBySpecies, reverseId, reversed);
            }
        }
    }

    /// <summary>
    /// Adds one source-free proof for a fusion available directly in a wild encounter table.
    /// </summary>
    /// <param name="plansBySpecies">The mutable run-wide proof states.</param>
    /// <param name="resultId">The mapped wild-fusion result identifier.</param>
    /// <param name="nextPlanOrder">The next stable plan order.</param>
    private static void AddDirectEncounterProof(Dictionary<int, List<PlayerFusionProofPlan>> plansBySpecies, int resultId, ref long nextPlanOrder)
    {
        PlayerFusionProofPlan plan = new(_emptyCounts, _emptyConstraints, _emptyCounts, 1, nextPlanOrder++);
        AddPlan(plansBySpecies, resultId, plan);
    }

    /// <summary>
    /// Converts the game-owned seed proofs into tracker-owned feasibility states.
    /// </summary>
    /// <param name="baseProofs">The normal and caught-fusion proof payloads.</param>
    /// <param name="nextPlanOrder">The next stable plan order after the imported states.</param>
    /// <returns>The proof states keyed by numeric species identifier.</returns>
    private static Dictionary<int, List<PlayerFusionProofPlan>> BuildBasePlans(IReadOnlyList<PlayerFusionProofSpeciesPayload> baseProofs, out long nextPlanOrder)
    {
        Dictionary<int, List<PlayerFusionProofPlan>> result = [];
        long order = 0;
        foreach (PlayerFusionProofSpeciesPayload species in baseProofs)
        {
            if (species.SpeciesId <= 0 || species.Plans.Count is < 1 or > MaximumPlansPerSpecies || result.ContainsKey(species.SpeciesId))
                throw new InvalidDataException("The player-fusion proof seed is malformed.");

            List<PlayerFusionProofPlan> plans = [];
            foreach (PlayerFusionProofPlanPayload plan in species.Plans)
            {
                if (plan.PathLength < 1 || plan.Items.Any(entry => string.IsNullOrWhiteSpace(entry.Key) || entry.Value < 1)
                    || plan.Constraints.Any(entry => string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Value))
                    || plan.SourceUses.Any(entry => string.IsNullOrWhiteSpace(entry.Key) || entry.Value < 1))
                {
                    throw new InvalidDataException("A player-fusion base proof is malformed.");
                }

                plans.Add(new PlayerFusionProofPlan(
                    plan.Items.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
                    plan.Constraints.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
                    plan.SourceUses.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
                    plan.PathLength, order++));
            }

            result.Add(species.SpeciesId, plans);
        }

        nextPlanOrder = order;
        return result;
    }

    /// <summary>
    /// Adds every feasible material-plan combination for one mapped direct fusion result.
    /// </summary>
    /// <param name="plansBySpecies">The mutable run-wide proof states.</param>
    /// <param name="itemFreeDirectProofIds">Targets already retaining a dominating item-free direct proof.</param>
    /// <param name="resultId">The mapped fusion result identifier.</param>
    /// <param name="bodyId">The body material identifier.</param>
    /// <param name="headId">The head material identifier.</param>
    /// <param name="resourceSupply">The run-wide evolution-item supply.</param>
    /// <param name="nextPlanOrder">The next stable plan order.</param>
    private static void AddDirectFusionProof(Dictionary<int, List<PlayerFusionProofPlan>> plansBySpecies, HashSet<int> itemFreeDirectProofIds, int resultId, int bodyId, int headId, IReadOnlyDictionary<string, int> resourceSupply, ref long nextPlanOrder)
    {
        if (!plansBySpecies.TryGetValue(bodyId, out List<PlayerFusionProofPlan>? bodyPlans) || !plansBySpecies.TryGetValue(headId, out List<PlayerFusionProofPlan>? headPlans))
            throw new InvalidDataException("A player-fusion material has no base proof.");

        PlayerFusionProofPlan? itemFreeBody = null;
        PlayerFusionProofPlan? itemFreeHead = null;
        foreach (PlayerFusionProofPlan bodyPlan in bodyPlans)
        {
            if (bodyPlan.Items.Count != 0)
                continue;

            foreach (PlayerFusionProofPlan headPlan in headPlans)
            {
                if (headPlan.Items.Count != 0)
                    continue;

                if (!MaterialPlansCompatible(bodyPlan, headPlan, resourceSupply))
                    continue;

                itemFreeBody = bodyPlan;
                itemFreeHead = headPlan;
                break;
            }

            if (itemFreeBody is not null)
                break;
        }

        if (itemFreeBody is not null && itemFreeHead is not null)
        {
            long order = nextPlanOrder;
            nextPlanOrder = checked(nextPlanOrder + (long)bodyPlans.Count * headPlans.Count);
            if (itemFreeDirectProofIds.Add(resultId))
            {
                PlayerFusionProofPlan itemFree = new(_emptyCounts, _emptyConstraints, _emptyCounts, checked(itemFreeBody.PathLength + itemFreeHead.PathLength + 1), order);
                AddPlan(plansBySpecies, resultId, itemFree);
            }

            return;
        }

        bool feasible = false;
        foreach (PlayerFusionProofPlan bodyPlan in bodyPlans)
        {
            foreach (PlayerFusionProofPlan headPlan in headPlans)
            {
                if (!MaterialPlansCompatible(bodyPlan, headPlan, resourceSupply))
                    continue;

                feasible = true;
                long order = nextPlanOrder++;
                if (bodyPlan.Items.Count == 0 && headPlan.Items.Count == 0 && itemFreeDirectProofIds.Contains(resultId))
                    continue;

                IReadOnlyDictionary<string, int> items = bodyPlan.Items.Count == 0 && headPlan.Items.Count == 0
                    ? _emptyCounts
                    : MergeCounts(bodyPlan.Items, headPlan.Items);
                PlayerFusionProofPlan combined = new(items, _emptyConstraints, _emptyCounts, checked(bodyPlan.PathLength + headPlan.PathLength + 1), order);
                AddPlan(plansBySpecies, resultId, combined);
                if (items.Count == 0)
                    itemFreeDirectProofIds.Add(resultId);
            }
        }

        if (!feasible)
            throw new InvalidDataException("A non-excluded player-fusion pair has no compatible proof state.");

    }

    /// <summary>
    /// Determines whether two normal-material proof states satisfy the game-owned constraint rules.
    /// </summary>
    /// <param name="body">The body proof state.</param>
    /// <param name="head">The head proof state.</param>
    /// <param name="resourceSupply">The run-wide evolution-item supply.</param>
    /// <returns>True when the material proof states can be combined.</returns>
    private static bool MaterialPlansCompatible(PlayerFusionProofPlan body, PlayerFusionProofPlan head, IReadOnlyDictionary<string, int> resourceSupply)
    {
        foreach ((string key, string expected) in body.Constraints)
        {
            if (head.Constraints.TryGetValue(key, out string? actual) && actual != expected)
                return false;
        }

        foreach ((string key, int count) in body.Items)
        {
            if (count + head.Items.GetValueOrDefault(key) > resourceSupply.GetValueOrDefault(key))
                return false;
        }

        foreach ((string key, int count) in head.Items)
        {
            if (!body.Items.ContainsKey(key) && count > resourceSupply.GetValueOrDefault(key))
                return false;
        }

        foreach ((string key, int count) in body.SourceUses)
        {
            if (count + head.SourceUses.GetValueOrDefault(key) > 1)
                return false;
        }

        foreach ((string key, int count) in head.SourceUses)
        {
            if (!body.SourceUses.ContainsKey(key) && count > 1)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Performs the exact ascending-strength fusion-evolution closure in tracker memory.
    /// </summary>
    /// <param name="plansBySpecies">The seeded normal, caught-fusion, and direct-fusion proof states.</param>
    /// <param name="assignments">Every exact generated fusion-evolution assignment.</param>
    /// <param name="resourceSupply">The run-wide evolution-item supply.</param>
    /// <param name="nextPlanOrder">The next stable plan order.</param>
    /// <param name="context">The workers bound to the pinned target pool.</param>
    /// <returns>Only source branches backed by an executable acquisition proof.</returns>
    private static IReadOnlyList<FusionEvolutionSourceAssignment> CloseFusionEvolutions(
        Dictionary<int, List<PlayerFusionProofPlan>> plansBySpecies,
        IReadOnlyList<FusionEvolutionSourceAssignment> assignments,
        IReadOnlyDictionary<string, int> resourceSupply, long nextPlanOrder,
        PlayerFusionWorkerContext context)
    {
        PlayerFusionMappingWorkerCatalog catalog = context.Catalog;
        Dictionary<string, PlayerFusionEvolutionBranch> branchesByIdentity = catalog.EvolutionBranches.ToDictionary(branch => branch.Identity, StringComparer.Ordinal);
        List<FusionEvolutionSourceAssignment> executable = [];
        foreach (FusionEvolutionSourceAssignment assignment in assignments.OrderBy(value => value.SourceBst).ThenBy(value => value.PackedSourceComponents))
        {
            int body = assignment.PackedSourceComponents >> 10;
            int head = assignment.PackedSourceComponents & 0x3FF;
            int sourceId = checked(body * catalog.NormalSpeciesCount + head);
            if (!plansBySpecies.TryGetValue(sourceId, out List<PlayerFusionProofPlan>? sourcePlans) || sourcePlans.Count == 0)
                continue;

            List<FusionEvolutionAssignedBranch> executableBranches = [];
            foreach (FusionEvolutionAssignedBranch branch in assignment.Branches)
            {
                PlayerFusionEvolutionBranch componentBranch = branchesByIdentity.GetValueOrDefault(branch.ComponentBranchIdentity)
                    ?? throw new InvalidDataException("A fusion-evolution assignment references an unknown conceptual branch.");

                bool feasible = false;
                PlayerFusionProofPlan[] stableSourcePlans = [.. sourcePlans];
                foreach (PlayerFusionProofPlan sourcePlan in stableSourcePlans)
                {
                    foreach (string item in componentBranch.ItemOptions)
                    {
                        PlayerFusionProofPlan? next = EvolvePlan(sourcePlan, item, resourceSupply, nextPlanOrder);
                        if (next is null)
                            continue;

                        feasible = true;
                        nextPlanOrder++;
                        AddPlan(plansBySpecies, branch.TargetId, next);
                    }
                }

                if (feasible)
                    executableBranches.Add(branch);
            }

            if (executableBranches.Count > 0)
                executable.Add(new FusionEvolutionSourceAssignment(assignment.PackedSourceComponents, assignment.SourceBst, executableBranches));
        }

        return executable;
    }

    /// <summary>
    /// Applies one alternative evolution method to a proof state.
    /// </summary>
    /// <param name="source">The source proof state.</param>
    /// <param name="item">The consumed item identifier, or an empty value for an item-free method.</param>
    /// <param name="resourceSupply">The run-wide item supply.</param>
    /// <param name="order">The stable order for the evolved proof.</param>
    /// <returns>The evolved state, or null when its item demand exceeds supply.</returns>
    private static PlayerFusionProofPlan? EvolvePlan(PlayerFusionProofPlan source, string item, IReadOnlyDictionary<string, int> resourceSupply, long order)
    {
        Dictionary<string, int> items = source.Items.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(item))
            items[item] = items.GetValueOrDefault(item) + 1;

        if (!WithinSupply(items, resourceSupply))
            return null;

        return new PlayerFusionProofPlan(items, source.Constraints, source.SourceUses, checked(source.PathLength + 1), order);
    }

    /// <summary>
    /// Adds one nondominated proof state with the same bounded ordering as the game service.
    /// </summary>
    /// <param name="plansBySpecies">The mutable proof index.</param>
    /// <param name="speciesId">The target species identifier.</param>
    /// <param name="candidate">The candidate proof state.</param>
    /// <returns>True when the candidate remains in the bounded nondominated set.</returns>
    private static bool AddPlan(Dictionary<int, List<PlayerFusionProofPlan>> plansBySpecies, int speciesId, PlayerFusionProofPlan candidate)
    {
        if (!plansBySpecies.TryGetValue(speciesId, out List<PlayerFusionProofPlan>? plans))
        {
            plans = [];
            plansBySpecies.Add(speciesId, plans);
        }

        if (plans.Any(existing => Dominates(existing, candidate)))
            return false;

        plans.RemoveAll(existing => Dominates(candidate, existing));
        plans.Add(candidate);
        plans.Sort(static (first, second) =>
        {
            int result = first.Items.Values.Sum().CompareTo(second.Items.Values.Sum());
            if (result != 0)
                return result;

            result = first.Constraints.Count.CompareTo(second.Constraints.Count);
            if (result != 0)
                return result;

            result = first.PathLength.CompareTo(second.PathLength);
            return result != 0 ? result : first.Order.CompareTo(second.Order);
        });

        if (plans.Count > MaximumPlansPerSpecies)
            plans.RemoveRange(MaximumPlansPerSpecies, plans.Count - MaximumPlansPerSpecies);

        return plans.Contains(candidate);
    }

    /// <summary>
    /// Determines whether one proof makes another proof unnecessary.
    /// </summary>
    private static bool Dominates(PlayerFusionProofPlan first, PlayerFusionProofPlan second)
    {
        return DictionariesEqual(first.Constraints, second.Constraints)
            && first.Items.All(entry => entry.Value <= second.Items.GetValueOrDefault(entry.Key))
            && first.SourceUses.All(entry => entry.Value <= second.SourceUses.GetValueOrDefault(entry.Key));
    }

    /// <summary>
    /// Determines whether two constraint maps contain identical ordinal entries.
    /// </summary>
    private static bool DictionariesEqual(IReadOnlyDictionary<string, string> first, IReadOnlyDictionary<string, string> second)
        => first.Count == second.Count && first.All(entry => second.TryGetValue(entry.Key, out string? value) && value == entry.Value);

    /// <summary>
    /// Merges two counted resource maps.
    /// </summary>
    private static Dictionary<string, int> MergeCounts(IReadOnlyDictionary<string, int> first, IReadOnlyDictionary<string, int> second)
    {
        Dictionary<string, int> result = first.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        foreach ((string key, int count) in second)
            result[key] = result.GetValueOrDefault(key) + count;

        return result;
    }

    /// <summary>
    /// Determines whether every consumed item remains within the run-wide supply.
    /// </summary>
    private static bool WithinSupply(IReadOnlyDictionary<string, int> items, IReadOnlyDictionary<string, int> resourceSupply)
        => items.All(entry => entry.Value <= resourceSupply.GetValueOrDefault(entry.Key));

    /// <summary>
    /// Resolves the parallel workers for the pinned runtime pool while keeping
    /// release-stable normal and evolution metadata embedded in the tracker.
    /// </summary>
    private PlayerFusionWorkerContext? ResolveWorkerContext(PlayerFusionClosureWorkPayload work)
    {
        if (work.GeneratorVersion != _catalog.PlayerFusionGeneratorVersion
            || work.SourceCatalogFingerprint != _sourceCatalog.Fingerprint
            || work.BaseStatSourceFingerprint != _catalog.BaseStatSourceFingerprint
            || work.FusionEvolutionGeneratorVersion != _catalog.FusionEvolutionGeneratorVersion
            || work.FusionEvolutionRulesVersion != _catalog.FusionEvolutionRulesVersion
            || work.EvolutionSourceFingerprint != _catalog.EvolutionSourceFingerprint
            || work.EvolutionTaxonomyFingerprint != _catalog.EvolutionTaxonomyFingerprint
            || work.EvolutionMethodFingerprint != _catalog.EvolutionMethodFingerprint)
        {
            return null;
        }

        if (_workerContexts.TryGetValue(work.CustomFusionPoolFingerprint, out PlayerFusionWorkerContext? retained))
        {
            return retained.Catalog.CustomFusionPoolVersion == work.CustomFusionPoolVersion && retained.Catalog.CustomFusionPool.Count == work.CustomFusionPoolSize
                ? retained
                : null;
        }

        if (string.IsNullOrWhiteSpace(work.PackedCustomFusionPool))
            return null;

        PlayerFusionMappingWorkerCatalog runtimeCatalog = _catalog.WithRuntimeFusionPool(work.CustomFusionPoolVersion, work.CustomFusionPoolSize, work.CustomFusionPoolFingerprint, work.PackedCustomFusionPool);
        PlayerFusionWorkerContext context = _workerContexts.GetOrAdd(work.CustomFusionPoolFingerprint, _ => new PlayerFusionWorkerContext(runtimeCatalog));
        return context.Catalog.CustomFusionPoolVersion == work.CustomFusionPoolVersion && context.Catalog.CustomFusionPool.Count == work.CustomFusionPoolSize
            ? context
            : null;
    }

    /// <summary>
    /// Gets the parallel worker context registered for one custom-fusion pool.
    /// </summary>
    /// <param name="fingerprint">The exact custom-fusion pool fingerprint.</param>
    /// <returns>The worker context bound to the requested pool.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no compatible worker context has been registered.</exception>
    private PlayerFusionWorkerContext WorkerContext(string fingerprint)
    {
        return _workerContexts.TryGetValue(fingerprint, out PlayerFusionWorkerContext? context)
            ? context
            : throw new InvalidOperationException("The pinned custom-fusion worker context is unavailable.");
    }

    /// <summary>
    /// Determines whether one completed recipe matches the tracker-owned fusion-evolution catalog.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <returns>True when exact tracker-side assignments are safe to use.</returns>
    private bool Compatible(CompletedRunRecipePayload recipe)
    {
        EvolutionGeneratorRecipePayload? evolution = recipe.EvolutionGenerator;
        if (!_workerContexts.TryGetValue(recipe.PlayerFusionGenerator.PoolFingerprint, out PlayerFusionWorkerContext? context))
            return false;

        PlayerFusionMappingWorkerCatalog catalog = context.Catalog;
        return evolution is not null
            && recipe.PlayerFusionGenerator.Version == catalog.PlayerFusionGeneratorVersion
            && recipe.PlayerFusionGenerator.PoolSize == catalog.CustomFusionPool.Count
            && recipe.PlayerFusionGenerator.PoolFingerprint == catalog.CustomFusionPoolFingerprint
            && evolution.Fusion.Version == catalog.FusionEvolutionGeneratorVersion
            && evolution.Fusion.RulesVersion == catalog.FusionEvolutionRulesVersion
            && evolution.SourceFingerprint == catalog.EvolutionSourceFingerprint
            && evolution.TaxonomyFingerprint == catalog.EvolutionTaxonomyFingerprint
            && evolution.MethodFingerprint == catalog.EvolutionMethodFingerprint
            && evolution.BaseStatGenerator.SourceFingerprint == catalog.BaseStatSourceFingerprint
            && evolution.Fusion.TargetPool.Version == catalog.CustomFusionPoolVersion
            && evolution.Fusion.TargetPool.Size == catalog.CustomFusionPool.Count
            && evolution.Fusion.TargetPool.Fingerprint == catalog.CustomFusionPoolFingerprint;
    }

    /// <summary>
    /// Determines whether one active-run assignment recipe matches the tracker-owned fusion-evolution catalog.
    /// </summary>
    /// <param name="recipe">The active-run deterministic assignment recipe.</param>
    /// <returns>True when exact tracker-side assignments are safe to prepare.</returns>
    private bool Compatible(FusionAssignmentRecipePayload recipe)
    {
        if (!_workerContexts.TryGetValue(recipe.TargetPoolFingerprint, out PlayerFusionWorkerContext? context))
            return false;

        return Compatible(recipe, context.Catalog);
    }

    /// <summary>
    /// Determines whether an active-run assignment recipe matches one resolved worker catalog.
    /// </summary>
    /// <param name="recipe">The authorized active-run assignment recipe.</param>
    /// <param name="catalog">The embedded or pinned pool-specific worker catalog.</param>
    /// <returns>True when exact tracker-side assignments are safe to prepare.</returns>
    private static bool Compatible(FusionAssignmentRecipePayload recipe, PlayerFusionMappingWorkerCatalog catalog)
    {
        return recipe.PlayerFusionGeneratorVersion == catalog.PlayerFusionGeneratorVersion
            && recipe.GeneratorVersion == catalog.FusionEvolutionGeneratorVersion
            && recipe.RulesVersion == catalog.FusionEvolutionRulesVersion
            && recipe.SourceFingerprint == catalog.EvolutionSourceFingerprint
            && recipe.TaxonomyFingerprint == catalog.EvolutionTaxonomyFingerprint
            && recipe.MethodFingerprint == catalog.EvolutionMethodFingerprint
            && recipe.BaseStatSourceFingerprint == catalog.BaseStatSourceFingerprint
            && recipe.TargetPoolVersion == catalog.CustomFusionPoolVersion
            && recipe.TargetPoolSize == catalog.CustomFusionPool.Count
            && recipe.TargetPoolFingerprint == catalog.CustomFusionPoolFingerprint;
    }

    /// <summary>
    /// Resolves or constructs the worker context disclosed by active-run recovery.
    /// </summary>
    /// <param name="recipe">The authorized active-run assignment recipe and exact pinned pool membership.</param>
    /// <returns>The matching worker context, or null when the recipe is incompatible.</returns>
    private PlayerFusionWorkerContext? ResolveWorkerContext(FusionAssignmentRecipePayload recipe)
    {
        if (_workerContexts.TryGetValue(recipe.TargetPoolFingerprint, out PlayerFusionWorkerContext? retained))
        {
            return retained.Catalog.CustomFusionPoolVersion == recipe.TargetPoolVersion && retained.Catalog.CustomFusionPool.Count == recipe.TargetPoolSize
                ? retained
                : null;
        }

        if (string.IsNullOrWhiteSpace(recipe.PackedCustomFusionPool))
            return null;

        PlayerFusionMappingWorkerCatalog runtimeCatalog = _catalog.WithRuntimeFusionPool(recipe.TargetPoolVersion, recipe.TargetPoolSize, recipe.TargetPoolFingerprint, recipe.PackedCustomFusionPool);
        PlayerFusionWorkerContext context = _workerContexts.GetOrAdd(recipe.TargetPoolFingerprint, _ => new PlayerFusionWorkerContext(runtimeCatalog));
        return context.Catalog.CustomFusionPoolVersion == recipe.TargetPoolVersion && context.Catalog.CustomFusionPool.Count == recipe.TargetPoolSize
            ? context
            : null;
    }
}

/// <summary>
/// Groups the validated catalog and native parallel workers for one exact custom-fusion pool.
/// </summary>
internal sealed class PlayerFusionWorkerContext
{
    /// <summary>
    /// Gets the validated catalog for this worker context.
    /// </summary>
    internal PlayerFusionMappingWorkerCatalog Catalog { get; }

    /// <summary>
    /// Gets the native player-fusion mapping worker for this pool.
    /// </summary>
    internal PlayerFusionMappingWorker MappingWorker { get; }

    /// <summary>
    /// Gets the native fusion-evolution assignment worker for this pool.
    /// </summary>
    internal FusionEvolutionAssignmentWorker EvolutionWorker { get; }

    /// <summary>
    /// Initializes a worker context from one validated pool-specific catalog.
    /// </summary>
    /// <param name="catalog">The validated catalog shared by both native workers.</param>
    internal PlayerFusionWorkerContext(PlayerFusionMappingWorkerCatalog catalog)
    {
        Catalog = catalog;
        MappingWorker = new PlayerFusionMappingWorker(catalog);
        EvolutionWorker = new FusionEvolutionAssignmentWorker(catalog);
    }
}

/// <summary>
/// Identifies one deterministic fusion-evolution assignment set.
/// </summary>
/// <param name="Seed">The run seed.</param>
/// <param name="GeneratorVersion">The fusion-evolution generator version.</param>
/// <param name="RulesVersion">The fusion-evolution rules version.</param>
/// <param name="SourceFingerprint">The normal evolution source fingerprint.</param>
/// <param name="TaxonomyFingerprint">The evolution taxonomy fingerprint.</param>
/// <param name="MethodFingerprint">The effective-method fingerprint.</param>
/// <param name="BaseStatFingerprint">The base-stat source fingerprint.</param>
/// <param name="TargetPoolFingerprint">The custom target-pool fingerprint.</param>
internal sealed record EvolutionAssignmentKey(long Seed, int GeneratorVersion, int RulesVersion, string SourceFingerprint, string TaxonomyFingerprint, string MethodFingerprint, string BaseStatFingerprint, string TargetPoolFingerprint)
{
    /// <summary>
    /// Creates the assignment identity carried by tracker mapping work.
    /// </summary>
    /// <param name="work">The game-owned compatible mapping work.</param>
    /// <returns>The deterministic assignment identity.</returns>
    internal static EvolutionAssignmentKey From(PlayerFusionClosureWorkPayload work)
        => new(work.Seed, work.FusionEvolutionGeneratorVersion, work.FusionEvolutionRulesVersion, work.EvolutionSourceFingerprint, work.EvolutionTaxonomyFingerprint, work.EvolutionMethodFingerprint, work.BaseStatSourceFingerprint, work.CustomFusionPoolFingerprint);

    /// <summary>
    /// Creates the assignment identity carried by a completed recipe.
    /// </summary>
    /// <param name="recipe">The compatible completed recipe.</param>
    /// <returns>The deterministic assignment identity.</returns>
    internal static EvolutionAssignmentKey From(CompletedRunRecipePayload recipe)
    {
        EvolutionGeneratorRecipePayload evolution = recipe.EvolutionGenerator
            ?? throw new InvalidOperationException("The completed recipe does not contain evolution metadata.");

        return new EvolutionAssignmentKey(recipe.Seed, evolution.Fusion.Version, evolution.Fusion.RulesVersion, evolution.SourceFingerprint, evolution.TaxonomyFingerprint, evolution.MethodFingerprint, evolution.BaseStatGenerator.SourceFingerprint, evolution.Fusion.TargetPool.Fingerprint);
    }

    /// <summary>
    /// Creates the assignment identity carried by an active-run recovery recipe.
    /// </summary>
    /// <param name="recipe">The compatible active-run assignment recipe.</param>
    /// <returns>The deterministic assignment identity.</returns>
    internal static EvolutionAssignmentKey From(FusionAssignmentRecipePayload recipe)
        => new(recipe.Seed, recipe.GeneratorVersion, recipe.RulesVersion, recipe.SourceFingerprint, recipe.TaxonomyFingerprint, recipe.MethodFingerprint, recipe.BaseStatSourceFingerprint, recipe.TargetPoolFingerprint);
}

/// <summary>
/// Identifies one deterministic player-fusion material assignment set.
/// </summary>
/// <param name="Seed">The Ironmon run seed.</param>
/// <param name="GeneratorVersion">The player-fusion generator version.</param>
/// <param name="BaseStatFingerprint">The base-stat source fingerprint.</param>
/// <param name="TargetPoolFingerprint">The custom target-pool fingerprint.</param>
internal sealed record PlayerFusionMaterialKey(long Seed, int GeneratorVersion, string BaseStatFingerprint, string TargetPoolFingerprint)
{
    /// <summary>
    /// Creates the material identity carried by a completed recipe.
    /// </summary>
    /// <param name="recipe">The compatible completed recipe.</param>
    /// <returns>The deterministic material identity.</returns>
    internal static PlayerFusionMaterialKey From(CompletedRunRecipePayload recipe)
    {
        EvolutionGeneratorRecipePayload evolution = recipe.EvolutionGenerator
            ?? throw new InvalidOperationException("The completed recipe does not contain evolution metadata.");

        return new PlayerFusionMaterialKey(recipe.Seed, recipe.PlayerFusionGenerator.Version, evolution.BaseStatGenerator.SourceFingerprint, recipe.PlayerFusionGenerator.PoolFingerprint);
    }

    /// <summary>
    /// Creates the material identity carried by an active-run assignment recipe.
    /// </summary>
    /// <param name="recipe">The compatible active-run assignment recipe.</param>
    /// <returns>The deterministic material identity.</returns>
    internal static PlayerFusionMaterialKey From(FusionAssignmentRecipePayload recipe)
        => new(recipe.Seed, recipe.PlayerFusionGeneratorVersion, recipe.BaseStatSourceFingerprint, recipe.TargetPoolFingerprint);
}

/// <summary>
/// Stores exact ordered normal-material assignments grouped by custom-fusion target.
/// </summary>
/// <param name="Mappings">The complete mappings in stable triangular material order.</param>
/// <param name="PackedAssignmentsByTarget">The packed ordered assignments keyed by numeric target identifier.</param>
internal sealed record PlayerFusionMaterialIndex(IReadOnlyList<PlayerFusionMappedPair> Mappings, IReadOnlyDictionary<int, IReadOnlyList<uint>> PackedAssignmentsByTarget);

/// <summary>
/// Stores one bounded exact material-assignment page.
/// </summary>
/// <param name="Assignments">The assignments on the requested page.</param>
/// <param name="Total">The complete assignment count for the target.</param>
internal sealed record PlayerFusionMaterialPage(IReadOnlyList<FusionMaterialAssignmentPayload> Assignments, int Total);

/// <summary>
/// Stores one seed's exact assignments and their lightweight reverse target index.
/// </summary>
/// <param name="Assignments">Every branchful exact custom-fusion source assignment.</param>
/// <param name="PredecessorAssignments">The exact predecessor branches grouped by assigned target.</param>
internal sealed record FusionEvolutionAssignmentIndex(IReadOnlyList<FusionEvolutionSourceAssignment> Assignments, IReadOnlyDictionary<int, IReadOnlyList<FusionEvolutionIndexedBranch>> PredecessorAssignments);

/// <summary>
/// Stores one reverse-index entry without duplicating its source or assigned branch.
/// </summary>
/// <param name="Source">The exact source assignment.</param>
/// <param name="Branch">The branch assigned to the indexed target.</param>
/// <param name="SourceId">The numeric custom-fusion source identifier.</param>
/// <param name="SourceRank">The source's stable graph-result rank.</param>
/// <param name="BranchIndex">The branch's stable position within its source assignment.</param>
internal sealed record FusionEvolutionIndexedBranch(FusionEvolutionSourceAssignment Source, FusionEvolutionAssignedBranch Branch, int SourceId, int SourceRank, int BranchIndex);

/// <summary>
/// Stores the mutable proof states needed by tracker-owned closure.
/// </summary>
/// <param name="PlansBySpecies">The nondominated proof states keyed by numeric species identifier.</param>
/// <param name="DirectEncounterFusionIds">The mapped fusion results available through derived wild encounters.</param>
/// <param name="NextPlanOrder">The next stable proof order after direct fusion closure.</param>
internal sealed record PlayerFusionDirectProofResult(Dictionary<int, List<PlayerFusionProofPlan>> PlansBySpecies, IReadOnlyList<int> DirectEncounterFusionIds, long NextPlanOrder);

/// <summary>
/// Stores the compact feasibility state of one tracker-owned acquisition proof.
/// </summary>
/// <param name="Items">The consumed evolution-item quantities.</param>
/// <param name="Constraints">The mutually exclusive run choices.</param>
/// <param name="SourceUses">The consumed unique acquisition sources.</param>
/// <param name="PathLength">The number of acquisition operations.</param>
/// <param name="Order">The stable insertion order used for bounded ties.</param>
internal sealed record PlayerFusionProofPlan(IReadOnlyDictionary<string, int> Items, IReadOnlyDictionary<string, string> Constraints, IReadOnlyDictionary<string, int> SourceUses, int PathLength, long Order);

/// <summary>
/// Stores the compact immutable tracker-computed result for one run.
/// </summary>
/// <param name="ObtainableFusionWords">The final numeric custom-fusion membership bitset.</param>
/// <param name="DirectEncounterFusionIds">The mapped fusion results available through derived wild encounters.</param>
/// <param name="PackedExecutableEvolutionEdges">The sorted executable source-target edge index.</param>
/// <param name="ObtainableCount">The final normal and fusion species count.</param>
internal sealed record PlayerFusionWorkerResult(IReadOnlyList<uint> ObtainableFusionWords, IReadOnlyList<int> DirectEncounterFusionIds, byte[] PackedExecutableEvolutionEdges, int ObtainableCount);
