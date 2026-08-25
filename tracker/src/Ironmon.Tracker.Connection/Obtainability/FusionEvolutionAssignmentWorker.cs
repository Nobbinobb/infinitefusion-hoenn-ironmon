using System.Collections.Concurrent;
using System.Text;

namespace Ironmon.Tracker.Connection.Obtainability;

/// <summary>
/// Reproduces exact complete-fusion evolution assignments outside the Ruby game thread.
/// </summary>
internal sealed class FusionEvolutionAssignmentWorker
{
    private const ulong FnvOffsetBasis = 14_695_981_039_346_656_037;
    private const ulong FnvPrime = 1_099_511_628_211;
    private const int MaterialIdBits = 10;
    private const int MaterialIdMask = (1 << MaterialIdBits) - 1;
    private const int PreferredMinimumPercent = 90;
    private const int PreferredMaximumPercent = 115;
    private const int IntermediateWeightForIntermediateReference = 60;
    private const int IntermediateWeightForTerminalReference = 40;
    private const int MaximumCachedStates = 2;
    private readonly PlayerFusionMappingWorkerCatalog _catalog;
    private readonly ConcurrentDictionary<long, Lazy<WorkerState>> _states = new();

    /// <summary>
    /// Initializes the worker from the embedded deterministic catalog.
    /// </summary>
    internal FusionEvolutionAssignmentWorker()
        : this(PlayerFusionMappingWorkerCatalog.Load())
    {
    }

    /// <summary>
    /// Initializes the worker from one validated deterministic catalog.
    /// </summary>
    /// <param name="catalog">The generated worker catalog.</param>
    internal FusionEvolutionAssignmentWorker(PlayerFusionMappingWorkerCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>
    /// Generates exact assignments for the supplied custom-fusion sources in parallel.
    /// </summary>
    /// <param name="seed">The run seed.</param>
    /// <param name="packedSourceComponents">The distinct packed body and head identifiers.</param>
    /// <param name="cancellationToken">The token that cancels parallel work.</param>
    /// <returns>The assignments in ascending packed-source order.</returns>
    internal IReadOnlyList<FusionEvolutionSourceAssignment> GenerateAll(long seed, IReadOnlyList<int> packedSourceComponents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packedSourceComponents);
        WorkerState state = GetState(seed);
        int[] sources = [.. packedSourceComponents.Distinct().Order()];
        FusionEvolutionSourceAssignment[] result = new FusionEvolutionSourceAssignment[sources.Length];
        Parallel.For(0, sources.Length, new ParallelOptions { CancellationToken = cancellationToken }, index =>
        {
            result[index] = GenerateSource(state, sources[index]);
        });

        return result;
    }

    /// <summary>
    /// Generates one exact assignment for verification or bounded follow-up work.
    /// </summary>
    /// <param name="seed">The run seed.</param>
    /// <param name="packedSourceComponents">The packed body and head identifiers.</param>
    /// <returns>The exact source assignment.</returns>
    internal FusionEvolutionSourceAssignment Generate(long seed, int packedSourceComponents)
        => GenerateSource(GetState(seed), packedSourceComponents);

    /// <summary>
    /// Returns every valid candidate for one displayed fusion component without solving assignments.
    /// </summary>
    /// <param name="seed">The run seed.</param>
    /// <param name="packedSourceComponents">The packed body and head identifiers.</param>
    /// <param name="side">The displayed component whose branches supply candidates.</param>
    /// <returns>The distinct valid candidates ordered by stable fusion identity.</returns>
    internal IReadOnlyList<FusionEvolutionCandidateTarget> GetCandidateTargets(long seed, int packedSourceComponents, FusionComponentSide side)
    {
        WorkerState state = GetState(seed);
        int bodyId = packedSourceComponents >> MaterialIdBits;
        int headId = packedSourceComponents & MaterialIdMask;
        if (bodyId <= 0 || bodyId >= state.Normal.Count || headId <= 0 || headId >= state.Normal.Count)
            throw new ArgumentOutOfRangeException(nameof(packedSourceComponents), "The fusion source components are outside the normal catalog.");

        string sourceIdentity = $"B{bodyId}H{headId}";
        List<ConceptualBranch> branches = [];
        int componentId = side == FusionComponentSide.Body ? bodyId : headId;
        AddConceptualBranches(state, branches, sourceIdentity, componentId, side);
        if (branches.Count == 0)
            return [];

        SourceContext source = new(sourceIdentity, bodyId, headId, FusedBst(state.Stats[bodyId], state.Stats[headId]), state.Normal[bodyId].FamilyId, state.Normal[headId].FamilyId, branches.Count);
        Dictionary<int, TargetData> candidates = [];
        foreach (ConceptualBranch branch in branches)
        {
            BranchContext context = CreateBranchContext(state, source, branch);
            IReadOnlyList<TargetData> hard = HardCandidates(state, source, branch);
            TargetData[] selected = [.. hard.Where(target => target.Bst >= context.Minimum && target.Bst <= context.Maximum)];
            if (selected.Length == 0 && hard.Count > 0)
            {
                int distance = hard.Min(target => Math.Abs(target.Bst - context.ReferenceBst));
                selected = [.. hard.Where(target => Math.Abs(target.Bst - context.ReferenceBst) == distance)];
            }

            foreach (TargetData target in selected)
                candidates[target.Id] = target;
        }

        return [.. candidates.Values.OrderBy(target => target.Identity, StringComparer.Ordinal).Select(target => new FusionEvolutionCandidateTarget(target.Id, target.Bst))];
    }

