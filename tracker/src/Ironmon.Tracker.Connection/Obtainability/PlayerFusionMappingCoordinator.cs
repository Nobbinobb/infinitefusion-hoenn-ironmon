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
    private readonly PlayerFusionMappingWorkerCatalog _catalog;
    private readonly ConcurrentDictionary<EvolutionAssignmentKey, Lazy<Task<FusionEvolutionAssignmentIndex>>> _evolutionJobs = new();
    private readonly ConcurrentDictionary<PlayerFusionMaterialKey, Lazy<Task<PlayerFusionMaterialIndex>>> _materialJobs = new();
    private readonly ConcurrentDictionary<string, Task<PlayerFusionWorkerResult>> _jobs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, EvolutionAssignmentKey> _runEvolutionKeys = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PlayerFusionMaterialKey> _runMaterialKeys = new(StringComparer.Ordinal);
    private readonly FusionEvolutionAssignmentWorker _evolutionWorker;
    private readonly ObtainabilitySourceCatalog _sourceCatalog;
    private readonly PlayerFusionMappingWorker _worker;

    /// <summary>
    /// Initializes a coordinator from the embedded generated mapping catalog.
    /// </summary>
    internal PlayerFusionMappingCoordinator()
    {
        _catalog = PlayerFusionMappingWorkerCatalog.Load();
        _sourceCatalog = ObtainabilitySourceCatalog.Load();
        _worker = new PlayerFusionMappingWorker(_catalog);
        _evolutionWorker = new FusionEvolutionAssignmentWorker(_catalog);
    }

    /// <summary>
    /// Gets the number of mapping jobs currently retained for delivery.
    /// </summary>
    internal int CachedJobCount => _jobs.Count;

    /// <summary>
    /// Starts or reuses the exact assignment index as soon as an authorized active run is recovered.
    /// </summary>
    /// <param name="runId">The active run identifier.</param>
    /// <param name="recipe">The deterministic assignment recipe supplied by the game.</param>
    /// <returns>True when the recipe matches the embedded tracker catalog; otherwise false.</returns>
    internal bool PrepareActiveFusionAssignments(string runId, FusionAssignmentRecipePayload recipe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(recipe);
        if (!Compatible(recipe))
            return false;

        EvolutionAssignmentKey key = EvolutionAssignmentKey.From(recipe);
        _runEvolutionKeys[runId] = key;
        _runMaterialKeys[runId] = PlayerFusionMaterialKey.From(recipe);
        _ = GetEvolutionAssignmentIndex(key);
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

        if (!Compatible(work))
            return null;

        EvolutionAssignmentKey evolutionKey = EvolutionAssignmentKey.From(work);
        if (!string.IsNullOrWhiteSpace(runId))
            _runEvolutionKeys[runId] = evolutionKey;

        Task<PlayerFusionWorkerResult> job = _jobs.GetOrAdd(work.JobId, _ => Task.Run(() => BuildResult(work, evolutionKey), CancellationToken.None));
        PlayerFusionWorkerResult result = await job.WaitAsync(cancellationToken).ConfigureAwait(false);
        _jobs.TryRemove(work.JobId, out _);

        return new PlayerFusionClosureResultPayload
        {
            JobId = work.JobId,
            ObtainableFusionWords = result.ObtainableFusionWords,
            PackedExecutableEvolutionEdges = result.PackedExecutableEvolutionEdges,
            ObtainableCount = result.ObtainableCount
        };
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
        return CreateTargetPayloads(_evolutionWorker.Generate(key.Seed, PackComponents(bodyId, headId)));
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

        return CreateTargetPayloads(_evolutionWorker.Generate(key.Seed, PackComponents(bodyId, headId)));
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
        return PackCandidateTargets(_evolutionWorker.GetCandidateTargets(key.Seed, PackComponents(bodyId, headId), componentSide));
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

        return PackCandidateTargets(_evolutionWorker.GetCandidateTargets(key.Seed, PackComponents(bodyId, headId), componentSide));
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
    /// Builds the compact run-wide worker result.
    /// </summary>
    /// <param name="work">The validated game-owned job description.</param>
    /// <param name="evolutionKey">The compatible deterministic assignment identity.</param>
    /// <returns>The final obtainability membership and executable-edge indexes.</returns>
    private PlayerFusionWorkerResult BuildResult(PlayerFusionClosureWorkPayload work, EvolutionAssignmentKey evolutionKey)
    {
        PlayerFusionDirectProofResult? directProof = null;
        FusionEvolutionAssignmentIndex? evolutionIndex = null;
        void buildDirectProof() => directProof = BuildDirectProof(work);
        void loadEvolutionIndex() => evolutionIndex = GetEvolutionAssignmentIndex(evolutionKey).GetAwaiter().GetResult();
        Parallel.Invoke(buildDirectProof, loadEvolutionIndex);

        PlayerFusionDirectProofResult provenDirect = directProof
            ?? throw new InvalidDataException("The direct fusion worker did not return a result.");

        IReadOnlyList<FusionEvolutionSourceAssignment> generatedAssignments = evolutionIndex?.Assignments
            ?? throw new InvalidDataException("The fusion-evolution worker did not return a result.");

        IReadOnlyList<FusionEvolutionSourceAssignment> executableAssignments = CloseFusionEvolutions(provenDirect.PlansBySpecies, generatedAssignments, work.ResourceSupply, provenDirect.NextPlanOrder);
        uint[] obtainableFusionWords = BuildObtainableFusionWords(provenDirect.PlansBySpecies);
        byte[] packedExecutableEdges = PackExecutableEvolutionEdges(executableAssignments);
        int obtainableCount = provenDirect.PlansBySpecies.Count(entry => entry.Value.Count > 0);

        return new PlayerFusionWorkerResult(obtainableFusionWords, packedExecutableEdges, obtainableCount);
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
        int[] materialIds = [.. Enumerable.Range(1, _catalog.NormalSpeciesCount)];
        IReadOnlyList<PlayerFusionMappedPair> mappings = _worker.MapAll(key.Seed, key.GeneratorVersion, materialIds, CancellationToken.None);
        Dictionary<int, List<uint>> assignments = [];
        foreach (PlayerFusionMappedPair mapping in mappings)
        {
            AddMaterialAssignment(assignments, mapping.FirstResultId, mapping.FirstMaterialId, mapping.SecondMaterialId);
            if (mapping.FirstMaterialId != mapping.SecondMaterialId)
                AddMaterialAssignment(assignments, mapping.SecondResultId, mapping.SecondMaterialId, mapping.FirstMaterialId);
        }

        Dictionary<int, IReadOnlyList<uint>> orderedAssignments = assignments.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<uint>)[.. entry.Value.Order()]);
        return new PlayerFusionMaterialIndex(orderedAssignments);
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
        => new(() => Task.Run(() => BuildEvolutionAssignmentIndex(key.Seed), CancellationToken.None), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Generates all exact assignments once and builds their reverse target index.
    /// </summary>
    /// <param name="seed">The run seed.</param>
    /// <returns>The exact assignments and reverse branches grouped by assigned target.</returns>
    private FusionEvolutionAssignmentIndex BuildEvolutionAssignmentIndex(long seed)
    {
        int[] packedSources = [.. _catalog.CustomFusionPool.Select(target => target.PackedComponents)];
        IReadOnlyList<FusionEvolutionSourceAssignment> assignments = _evolutionWorker.GenerateAll(seed, packedSources, CancellationToken.None);

        Dictionary<int, int> bodyRanks = _catalog.NormalSpecies
            .OrderBy(species => $"{species.Id}{FusionBodyLexicalSuffix}", StringComparer.Ordinal)
            .Select((species, index) => (species.Id, index))
            .ToDictionary(entry => entry.Id, entry => entry.index);

        Dictionary<int, int> headRanks = _catalog.NormalSpecies
            .OrderBy(species => species.Id.ToString(), StringComparer.Ordinal)
            .Select((species, index) => (species.Id, index))
            .ToDictionary(entry => entry.Id, entry => entry.index);

        FusionEvolutionSourceAssignment[] branchfulAssignments = [.. assignments.Where(assignment => assignment.Branches.Count > 0)];
        Dictionary<int, List<FusionEvolutionIndexedBranch>> assignmentsByTarget = [];
        foreach (FusionEvolutionSourceAssignment assignment in branchfulAssignments)
        {
            int bodyId = assignment.PackedSourceComponents >> 10;
            int headId = assignment.PackedSourceComponents & 0x3FF;
            int sourceId = checked(bodyId * _catalog.NormalSpeciesCount + headId);
            int rank = checked(bodyRanks[bodyId] * _catalog.NormalSpeciesCount + headRanks[headId]);
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
    /// <returns>The proof states needed by tracker-owned closure.</returns>
    private PlayerFusionDirectProofResult BuildDirectProof(PlayerFusionClosureWorkPayload work)
    {
        IReadOnlyList<PlayerFusionMappedPair> mappings = _worker.MapAll(work.Seed, work.GeneratorVersion, work.MaterialIds, CancellationToken.None);
        if (mappings.Count != work.TotalPairs)
            throw new InvalidDataException("The player-fusion mapping job no longer matches the game-owned pair count.");

        HashSet<int> excludedOffsets = [.. work.ExcludedPairOffsets];
        if (excludedOffsets.Any(offset => offset < 0 || offset >= mappings.Count))
            throw new InvalidDataException("The player-fusion mapping job contains an invalid excluded pair offset.");

        Dictionary<int, List<PlayerFusionProofPlan>> plansBySpecies = BuildBasePlans(work.BaseProofs, out long nextPlanOrder);
        for (int index = 0; index < mappings.Count; index++)
        {
            if (excludedOffsets.Contains(index))
                continue;

            PlayerFusionMappedPair mapping = mappings[index];
            AddDirectFusionProof(plansBySpecies, mapping.FirstResultId, mapping.FirstMaterialId, mapping.SecondMaterialId, work.ResourceSupply, ref nextPlanOrder);
            if (mapping.FirstMaterialId != mapping.SecondMaterialId)
                AddDirectFusionProof(plansBySpecies, mapping.SecondResultId, mapping.SecondMaterialId, mapping.FirstMaterialId, work.ResourceSupply, ref nextPlanOrder);
        }

        return new PlayerFusionDirectProofResult(plansBySpecies, nextPlanOrder);
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
    /// <param name="resultId">The mapped fusion result identifier.</param>
    /// <param name="bodyId">The body material identifier.</param>
    /// <param name="headId">The head material identifier.</param>
    /// <param name="resourceSupply">The run-wide evolution-item supply.</param>
    /// <param name="nextPlanOrder">The next stable plan order.</param>
    private static void AddDirectFusionProof(Dictionary<int, List<PlayerFusionProofPlan>> plansBySpecies, int resultId, int bodyId, int headId, IReadOnlyDictionary<string, int> resourceSupply, ref long nextPlanOrder)
    {
        if (!plansBySpecies.TryGetValue(bodyId, out List<PlayerFusionProofPlan>? bodyPlans) || !plansBySpecies.TryGetValue(headId, out List<PlayerFusionProofPlan>? headPlans))
            throw new InvalidDataException("A player-fusion material has no base proof.");

        bool feasible = false;
        foreach (PlayerFusionProofPlan bodyPlan in bodyPlans)
        {
            foreach (PlayerFusionProofPlan headPlan in headPlans)
            {
                PlayerFusionProofPlan? combined = CombineMaterialPlans(bodyPlan, headPlan, resourceSupply, nextPlanOrder);
                if (combined is null)
                    continue;

                feasible = true;
                nextPlanOrder++;
                AddPlan(plansBySpecies, resultId, combined);
            }
        }

        if (!feasible)
            throw new InvalidDataException("A non-excluded player-fusion pair has no compatible proof state.");
    }

    /// <summary>
    /// Combines two normal-material proof states using the game-owned constraint rules.
    /// </summary>
    /// <param name="body">The body proof state.</param>
    /// <param name="head">The head proof state.</param>
    /// <param name="resourceSupply">The run-wide evolution-item supply.</param>
    /// <param name="order">The stable order for the combined proof.</param>
    /// <returns>The normalized direct-fusion proof, or null when the inputs conflict.</returns>
    private static PlayerFusionProofPlan? CombineMaterialPlans(PlayerFusionProofPlan body, PlayerFusionProofPlan head, IReadOnlyDictionary<string, int> resourceSupply, long order)
    {
        if (body.Constraints.Any(entry => head.Constraints.TryGetValue(entry.Key, out string? value) && value != entry.Value))
            return null;

        Dictionary<string, int> items = MergeCounts(body.Items, head.Items);
        if (!WithinSupply(items, resourceSupply))
            return null;

        Dictionary<string, int> sourceUses = MergeCounts(body.SourceUses, head.SourceUses);
        if (sourceUses.Values.Any(count => count > 1))
            return null;

        return new PlayerFusionProofPlan(items, new Dictionary<string, string>(StringComparer.Ordinal), new Dictionary<string, int>(StringComparer.Ordinal), checked(body.PathLength + head.PathLength + 1), order);
    }

    /// <summary>
    /// Performs the exact ascending-strength fusion-evolution closure in tracker memory.
    /// </summary>
    /// <param name="plansBySpecies">The seeded normal, caught-fusion, and direct-fusion proof states.</param>
    /// <param name="assignments">Every exact generated fusion-evolution assignment.</param>
    /// <param name="resourceSupply">The run-wide evolution-item supply.</param>
    /// <param name="nextPlanOrder">The next stable plan order.</param>
    /// <returns>Only source branches backed by an executable acquisition proof.</returns>
    private IReadOnlyList<FusionEvolutionSourceAssignment> CloseFusionEvolutions(Dictionary<int, List<PlayerFusionProofPlan>> plansBySpecies, IReadOnlyList<FusionEvolutionSourceAssignment> assignments, IReadOnlyDictionary<string, int> resourceSupply, long nextPlanOrder)
    {
        Dictionary<string, PlayerFusionEvolutionBranch> branchesByIdentity = _catalog.EvolutionBranches.ToDictionary(branch => branch.Identity, StringComparer.Ordinal);
        List<FusionEvolutionSourceAssignment> executable = [];
        foreach (FusionEvolutionSourceAssignment assignment in assignments.OrderBy(value => value.SourceBst).ThenBy(value => value.PackedSourceComponents))
        {
            int body = assignment.PackedSourceComponents >> 10;
            int head = assignment.PackedSourceComponents & 0x3FF;
            int sourceId = checked(body * _catalog.NormalSpeciesCount + head);
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

        return new PlayerFusionProofPlan(items, source.Constraints.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal), source.SourceUses.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal), checked(source.PathLength + 1), order);
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
    /// Determines whether one game-owned job matches the embedded generated catalog.
    /// </summary>
    private bool Compatible(PlayerFusionClosureWorkPayload work)
    {
        return work.GeneratorVersion == _catalog.PlayerFusionGeneratorVersion
            && work.SourceCatalogFingerprint == _sourceCatalog.Fingerprint
            && work.BaseStatSourceFingerprint == _catalog.BaseStatSourceFingerprint
            && work.CustomFusionPoolVersion == _catalog.CustomFusionPoolVersion
            && work.CustomFusionPoolSize == _catalog.CustomFusionPool.Count
            && work.CustomFusionPoolFingerprint == _catalog.CustomFusionPoolFingerprint
            && work.FusionEvolutionGeneratorVersion == _catalog.FusionEvolutionGeneratorVersion
            && work.FusionEvolutionRulesVersion == _catalog.FusionEvolutionRulesVersion
            && work.EvolutionSourceFingerprint == _catalog.EvolutionSourceFingerprint
            && work.EvolutionTaxonomyFingerprint == _catalog.EvolutionTaxonomyFingerprint
            && work.EvolutionMethodFingerprint == _catalog.EvolutionMethodFingerprint;
    }

    /// <summary>
    /// Determines whether one completed recipe matches the tracker-owned fusion-evolution catalog.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <returns>True when exact tracker-side assignments are safe to use.</returns>
    private bool Compatible(CompletedRunRecipePayload recipe)
    {
        EvolutionGeneratorRecipePayload? evolution = recipe.EvolutionGenerator;
        return evolution is not null
            && recipe.PlayerFusionGenerator.Version == _catalog.PlayerFusionGeneratorVersion
            && recipe.PlayerFusionGenerator.PoolSize == _catalog.CustomFusionPool.Count
            && recipe.PlayerFusionGenerator.PoolFingerprint == _catalog.CustomFusionPoolFingerprint
            && evolution.Fusion.Version == _catalog.FusionEvolutionGeneratorVersion
            && evolution.Fusion.RulesVersion == _catalog.FusionEvolutionRulesVersion
            && evolution.SourceFingerprint == _catalog.EvolutionSourceFingerprint
            && evolution.TaxonomyFingerprint == _catalog.EvolutionTaxonomyFingerprint
            && evolution.MethodFingerprint == _catalog.EvolutionMethodFingerprint
            && evolution.BaseStatGenerator.SourceFingerprint == _catalog.BaseStatSourceFingerprint
            && evolution.Fusion.TargetPool.Version == _catalog.CustomFusionPoolVersion
            && evolution.Fusion.TargetPool.Size == _catalog.CustomFusionPool.Count
            && evolution.Fusion.TargetPool.Fingerprint == _catalog.CustomFusionPoolFingerprint;
    }

    /// <summary>
    /// Determines whether one active-run assignment recipe matches the tracker-owned fusion-evolution catalog.
    /// </summary>
    /// <param name="recipe">The active-run deterministic assignment recipe.</param>
    /// <returns>True when exact tracker-side assignments are safe to prepare.</returns>
    private bool Compatible(FusionAssignmentRecipePayload recipe)
    {
        return recipe.PlayerFusionGeneratorVersion == _catalog.PlayerFusionGeneratorVersion
            && recipe.GeneratorVersion == _catalog.FusionEvolutionGeneratorVersion
            && recipe.RulesVersion == _catalog.FusionEvolutionRulesVersion
            && recipe.SourceFingerprint == _catalog.EvolutionSourceFingerprint
            && recipe.TaxonomyFingerprint == _catalog.EvolutionTaxonomyFingerprint
            && recipe.MethodFingerprint == _catalog.EvolutionMethodFingerprint
            && recipe.BaseStatSourceFingerprint == _catalog.BaseStatSourceFingerprint
            && recipe.TargetPoolVersion == _catalog.CustomFusionPoolVersion
            && recipe.TargetPoolSize == _catalog.CustomFusionPool.Count
            && recipe.TargetPoolFingerprint == _catalog.CustomFusionPoolFingerprint;
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
/// <param name="PackedAssignmentsByTarget">The packed ordered assignments keyed by numeric target identifier.</param>
internal sealed record PlayerFusionMaterialIndex(IReadOnlyDictionary<int, IReadOnlyList<uint>> PackedAssignmentsByTarget);

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
/// <param name="NextPlanOrder">The next stable proof order after direct fusion closure.</param>
internal sealed record PlayerFusionDirectProofResult(Dictionary<int, List<PlayerFusionProofPlan>> PlansBySpecies, long NextPlanOrder);

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
/// <param name="PackedExecutableEvolutionEdges">The sorted executable source-target edge index.</param>
/// <param name="ObtainableCount">The final normal and fusion species count.</param>
internal sealed record PlayerFusionWorkerResult(IReadOnlyList<uint> ObtainableFusionWords, byte[] PackedExecutableEvolutionEdges, int ObtainableCount);