    /// <summary>
    /// Gets or creates one immutable seed-specific worker state shared by assignments and candidate lists.
    /// </summary>
    /// <param name="seed">The run seed.</param>
    /// <returns>The shared worker state.</returns>
    private WorkerState GetState(long seed)
    {
        Lazy<WorkerState> state = _states.GetOrAdd(seed, value => new Lazy<WorkerState>(() => BuildState(value, CancellationToken.None), LazyThreadSafetyMode.ExecutionAndPublication));
        TrimStates(seed);
        return state.Value;
    }

    /// <summary>
    /// Removes old prepared states beyond the active and archive reuse window.
    /// </summary>
    /// <param name="retainedSeed">The seed required by the current caller.</param>
    private void TrimStates(long retainedSeed)
    {
        if (_states.Count <= MaximumCachedStates)
            return;

        foreach ((long seed, Lazy<WorkerState> state) in _states)
        {
            if (_states.Count <= MaximumCachedStates)
                break;

            if (seed != retainedSeed && state.IsValueCreated)
                _states.TryRemove(seed, out _);
        }
    }

    /// <summary>
    /// Builds the immutable seed-specific target indexes.
    /// </summary>
    private WorkerState BuildState(long seed, CancellationToken cancellationToken)
    {
        PlayerFusionNormalSpecies[] normal = new PlayerFusionNormalSpecies[_catalog.NormalSpeciesCount + 1];
        int[][] stats = new int[_catalog.NormalSpeciesCount + 1][];
        foreach (PlayerFusionNormalSpecies species in _catalog.NormalSpecies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            normal[species.Id] = species;
            stats[species.Id] = DistributeStats(seed, species);
        }

        int normalType = _catalog.TypeNames.Select((name, index) => (name, index)).Single(entry => entry.name == "NORMAL").index;
        int flyingType = _catalog.TypeNames.Select((name, index) => (name, index)).Single(entry => entry.name == "FLYING").index;
        Dictionary<int, int> bodyRanks = _catalog.NormalSpecies
            .OrderBy(species => $"{species.Id}H", StringComparer.Ordinal)
            .Select((species, index) => (species.Id, index))
            .ToDictionary(entry => entry.Id, entry => entry.index);

        Dictionary<int, int> headRanks = _catalog.NormalSpecies
            .OrderBy(species => species.Id.ToString(), StringComparer.Ordinal)
            .Select((species, index) => (species.Id, index))
            .ToDictionary(entry => entry.Id, entry => entry.index);

        List<TargetData> targets = [];
        foreach (PlayerFusionTargetSpecies source in _catalog.CustomFusionPool)
        {
            int bodyId = source.PackedComponents >> MaterialIdBits;
            int headId = source.PackedComponents & MaterialIdMask;
            TargetBucket bucket = TargetBucketFor(normal[bodyId].Role, normal[headId].Role);
            if (bucket == TargetBucket.None)
                continue;

            ulong types = FusionTypeMask(normal[bodyId], normal[headId], normalType, flyingType);
            targets.Add(new TargetData(
                checked(bodyId * _catalog.NormalSpeciesCount + headId), source.PackedComponents, bodyId, headId,
                $"B{bodyId}H{headId}", FusedBst(stats[bodyId], stats[headId]), types,
                normal[bodyId].FamilyId, normal[headId].FamilyId, bucket,
                checked(bodyRanks[bodyId] * _catalog.NormalSpeciesCount + headRanks[headId])));
        }

        Dictionary<int, PlayerFusionEvolutionBranch[]> branchesBySource = _catalog.EvolutionBranches
            .GroupBy(branch => branch.SourceId)
            .ToDictionary(group => group.Key, group => group.OrderBy(branch => branch.Identity, StringComparer.Ordinal).ToArray());

        ulong deterministicBase = HashEntries(FnvOffsetBasis, [
            _catalog.FusionEvolutionGeneratorVersion, _catalog.FusionEvolutionRulesVersion, seed, "fusion_evolution",
            _catalog.EvolutionSourceFingerprint, _catalog.EvolutionTaxonomyFingerprint, _catalog.EvolutionMethodFingerprint,
            _catalog.CustomFusionPoolVersion, _catalog.CustomFusionPool.Count, _catalog.CustomFusionPoolFingerprint,
            1, _catalog.BaseStatSourceFingerprint
        ]);

        TargetData[] continuing = [.. targets.Where(target => target.Bucket == TargetBucket.Continuing).OrderBy(target => target.LexicalRank)];
        TargetData[] terminal = [.. targets.Where(target => target.Bucket == TargetBucket.Terminal).OrderBy(target => target.LexicalRank)];
        Dictionary<(TargetBucket Bucket, ulong Types), IReadOnlyList<TargetData>> targetsByTypes = [];
        foreach (ulong requiredTypes in _catalog.EvolutionBranches.Select(branch => branch.RequiredTypeMask).Distinct())
        {
            targetsByTypes[(TargetBucket.Continuing, requiredTypes)] = [.. continuing.Where(target => (target.TypeMask & requiredTypes) != 0)];
            targetsByTypes[(TargetBucket.Terminal, requiredTypes)] = [.. terminal.Where(target => (target.TypeMask & requiredTypes) != 0)];
        }

        return new WorkerState(seed, deterministicBase, normal, stats, branchesBySource, continuing, terminal, targetsByTypes);
    }

    /// <summary>
    /// Generates all conceptual branches for one source and solves their distinct targets.
    /// </summary>
    private FusionEvolutionSourceAssignment GenerateSource(WorkerState state, int packedSourceComponents)
    {
        int bodyId = packedSourceComponents >> MaterialIdBits;
        int headId = packedSourceComponents & MaterialIdMask;
        if (bodyId <= 0 || bodyId >= state.Normal.Count || headId <= 0 || headId >= state.Normal.Count)
            throw new ArgumentOutOfRangeException(nameof(packedSourceComponents), "The fusion source components are outside the normal catalog.");

        string sourceIdentity = $"B{bodyId}H{headId}";
        List<ConceptualBranch> branches = [];
        AddConceptualBranches(state, branches, sourceIdentity, bodyId, FusionComponentSide.Body);
        AddConceptualBranches(state, branches, sourceIdentity, headId, FusionComponentSide.Head);
        if (branches.Count == 0)
            return new FusionEvolutionSourceAssignment(packedSourceComponents, FusedBst(state.Stats[bodyId], state.Stats[headId]), []);

        SourceContext context = new(
            sourceIdentity, bodyId, headId, FusedBst(state.Stats[bodyId], state.Stats[headId]),
            state.Normal[bodyId].FamilyId, state.Normal[headId].FamilyId, branches.Count);

        Dictionary<string, IReadOnlyList<CandidatePlan>> plans = branches.ToDictionary(branch => branch.Identity, branch => CandidatePlans(state, context, branch), StringComparer.Ordinal);
        Dictionary<string, TargetData>? assignment = FindPlanAssignment(branches, plans);
        assignment ??= FindUpwardExpansionAssignment(state, context, branches);
        if (assignment is null)
            throw new InvalidOperationException($"{sourceIdentity} has no complete valid fusion evolution assignment.");

        FusionEvolutionAssignedBranch[] assigned = [.. branches.Select(branch => new FusionEvolutionAssignedBranch(branch.Side, branch.ComponentBranch.Identity, assignment[branch.Identity].Id, assignment[branch.Identity].Bst))];
        return new FusionEvolutionSourceAssignment(packedSourceComponents, context.Bst, assigned);
    }

    /// <summary>
    /// Adds one component side's conceptual branches in stable catalog order.
    /// </summary>
    private static void AddConceptualBranches(WorkerState state, List<ConceptualBranch> result, string sourceIdentity, int componentId, FusionComponentSide side)
    {
        if (!state.BranchesBySource.TryGetValue(componentId, out PlayerFusionEvolutionBranch[]? branches))
            return;

        string sideName = side == FusionComponentSide.Body ? "body" : "head";
        foreach (PlayerFusionEvolutionBranch branch in branches)
            result.Add(new ConceptualBranch($"{sourceIdentity}|{sideName}|{branch.Identity}", side, componentId, branch));
    }

    /// <summary>
    /// Builds the ordered standard plan alternatives for one conceptual branch.
    /// </summary>
    private static IReadOnlyList<CandidatePlan> CandidatePlans(WorkerState state, SourceContext source, ConceptualBranch branch)
    {
        BranchContext context = CreateBranchContext(state, source, branch);
        List<(TargetBucket Bucket, CandidatePlan Plan)> preferred = [];
        foreach (TargetBucket bucket in AllowedBuckets(state.Normal[branch.ComponentId].Role))
        {
            IReadOnlyList<TargetData> candidates = DeterministicPrefix(
                state, TargetsForTypes(state, bucket, branch.ComponentBranch.RequiredTypeMask),
                CandidatePriority(state, branch, bucket), source.BranchCount,
                target => !SharesFamily(source, target) && target.Bst >= context.Minimum && target.Bst <= context.Maximum);
            if (candidates.Count > 0)
                preferred.Add((bucket, new CandidatePlan(bucket, candidates)));
        }

        if (preferred.Count == 0)
        {
            IReadOnlyList<TargetData> hard = HardCandidates(state, source, branch);
            if (hard.Count == 0)
                throw new InvalidOperationException($"{branch.Identity} has no valid stronger custom target.");

            int distance = hard.Min(target => Math.Abs(target.Bst - context.ReferenceBst));
            TargetData[] closest = [.. hard.Where(target => Math.Abs(target.Bst - context.ReferenceBst) == distance)];
            return [new CandidatePlan(TargetBucket.Fallback, DeterministicPrefix(state, closest, CandidatePriority(state, branch, TargetBucket.Fallback), source.BranchCount, _ => true))];
        }

        TargetBucket[] ordered = OrderedBuckets(state, branch.ComponentBranch, [.. preferred.Select(entry => entry.Bucket)]);
        return [.. ordered.Select(bucket => preferred.Single(entry => entry.Bucket == bucket).Plan)];
    }

    /// <summary>
    /// Creates the preferred strength interval and natural one-component reference.
    /// </summary>
    private static BranchContext CreateBranchContext(WorkerState state, SourceContext source, ConceptualBranch branch)
    {
        int body = branch.Side == FusionComponentSide.Body ? branch.ComponentBranch.DestinationId : source.BodyId;
        int head = branch.Side == FusionComponentSide.Head ? branch.ComponentBranch.DestinationId : source.HeadId;
        int reference = FusedBst(state.Stats[body], state.Stats[head]);
        int minimum = Math.Max(DivideRoundUp(reference * PreferredMinimumPercent, 100), source.Bst + 1);
        int maximum = reference * PreferredMaximumPercent / 100;
        return new BranchContext(reference, minimum, maximum);
    }

    /// <summary>
    /// Returns every hard-valid target ordered by generated BST and identity.
    /// </summary>
    private static IReadOnlyList<TargetData> HardCandidates(WorkerState state, SourceContext source, ConceptualBranch branch)
    {
        return [.. AllowedBuckets(state.Normal[branch.ComponentId].Role)
            .SelectMany(bucket => TargetsForTypes(state, bucket, branch.ComponentBranch.RequiredTypeMask))
            .DistinctBy(target => target.Id)
            .Where(target => !SharesFamily(source, target) && target.Bst > source.Bst)
            .OrderBy(target => target.Bst)
            .ThenBy(target => target.Identity, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Finds the first standard plan combination with distinct targets.
    /// </summary>
    private static Dictionary<string, TargetData>? FindPlanAssignment(IReadOnlyList<ConceptualBranch> branches, IReadOnlyDictionary<string, IReadOnlyList<CandidatePlan>> plans)
    {
        Dictionary<string, CandidatePlan> selected = new(StringComparer.Ordinal);
        return SelectPlans(branches, plans, selected, 0);
    }

    /// <summary>
    /// Recursively selects ordered plan alternatives before solving target uniqueness.
    /// </summary>
    private static Dictionary<string, TargetData>? SelectPlans(IReadOnlyList<ConceptualBranch> branches, IReadOnlyDictionary<string, IReadOnlyList<CandidatePlan>> plans, Dictionary<string, CandidatePlan> selected, int index)
    {
        if (index >= branches.Count)
            return AssignDistinctTargets(branches, selected, [], new HashSet<int>());

        ConceptualBranch branch = branches[index];
        foreach (CandidatePlan plan in plans[branch.Identity])
        {
            selected[branch.Identity] = plan;
            Dictionary<string, TargetData>? result = SelectPlans(branches, plans, selected, index + 1);
            if (result is not null)
                return result;
        }
        selected.Remove(branch.Identity);
        return null;
    }

    /// <summary>
    /// Assigns distinct targets using the smallest remaining candidate set first.
    /// </summary>
    private static Dictionary<string, TargetData>? AssignDistinctTargets(IReadOnlyList<ConceptualBranch> branches, IReadOnlyDictionary<string, CandidatePlan> selected, Dictionary<string, TargetData> assigned, HashSet<int> usedTargets)
    {
        if (assigned.Count >= branches.Count)
            return new Dictionary<string, TargetData>(assigned, StringComparer.Ordinal);

        (ConceptualBranch Branch, TargetData[] Candidates) choice = branches
            .Where(branch => !assigned.ContainsKey(branch.Identity))
            .Select(branch => (Branch: branch, Candidates: selected[branch.Identity].Candidates.Where(target => !usedTargets.Contains(target.Id)).ToArray()))
            .OrderBy(entry => entry.Candidates.Length)
            .ThenBy(entry => entry.Branch.Identity, StringComparer.Ordinal)
            .First();

        foreach (TargetData target in choice.Candidates)
        {
            assigned[choice.Branch.Identity] = target;
            usedTargets.Add(target.Id);
            Dictionary<string, TargetData>? result = AssignDistinctTargets(branches, selected, assigned, usedTargets);
            if (result is not null)
                return result;

            assigned.Remove(choice.Branch.Identity);
            usedTargets.Remove(target.Id);
        }

        return null;
    }

    /// <summary>
    /// Applies the deterministic upward-only assignment rescue when standard plans collide.
    /// </summary>
    private static Dictionary<string, TargetData>? FindUpwardExpansionAssignment(WorkerState state, SourceContext source, IReadOnlyList<ConceptualBranch> branches)
    {
        Dictionary<string, UpwardPlan> plans = branches.ToDictionary(
            branch => branch.Identity, branch => CreateUpwardPlan(state, source, branch), StringComparer.Ordinal);
        for (int expansionCount = 0; expansionCount <= branches.Count; expansionCount++)
        {
            List<ConceptualBranch[]> subsets = [.. Combinations(branches, expansionCount)];
            subsets.Sort((left, right) => CompareExpansionSubsets(left, right, plans));
            foreach (ConceptualBranch[] subset in subsets)
            {
                HashSet<string> expanded = [.. subset.Select(branch => branch.Identity)];
                Dictionary<string, IReadOnlyList<CandidatePlan>> filtered = new(StringComparer.Ordinal);
                bool valid = true;
                foreach (ConceptualBranch branch in branches)
                {
                    TargetData[] candidates = [.. plans[branch.Identity].Candidates.Where(candidate => !candidate.Upward || expanded.Contains(branch.Identity)).Select(candidate => candidate.Target)];
                    if (candidates.Length == 0)
                    {
                        valid = false;
                        break;
                    }

                    filtered[branch.Identity] = [new CandidatePlan(TargetBucket.UpwardExpansion, candidates)];
                }
                if (!valid)
                    continue;

                Dictionary<string, TargetData>? assignment = FindPlanAssignment(branches, filtered);
                if (assignment is not null)
                    return assignment;
            }
        }
        return null;
    }

    /// <summary>
    /// Builds standard and upward candidates for one rescue plan.
    /// </summary>
    private static UpwardPlan CreateUpwardPlan(WorkerState state, SourceContext source, ConceptualBranch branch)
    {
        BranchContext context = CreateBranchContext(state, source, branch);
        IReadOnlyList<TargetData> hard = HardCandidates(state, source, branch);
        TargetData[] preferred = [.. hard.Where(target => target.Bst >= context.Minimum && target.Bst <= context.Maximum)];
        List<TargetData> standard = [];
        if (preferred.Length == 0)
        {
            int distance = hard.Min(target => Math.Abs(target.Bst - context.ReferenceBst));
            TargetData[] closest = [.. hard.Where(target => Math.Abs(target.Bst - context.ReferenceBst) == distance)];
            standard.AddRange(DeterministicPrefix(state, closest, CandidatePriority(state, branch, TargetBucket.Fallback), closest.Length, _ => true));
        }
        else
        {
            TargetBucket[] available = [.. AllowedBuckets(state.Normal[branch.ComponentId].Role).Where(bucket => preferred.Any(target => target.Bucket == bucket))];
            foreach (TargetBucket bucket in OrderedBuckets(state, branch.ComponentBranch, available))
            {
                TargetData[] candidates = [.. preferred.Where(target => target.Bucket == bucket)];
                standard.AddRange(DeterministicPrefix(state, candidates, CandidatePriority(state, branch, bucket), candidates.Length, _ => true));
            }
        }

        int floor = Math.Max(context.Maximum, standard.Count == 0 ? context.Maximum : standard.Max(target => target.Bst));
        TargetData[] upward = [.. hard.Where(target => target.Bst > floor)
            .OrderBy(target => target.Bst)
            .ThenBy(target => DeterministicValue(state, "upward_expansion", branch.Identity, target.Identity))
            .ThenBy(target => target.Identity, StringComparer.Ordinal)];

        List<UpwardCandidate> combined = [.. standard.DistinctBy(target => target.Id).Select(target => new UpwardCandidate(target, false))];
        combined.AddRange(upward.Where(target => combined.All(existing => existing.Target.Id != target.Id)).Select(target => new UpwardCandidate(target, true)));
        return new UpwardPlan(combined, floor);
    }

    /// <summary>
    /// Enumerates fixed-size branch subsets in source order.
    /// </summary>
    private static IEnumerable<ConceptualBranch[]> Combinations(IReadOnlyList<ConceptualBranch> branches, int count)
    {
        if (count == 0)
        {
            yield return [];
            yield break;
        }

        int[] indexes = [.. Enumerable.Range(0, count)];
        while (indexes[0] <= branches.Count - count)
        {
            yield return [.. indexes.Select(index => branches[index])];
            int position = count - 1;
            while (position >= 0 && indexes[position] == branches.Count - count + position)
                position--;

            if (position < 0)
                yield break;

            indexes[position]++;
            for (int next = position + 1; next < count; next++)
                indexes[next] = indexes[next - 1] + 1;
        }
    }

    /// <summary>
    /// Orders rescue subsets by minimum upward distance and branch identities.
    /// </summary>
    private static int CompareExpansionSubsets(IReadOnlyList<ConceptualBranch> left, IReadOnlyList<ConceptualBranch> right, IReadOnlyDictionary<string, UpwardPlan> plans)
    {
        ulong leftDistance = ExpansionDistance(left, plans);
        ulong rightDistance = ExpansionDistance(right, plans);
        int distanceComparison = leftDistance.CompareTo(rightDistance);
        if (distanceComparison != 0)
            return distanceComparison;

        for (int index = 0; index < left.Count; index++)
        {
            int identityComparison = string.CompareOrdinal(left[index].Identity, right[index].Identity);
            if (identityComparison != 0)
                return identityComparison;
        }
        return 0;
    }

    /// <summary>
    /// Calculates one rescue subset's total minimum upward distance.
    /// </summary>
    private static ulong ExpansionDistance(IReadOnlyList<ConceptualBranch> subset, IReadOnlyDictionary<string, UpwardPlan> plans)
    {
        ulong result = 0;
        foreach (ConceptualBranch branch in subset)
        {
            UpwardPlan plan = plans[branch.Identity];
            int? distance = plan.Candidates.Where(candidate => candidate.Upward).Select(candidate => candidate.Target.Bst - plan.Floor).Cast<int?>().Min();
            result = unchecked(result + (ulong)(distance ?? -1));
        }
        return result;
    }

    /// <summary>
    /// Selects a bounded deterministic prefix without materializing a full shuffle.
    /// </summary>
    private static IReadOnlyList<TargetData> DeterministicPrefix(WorkerState state, IReadOnlyList<TargetData> targets, ulong priority, int limit, Func<TargetData, bool> accepted)
    {
        if (limit <= 0 || targets.Count == 0)
            return [];

        Dictionary<int, int> swaps = [];
        List<TargetData> result = [];
        ulong shuffleState = priority;
        int position = 0;
        while (position < targets.Count && result.Count < limit)
        {
            (int offset, ulong nextState) = DeterministicBoundedValue(shuffleState, targets.Count - position);
            shuffleState = nextState;
            int selectedPosition = position + offset;
            int originalPosition = swaps.GetValueOrDefault(selectedPosition, selectedPosition);
            int currentPosition = swaps.GetValueOrDefault(position, position);
            if (selectedPosition != position)
                swaps[selectedPosition] = currentPosition;

            swaps.Remove(position);
            TargetData target = targets[originalPosition];
            position++;
            if (accepted(target))
                result.Add(target);
        }
        return result;
    }

    /// <summary>
    /// Returns targets in one bucket sharing any required branch type, in lexical order.
    /// </summary>
    private static IReadOnlyList<TargetData> TargetsForTypes(WorkerState state, TargetBucket bucket, ulong requiredTypes)
    {
        return state.TargetsByTypes[(bucket, requiredTypes)];
    }

    /// <summary>
    /// Orders preferred buckets using the branch's deterministic native-destination bias.
    /// </summary>
    private static TargetBucket[] OrderedBuckets(WorkerState state, PlayerFusionEvolutionBranch branch, TargetBucket[] available)
    {
        if (available.Length <= 1)
            return available;

        int weight = branch.DestinationRole == PlayerFusionEvolutionRole.Intermediate
            ? IntermediateWeightForIntermediateReference
            : IntermediateWeightForTerminalReference;

        TargetBucket selected = DeterministicValue(state, "bucket", branch.Identity) % 100 < (ulong)weight
            ? TargetBucket.Continuing
            : TargetBucket.Terminal;

        if (!available.Contains(selected))
            selected = available[0];
        return [selected, .. available.Where(bucket => bucket != selected)];
    }

    /// <summary>
    /// Gets the buckets permitted by one evolving component role.
    /// </summary>
    private static TargetBucket[] AllowedBuckets(PlayerFusionEvolutionRole role)
    {
        return role switch
        {
            PlayerFusionEvolutionRole.Intermediate => [TargetBucket.Terminal],
            PlayerFusionEvolutionRole.FirstStage => [TargetBucket.Continuing, TargetBucket.Terminal],
            _ => throw new InvalidOperationException($"{role} fusion component cannot evolve.")
        };
    }

    /// <summary>
    /// Gets one custom target's structural bucket.
    /// </summary>
    private static TargetBucket TargetBucketFor(PlayerFusionEvolutionRole body, PlayerFusionEvolutionRole head)
    {
        if (body == PlayerFusionEvolutionRole.FirstStage || head == PlayerFusionEvolutionRole.FirstStage)
            return TargetBucket.None;

        if (body == PlayerFusionEvolutionRole.Intermediate || head == PlayerFusionEvolutionRole.Intermediate)
            return TargetBucket.Continuing;

        return body is PlayerFusionEvolutionRole.Final or PlayerFusionEvolutionRole.Standalone && head is PlayerFusionEvolutionRole.Final or PlayerFusionEvolutionRole.Standalone
            ? TargetBucket.Terminal
            : TargetBucket.None;
    }

    /// <summary>
    /// Calculates Infinite Fusion's ordered displayed type mask.
    /// </summary>
    private static ulong FusionTypeMask(PlayerFusionNormalSpecies body, PlayerFusionNormalSpecies head, int normalType, int flyingType)
    {
        int first = head.PrimaryType == normalType && head.SecondaryType == flyingType ? head.SecondaryType : head.PrimaryType;
        int second = body.SecondaryType == first ? body.PrimaryType : body.SecondaryType;
        ulong result = 1UL << first;
        if (second >= 0)
            result |= 1UL << second;

        return result;
    }

    /// <summary>
    /// Tests whether one target shares either excluded source family.
    /// </summary>
    private static bool SharesFamily(SourceContext source, TargetData target)
        => target.BodyFamily == source.BodyFamily || target.BodyFamily == source.HeadFamily || target.HeadFamily == source.BodyFamily || target.HeadFamily == source.HeadFamily;

    /// <summary>
    /// Computes one branch and bucket's deterministic candidate state.
    /// </summary>
    private static ulong CandidatePriority(WorkerState state, ConceptualBranch branch, TargetBucket bucket)
        => DeterministicValue(state, "candidate", branch.Identity, BucketName(bucket));

    /// <summary>
    /// Gets the Ruby symbol spelling used by deterministic bucket hashing.
    /// </summary>
    private static string BucketName(TargetBucket bucket)
    {
        return bucket switch
        {
            TargetBucket.Continuing => "continuing",
            TargetBucket.Terminal => "terminal",
            TargetBucket.Fallback => "fallback",
            TargetBucket.UpwardExpansion => "upward_expansion",
            _ => throw new ArgumentOutOfRangeException(nameof(bucket))
        };
    }

    /// <summary>
    /// Computes a deterministic value from the evolution generator's seeded base.
    /// </summary>
    private static ulong DeterministicValue(WorkerState state, params object[] parts)
        => HashEntries(state.DeterministicBase, parts);

    /// <summary>
    /// Selects an unbiased bounded SplitMix64 value.
    /// </summary>
    private static (int Value, ulong State) DeterministicBoundedValue(ulong state, int bound)
    {
        ulong rejectionThreshold = unchecked(0UL - (ulong)bound) % (ulong)bound;
        while (true)
        {
            (state, ulong value) = NextShuffleValue(state);
            if (value >= rejectionThreshold)
                return ((int)(value % (ulong)bound), state);
        }
    }

    /// <summary>
    /// Advances the fusion generator's SplitMix64 shuffle stream.
    /// </summary>
    private static (ulong State, ulong Value) NextShuffleValue(ulong state)
    {
        state = unchecked(state + 0x9E3779B97F4A7C15);
        ulong value = state;
        value = unchecked((value ^ value >> 30) * 0xBF58476D1CE4E5B9);
        value = unchecked((value ^ value >> 27) * 0x94D049BB133111EB);
        value ^= value >> 31;
        return (state, value);
    }

    /// <summary>
    /// Reproduces the generated normal-species base-stat vector.
    /// </summary>
    private static int[] DistributeStats(long seed, PlayerFusionNormalSpecies species)
    {
        const int minimumStat = 5;
        const int maximumStat = 255;
        const int weightRange = 1_000_000;
        string[] statNames = ["HP", "ATTACK", "DEFENSE", "SPECIAL_ATTACK", "SPECIAL_DEFENSE", "SPEED"];
        int[] values = [.. Enumerable.Repeat(minimumStat, 6)];
        int[] capacities = [.. Enumerable.Repeat(maximumStat - minimumStat, 6)];
        ulong[] weights = [.. statNames.Select(stat => 1UL + Fnv1a(string.Join('|', 1, seed, "base_stats", species.Identity, stat, "weight")) % weightRange)];
        int remaining = species.Stats.Sum() - minimumStat * 6;
        while (remaining > 0)
        {
            int[] active = [.. Enumerable.Range(0, 6).Where(index => capacities[index] > 0)];
            ulong totalWeight = active.Aggregate(0UL, (sum, index) => sum + weights[index]);
            int roundTotal = remaining;
            List<(ulong Remainder, int Index)> remainders = [];
            int distributed = 0;
            foreach (int index in active)
            {
                ulong numerator = (ulong)roundTotal * weights[index];
                int allocation = Math.Min((int)(numerator / totalWeight), capacities[index]);
                values[index] += allocation;
                capacities[index] -= allocation;
                remaining -= allocation;
                distributed += allocation;
                remainders.Add((numerator % totalWeight, index));
            }

            foreach ((ulong _, int index) in remainders.OrderByDescending(value => value.Remainder).ThenBy(value => value.Index))
            {
                if (remaining <= 0)
                    break;

                if (capacities[index] <= 0)
                    continue;

                values[index]++;
                capacities[index]--;
                remaining--;
                distributed++;
            }

            if (distributed <= 0)
                throw new InvalidOperationException($"Base-stat distribution made no progress for {species.Identity}.");
        }

        return values;
    }

    /// <summary>
    /// Calculates an oriented fusion BST from generated component stats.
    /// </summary>
    private static int FusedBst(IReadOnlyList<int> body, IReadOnlyList<int> head)
        => 2 * head[0] / 3 + body[0] / 3 + 2 * body[1] / 3 + head[1] / 3 + 2 * body[2] / 3 + head[2] / 3 + 2 * head[3] / 3 + body[3] / 3 + 2 * head[4] / 3 + body[4] / 3 + 2 * body[5] / 3 + head[5] / 3;

    /// <summary>
    /// Divides positive integers while rounding upward.
    /// </summary>
    private static int DivideRoundUp(int value, int divisor)
        => (value + divisor - 1) / divisor;

    /// <summary>
    /// Reproduces Ruby's entry-delimited FNV-1a accumulation.
    /// </summary>
    private static ulong HashEntries(ulong initialValue, IEnumerable<object> entries)
    {
        ulong value = initialValue;
        foreach (object entry in entries)
        {
            value = Fnv1a(entry.ToString() ?? string.Empty, value);
            value = unchecked(value * FnvPrime);
        }

        return value;
    }

    /// <summary>
    /// Computes UTF-8 FNV-1a from one initial state.
    /// </summary>
    private static ulong Fnv1a(string input, ulong initialValue = FnvOffsetBasis)
    {
        ulong value = initialValue;
        foreach (byte item in Encoding.UTF8.GetBytes(input))
        {
            value ^= item;
            value = unchecked(value * FnvPrime);
        }

        return value;
    }

    /// <summary>
    /// Identifies one target-pool or fallback plan category.
    /// </summary>
    private enum TargetBucket
    {
        /// <summary>
        /// Not a valid target.
        /// </summary>
        None = 0,

        /// <summary>
        /// Contains at least one intermediate component.
        /// </summary>
        Continuing = 1,

        /// <summary>
        /// Contains only final or standalone components.
        /// </summary>
        Terminal = 2,

        /// <summary>
        /// Uses the closest hard-valid target.
        /// </summary>
        Fallback = 3,

        /// <summary>
        /// Uses the upward-only collision rescue.
        /// </summary>
        UpwardExpansion = 4
    }

    /// <summary>
    /// Stores immutable seed-specific indexes shared by source assignments.
    /// </summary>
    /// <param name="Seed">The run seed.</param>
    /// <param name="DeterministicBase">The seeded deterministic hash base.</param>
    /// <param name="Normal">The normal-species catalog indexed by numeric identifier.</param>
    /// <param name="Stats">The generated stat vectors indexed by numeric identifier.</param>
    /// <param name="BranchesBySource">The conceptual branches grouped by normal source identifier.</param>
    /// <param name="ContinuingTargets">The lexically ordered continuing targets.</param>
    /// <param name="TerminalTargets">The lexically ordered terminal targets.</param>
    /// <param name="TargetsByTypes">The target pools grouped by bucket and required types.</param>
    private sealed record WorkerState(long Seed, ulong DeterministicBase, IReadOnlyList<PlayerFusionNormalSpecies> Normal, IReadOnlyList<int[]> Stats, IReadOnlyDictionary<int, PlayerFusionEvolutionBranch[]> BranchesBySource, IReadOnlyList<TargetData> ContinuingTargets, IReadOnlyList<TargetData> TerminalTargets, IReadOnlyDictionary<(TargetBucket Bucket, ulong Types), IReadOnlyList<TargetData>> TargetsByTypes);

    /// <summary>
    /// Stores one generated custom-fusion target and its selection metadata.
    /// </summary>
    /// <param name="Id">The numeric fusion identifier.</param>
    /// <param name="PackedComponents">The packed body and head identifiers.</param>
    /// <param name="BodyId">The body identifier.</param>
    /// <param name="HeadId">The head identifier.</param>
    /// <param name="Identity">The stable fusion identity.</param>
    /// <param name="Bst">The generated base-stat total.</param>
    /// <param name="TypeMask">The fusion type mask.</param>
    /// <param name="BodyFamily">The body evolution-family identifier.</param>
    /// <param name="HeadFamily">The head evolution-family identifier.</param>
    /// <param name="Bucket">The structural target bucket.</param>
    /// <param name="LexicalRank">The stable lexical rank.</param>
    private sealed record TargetData(int Id, int PackedComponents, int BodyId, int HeadId, string Identity, int Bst, ulong TypeMask, int BodyFamily, int HeadFamily, TargetBucket Bucket, int LexicalRank);

    /// <summary>
    /// Stores source-level values shared by conceptual branches.
    /// </summary>
    /// <param name="Identity">The stable source identity.</param>
    /// <param name="BodyId">The source body identifier.</param>
    /// <param name="HeadId">The source head identifier.</param>
    /// <param name="Bst">The source generated base-stat total.</param>
    /// <param name="BodyFamily">The source body evolution-family identifier.</param>
    /// <param name="HeadFamily">The source head evolution-family identifier.</param>
    /// <param name="BranchCount">The number of conceptual branches requiring distinct targets.</param>
    private sealed record SourceContext(string Identity, int BodyId, int HeadId, int Bst, int BodyFamily, int HeadFamily, int BranchCount);

    /// <summary>
    /// Stores one side-specific conceptual evolution branch.
    /// </summary>
    /// <param name="Identity">The stable conceptual branch identity.</param>
    /// <param name="Side">The evolving fusion component.</param>
    /// <param name="ComponentId">The evolving normal-species identifier.</param>
    /// <param name="ComponentBranch">The underlying normal evolution branch.</param>
    private sealed record ConceptualBranch(string Identity, FusionComponentSide Side, int ComponentId, PlayerFusionEvolutionBranch ComponentBranch);

    /// <summary>
    /// Stores one branch's natural reference and preferred strength interval.
    /// </summary>
    /// <param name="ReferenceBst">The natural one-component destination strength.</param>
    /// <param name="Minimum">The inclusive preferred minimum strength.</param>
    /// <param name="Maximum">The inclusive preferred maximum strength.</param>
    private sealed record BranchContext(int ReferenceBst, int Minimum, int Maximum);

    /// <summary>
    /// Stores one ordered target-plan alternative.
    /// </summary>
    /// <param name="Bucket">The target selection bucket.</param>
    /// <param name="Candidates">The deterministic candidate order.</param>
    private sealed record CandidatePlan(TargetBucket Bucket, IReadOnlyList<TargetData> Candidates);

    /// <summary>
    /// Stores one rescue candidate and whether it exceeds the standard ceiling.
    /// </summary>
    /// <param name="Target">The target candidate.</param>
    /// <param name="Upward">Whether the target requires upward expansion.</param>
    private sealed record UpwardCandidate(TargetData Target, bool Upward);

    /// <summary>
    /// Stores candidates and the standard strength ceiling for one rescue branch.
    /// </summary>
    /// <param name="Candidates">The standard and upward candidates.</param>
    /// <param name="Floor">The preferred maximum strength used to measure expansion.</param>
    private sealed record UpwardPlan(IReadOnlyList<UpwardCandidate> Candidates, int Floor);
}

/// <summary>
/// Identifies the evolving displayed fusion component.
/// </summary>
internal enum FusionComponentSide
{
    /// <summary>
    /// The body component.
    /// </summary>
    Body = 0,

    /// <summary>
    /// The head component.
    /// </summary>
    Head = 1
}

/// <summary>
/// Stores every exact assigned branch for one custom-fusion source.
/// </summary>
/// <param name="PackedSourceComponents">The body and head identifiers packed into ten bits each.</param>
/// <param name="SourceBst">The source fusion's generated base-stat total.</param>
/// <param name="Branches">The assigned conceptual branches.</param>
internal sealed record FusionEvolutionSourceAssignment(int PackedSourceComponents, int SourceBst, IReadOnlyList<FusionEvolutionAssignedBranch> Branches);

/// <summary>
/// Stores one exact complete-fusion evolution assignment.
/// </summary>
/// <param name="Side">The evolving component side.</param>
/// <param name="ComponentBranchIdentity">The conceptual normal branch identity.</param>
/// <param name="TargetId">The numeric custom-fusion target identifier.</param>
/// <param name="TargetBst">The generated target base-stat total.</param>
internal sealed record FusionEvolutionAssignedBranch(FusionComponentSide Side, string ComponentBranchIdentity, int TargetId, int TargetBst);

/// <summary>
/// Stores one valid candidate returned by the native fusion-evolution worker.
/// </summary>
/// <param name="TargetId">The numeric custom-fusion target identifier.</param>
/// <param name="TargetBst">The generated target base-stat total.</param>
internal sealed record FusionEvolutionCandidateTarget(int TargetId, int TargetBst);
