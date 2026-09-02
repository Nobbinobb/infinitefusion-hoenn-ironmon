using System.Collections.Concurrent;
using System.Text;

namespace Ironmon.Tracker.Connection.Obtainability;

/// <summary>
/// Reproduces schema-5 player-fusion mappings in parallel outside the game runtime.
/// </summary>
internal sealed class PlayerFusionMappingWorker
{
    private static readonly int _maximumParallelism = Math.Max(1, Environment.ProcessorCount * 3 / 8);
    private const ulong FnvOffsetBasis = 14_695_981_039_346_656_037;
    private const ulong FnvPrime = 1_099_511_628_211;
    private const int MaterialIdBits = 10;
    private const int MaterialIdMask = (1 << MaterialIdBits) - 1;
    private const int GuaranteedNonWeakerGap = 40;
    private const int MaximumBonusBasis = 500;
    private const int MaximumLossPoints = 80;
    private const int MaximumCachedStates = 2;
    private readonly PlayerFusionMappingWorkerCatalog _catalog;
    private readonly ConcurrentDictionary<PlayerFusionStateKey, Lazy<WorkerState>> _states = new();

    /// <summary>
    /// Initializes a worker from the embedded generated game-data catalog.
    /// </summary>
    internal PlayerFusionMappingWorker()
        : this(PlayerFusionMappingWorkerCatalog.Load())
    {
    }

    /// <summary>
    /// Initializes a worker from one validated catalog.
    /// </summary>
    /// <param name="catalog">The generated deterministic game-data catalog.</param>
    internal PlayerFusionMappingWorker(PlayerFusionMappingWorkerCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>
    /// Maps every unordered pair from a bounded normal-material set while retaining CPU capacity for the connected game.
    /// </summary>
    /// <param name="seed">The Ironmon run seed.</param>
    /// <param name="generatorVersion">The player-fusion generator schema version.</param>
    /// <param name="materialIds">The distinct normal material identifiers.</param>
    /// <param name="cancellationToken">The token that cancels parallel work.</param>
    /// <returns>The mappings in stable unordered-pair order.</returns>
    internal IReadOnlyList<PlayerFusionMappedPair> MapAll(long seed, int generatorVersion, IReadOnlyList<int> materialIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(materialIds);
        if (generatorVersion != _catalog.PlayerFusionGeneratorVersion)
            throw new NotSupportedException($"Player-fusion generator {generatorVersion} is unsupported by the worker catalog.");

        int[] materials = [.. materialIds.Distinct().Order()];
        if (materials.Any(value => value <= 0 || value > _catalog.NormalSpeciesCount))
            throw new ArgumentOutOfRangeException(nameof(materialIds), "Every fusion material must be a normal species in the generated catalog.");

        WorkerState state = GetState(seed, generatorVersion);
        int pairCount = checked(materials.Length * (materials.Length + 1) / 2);
        PlayerFusionMappedPair[] mappings = new PlayerFusionMappedPair[pairCount];
        Parallel.For(0, materials.Length, new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = _maximumParallelism }, firstIndex =>
        {
            int outputIndex = PairOffset(materials.Length, firstIndex);
            for (int secondIndex = firstIndex; secondIndex < materials.Length; secondIndex++)
            {
                int first = materials[firstIndex];
                int second = materials[secondIndex];
                (int firstResult, int secondResult) = SelectResults(state, first, second);
                mappings[outputIndex++] = new PlayerFusionMappedPair(first, second, firstResult, secondResult);
            }
        });

        return mappings;
    }

    /// <summary>
    /// Maps one ordered normal-material pair using the same retained state as obtainability preparation.
    /// </summary>
    /// <param name="seed">The run seed.</param>
    /// <param name="generatorVersion">The player-fusion generator schema.</param>
    /// <param name="bodyId">The normal Body material.</param>
    /// <param name="headId">The normal Head material.</param>
    /// <returns>The numeric custom fusion result in the requested orientation.</returns>
    internal int MapOrderedPair(long seed, int generatorVersion, int bodyId, int headId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bodyId, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bodyId, _catalog.NormalSpeciesCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(headId, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(headId, _catalog.NormalSpeciesCount);
        if (generatorVersion != _catalog.PlayerFusionGeneratorVersion)
            throw new NotSupportedException($"Player-fusion generator {generatorVersion} is unsupported by the worker catalog.");

        WorkerState state = GetState(seed, generatorVersion);
        (int first, int second) = SelectResults(state, Math.Min(bodyId, headId), Math.Max(bodyId, headId));
        return bodyId <= headId ? first : second;
    }

    /// <summary>
    /// Reports the global reverse-pair quality for regression tests and diagnostics.
    /// </summary>
    internal PlayerFusionPairingAudit AuditPairing(long seed, int generatorVersion)
    {
        WorkerState state = GetState(seed, generatorVersion);
        int maximumBstDifference = state.Pairs.Max(pair => Math.Abs(pair.First.Bst - pair.Second.Bst));
        int pairsWithoutSharedType = state.Pairs.Count(pair => (pair.First.TypeMask & pair.Second.TypeMask) == 0);
        return new PlayerFusionPairingAudit(state.Pairs.Count, maximumBstDifference, pairsWithoutSharedType, state.MaximumLegalRangeWidth);
    }

    /// <summary>
    /// Returns the seed-specific global reverse partner for one custom fusion.
    /// </summary>
    /// <param name="seed">The Ironmon run seed.</param>
    /// <param name="generatorVersion">The player-fusion generator schema version.</param>
    /// <param name="resultId">The numeric custom-fusion identifier.</param>
    /// <returns>The numeric identifier of the paired reverse fusion.</returns>
    internal int ReversePartner(long seed, int generatorVersion, int resultId)
    {
        WorkerState state = GetState(seed, generatorVersion);
        if (resultId <= _catalog.NormalSpeciesCount || resultId >= state.ReversePartners.Count || state.ReversePartners[resultId] == 0)
            throw new ArgumentOutOfRangeException(nameof(resultId), "The result must belong to the paired custom-fusion pool.");

        return state.ReversePartners[resultId];
    }

    /// <summary>
    /// Gets or builds the expensive seed-specific mapping state shared by material requests and obtainability work.
    /// </summary>
    /// <param name="seed">The Ironmon run seed.</param>
    /// <param name="generatorVersion">The player-fusion generator schema version.</param>
    /// <returns>The retained deterministic mapping state.</returns>
    private WorkerState GetState(long seed, int generatorVersion)
    {
        PlayerFusionStateKey key = new(seed, generatorVersion);
        Lazy<WorkerState> state = _states.GetOrAdd(key, value => new Lazy<WorkerState>(() => BuildState(value.Seed, value.GeneratorVersion, CancellationToken.None), LazyThreadSafetyMode.ExecutionAndPublication));
        TrimStates(key);
        return state.Value;
    }

    /// <summary>
    /// Bounds retained seed-specific mapping state while preserving the requested entry.
    /// </summary>
    /// <param name="currentKey">The state currently being requested.</param>
    private void TrimStates(PlayerFusionStateKey currentKey)
    {
        if (_states.Count <= MaximumCachedStates)
            return;

        foreach (PlayerFusionStateKey key in _states.Keys.Where(key => key != currentKey).Take(_states.Count - MaximumCachedStates))
            _states.TryRemove(key, out _);
    }

    /// <summary>
    /// Computes the stable flat output offset for one triangular material row.
    /// </summary>
    private static int PairOffset(int materialCount, int firstIndex)
        => checked(firstIndex * materialCount - firstIndex * (firstIndex - 1) / 2);

    /// <summary>
    /// Builds the seed-specific randomized stats, global reverse pairs, and BST index.
    /// </summary>
    private WorkerState BuildState(long seed, int generatorVersion, CancellationToken cancellationToken)
    {
        int[][] stats = new int[_catalog.NormalSpeciesCount + 1][];
        ulong[] types = new ulong[_catalog.NormalSpeciesCount + 1];
        foreach (PlayerFusionNormalSpecies species in _catalog.NormalSpecies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            stats[species.Id] = DistributeStats(seed, species);
            types[species.Id] = species.TypeMask;
        }

        TargetData[] targets = [.. _catalog.CustomFusionPool.Select(target => CreateTargetData(target, stats))];
        int maximumLegalRangeWidth = MaximumFusionRangeWidth(stats);
        TargetPair[] pairs = BuildTargetPairs(seed, generatorVersion, targets, maximumLegalRangeWidth, cancellationToken);
        IReadOnlyDictionary<int, IReadOnlyList<OrientedTarget>> bstIndex = BuildBstIndex(pairs);
        int maximumSpeciesId = checked(_catalog.NormalSpeciesCount * _catalog.NormalSpeciesCount + _catalog.NormalSpeciesCount);
        int[] reversePartners = new int[maximumSpeciesId + 1];
        foreach (TargetPair pair in pairs)
        {
            reversePartners[pair.First.Id] = pair.Second.Id;
            reversePartners[pair.Second.Id] = pair.First.Id;
        }

        return new WorkerState(seed, generatorVersion, stats, types, pairs, bstIndex, reversePartners, bstIndex.Keys.Min(), bstIndex.Keys.Max(), maximumLegalRangeWidth);
    }

    /// <summary>
    /// Creates one custom target's numeric identity, generated BST, types, and components.
    /// </summary>
    private TargetData CreateTargetData(PlayerFusionTargetSpecies target, IReadOnlyList<int[]> stats)
    {
        int body = target.PackedComponents >> MaterialIdBits;
        int head = target.PackedComponents & MaterialIdMask;
        int id = checked(body * _catalog.NormalSpeciesCount + head);
        return new TargetData(id, body, head, FusedBst(stats[body], stats[head]), target.TypeMask);
    }

    /// <summary>
    /// Reproduces the Ruby base-stat distributor for one normal species.
    /// </summary>
    private static int[] DistributeStats(long seed, PlayerFusionNormalSpecies species)
    {
        const int minimumStat = 5;
        const int maximumStat = 255;
        const int weightRange = 1_000_000;
        string[] statNames = ["HP", "ATTACK", "DEFENSE", "SPECIAL_ATTACK", "SPECIAL_DEFENSE", "SPEED"];
        int total = species.Stats.Sum();
        int[] values = [.. Enumerable.Repeat(minimumStat, 6)];
        int[] capacities = [.. Enumerable.Repeat(maximumStat - minimumStat, 6)];
        ulong[] weights = [.. statNames.Select(stat => 1UL + DeterministicValue(1, seed, "base_stats", species.Identity, stat, "weight") % weightRange)];
        int remaining = total - minimumStat * 6;
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
    /// Builds one global strength-aware, type-aware reverse pairing.
    /// </summary>
    private static TargetPair[] BuildTargetPairs(long seed, int generatorVersion, IReadOnlyList<TargetData> targets, int maximumPairDifference, CancellationToken cancellationToken)
    {
        int bestMaximumBstDifference = int.MaxValue;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            TargetData[] ordered = [.. targets
                .OrderBy(target => target.Bst)
                .ThenBy(target => DeterministicValue(generatorVersion, seed, "player_fusion", "pairing_rank", attempt, target.Id))];
            List<TargetPair> pairs = [];
            bool failed = false;
            for (int position = 0; position < ordered.Length; position += 2)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int partner = FindStrengthPartner(ordered, position, true, maximumPairDifference);
                if (partner < 0)
                    partner = FindStrengthPartner(ordered, position, false, maximumPairDifference);

                if (partner < 0)
                {
                    bool repaired = RepairStrengthPair(pairs, ordered[position], ordered[position + 1], maximumPairDifference);
                    if (repaired)
                        continue;

                    failed = true;
                    break;
                }

                (ordered[position + 1], ordered[partner]) = (ordered[partner], ordered[position + 1]);
                pairs.Add(new TargetPair(ordered[position], ordered[position + 1]));
            }
            if (!failed)
            {
                int maximumBstDifference = pairs.Max(pair => Math.Abs(pair.First.Bst - pair.Second.Bst));
                bestMaximumBstDifference = Math.Min(bestMaximumBstDifference, maximumBstDifference);
                if (maximumBstDifference <= maximumPairDifference)
                    return [.. pairs];
            }
        }

        throw new InvalidOperationException($"The custom fusion pool could not form strength-matched reverse pairs; the best maximum difference was {bestMaximumBstDifference} BST.");
    }

    /// <summary>
    /// Finds the nearest remaining disjoint partner, preferring at least one shared type.
    /// </summary>
    private static int FindStrengthPartner(IReadOnlyList<TargetData> ordered, int position, bool requireSharedType, int maximumPairDifference)
    {
        TargetData first = ordered[position];
        for (int candidatePosition = position + 1; candidatePosition < ordered.Count; candidatePosition++)
        {
            TargetData second = ordered[candidatePosition];
            if (second.Bst - first.Bst > maximumPairDifference)
                break;

            if (SharesComponent(first, second))
                continue;

            if (requireSharedType && (first.TypeMask & second.TypeMask) == 0)
                continue;

            return candidatePosition;
        }
        return -1;
    }

    /// <summary>
    /// Re-pairs two blocked targets with the best compatible earlier pair.
    /// </summary>
    private static bool RepairStrengthPair(List<TargetPair> pairs, TargetData currentFirst, TargetData currentSecond, int maximumPairDifference)
    {
        ((int Distance, int TypePenalty, int PairIndex) Score, int PairIndex, TargetPair First, TargetPair Second)? best = null;
        for (int pairIndex = 0; pairIndex < pairs.Count; pairIndex++)
        {
            TargetPair previous = pairs[pairIndex];
            for (int orientation = 0; orientation < 2; orientation++)
            {
                TargetData previousFirst = orientation == 0 ? previous.First : previous.Second;
                TargetData previousSecond = orientation == 0 ? previous.Second : previous.First;
                TargetPair first = new(currentFirst, previousFirst);
                TargetPair second = new(currentSecond, previousSecond);
                if (SharesComponent(first.First, first.Second) || SharesComponent(second.First, second.Second))
                    continue;

                if (Math.Abs(first.First.Bst - first.Second.Bst) > maximumPairDifference || Math.Abs(second.First.Bst - second.Second.Bst) > maximumPairDifference)
                    continue;

                int typePenalty = ((first.First.TypeMask & first.Second.TypeMask) == 0 ? 1 : 0)
                    + ((second.First.TypeMask & second.Second.TypeMask) == 0 ? 1 : 0);

                int distance = Math.Abs(first.First.Bst - first.Second.Bst) + Math.Abs(second.First.Bst - second.Second.Bst);
                (int Distance, int TypePenalty, int PairIndex) score = (distance, typePenalty, pairIndex);
                if (best is null || score.CompareTo(best.Value.Score) < 0)
                    best = (score, pairIndex, first, second);
            }
        }

        if (best is null)
            return RepairStrengthChain(pairs, currentFirst, currentSecond, maximumPairDifference);

        pairs.RemoveAt(best.Value.PairIndex);
        pairs.Add(best.Value.First);
        pairs.Add(best.Value.Second);
        return true;
    }

    /// <summary>
    /// Repairs a blocked pair through a deterministic alternating chain without relaxing strength or component eligibility.
    /// </summary>
    private static bool RepairStrengthChain(List<TargetPair> pairs, TargetData currentFirst, TargetData currentSecond, int maximumPairDifference)
    {
        Queue<(TargetData Current, TargetPair[] Replacements, int[] Removed)> queue = new();
        queue.Enqueue((currentFirst, [], []));
        HashSet<int> visited = [currentFirst.Id];
        while (queue.TryDequeue(out var work))
        {
            for (int index = 0; index < pairs.Count; index++)
            {
                if (work.Removed.Contains(index))
                    continue;

                TargetPair previous = pairs[index];
                for (int orientation = 0; orientation < 2; orientation++)
                {
                    TargetData partner = orientation == 0 ? previous.First : previous.Second;
                    TargetData displaced = orientation == 0 ? previous.Second : previous.First;
                    if (visited.Contains(displaced.Id) || !StrengthPairCompatible(work.Current, partner, maximumPairDifference))
                        continue;

                    TargetPair replacement = new(work.Current, partner);
                    if (StrengthPairCompatible(displaced, currentSecond, maximumPairDifference))
                    {
                        foreach (int removed in work.Removed.Append(index).OrderDescending())
                            pairs.RemoveAt(removed);

                        pairs.AddRange(work.Replacements);
                        pairs.Add(replacement);
                        pairs.Add(new TargetPair(displaced, currentSecond));
                        return true;
                    }

                    visited.Add(displaced.Id);
                    queue.Enqueue((displaced, [.. work.Replacements, replacement], [.. work.Removed, index]));
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a repair edge preserves the global strength and disjoint-component constraints.
    /// </summary>
    private static bool StrengthPairCompatible(TargetData first, TargetData second, int maximumPairDifference)
        => Math.Abs(first.Bst - second.Bst) <= maximumPairDifference && !SharesComponent(first, second);

    /// <summary>
    /// Determines whether two custom targets share either normal component.
    /// </summary>
    private static bool SharesComponent(TargetData first, TargetData second)
        => first.BodyId == second.BodyId || first.BodyId == second.HeadId || first.HeadId == second.BodyId || first.HeadId == second.HeadId;

    /// <summary>
    /// Indexes both orientations of every global reverse pair by the first BST.
    /// </summary>
    private static IReadOnlyDictionary<int, IReadOnlyList<OrientedTarget>> BuildBstIndex(IReadOnlyList<TargetPair> pairs)
    {
        Dictionary<int, List<OrientedTarget>> mutable = [];
        for (int position = 0; position < pairs.Count; position++)
        {
            AddBstIndexEntry(mutable, pairs[position].First.Bst, new OrientedTarget(position, false));
            AddBstIndexEntry(mutable, pairs[position].Second.Bst, new OrientedTarget(position, true));
        }

        return mutable.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<OrientedTarget>)[.. entry.Value]);
    }

    /// <summary>
    /// Adds one oriented reverse-pair position to the mutable BST index.
    /// </summary>
    private static void AddBstIndexEntry(Dictionary<int, List<OrientedTarget>> index, int bst, OrientedTarget target)
    {
        if (!index.TryGetValue(bst, out List<OrientedTarget>? entries))
        {
            entries = [];
            index.Add(bst, entries);
        }

        entries.Add(target);
    }

    /// <summary>
    /// Selects both deterministic result orientations for one unordered material pair.
    /// </summary>
    private static (int First, int Second) SelectResults(WorkerState state, int firstId, int secondId)
    {
        ulong sourceTypes = state.Types[firstId] | state.Types[secondId];
        int firstBst = state.Stats[firstId].Sum();
        int secondBst = state.Stats[secondId].Sum();
        (int minimum, int maximum) = FusionBstRange(firstBst, secondBst);
        int target = minimum + (int)(DeterministicValue(state.GeneratorVersion, state.Seed, "player_fusion", "bst_target", firstId, secondId) % (ulong)(maximum - minimum + 1));
        (int First, int Second)? result = ClosestResults(state, firstId, secondId, sourceTypes, target, minimum, maximum);
        result ??= ClosestResults(state, firstId, secondId, sourceTypes, target, state.MinimumTargetBst, state.MaximumTargetBst);
        return result ?? throw new InvalidOperationException($"No custom fusion pair satisfies the BST and type rules for {firstId}+{secondId} in {minimum}-{maximum}.");
    }

    /// <summary>
    /// Calculates the inclusive schema-5 BST range from the stronger and weaker materials.
    /// </summary>
    internal static (int Minimum, int Maximum) FusionBstRange(int firstBst, int secondBst)
    {
        int higher = Math.Max(firstBst, secondBst);
        int lower = Math.Min(firstBst, secondBst);
        int gap = higher - lower;
        int bonusBasis = Math.Min(higher, MaximumBonusBasis);
        int maximum = higher + 10 * bonusBasis / (100 + gap);
        if (gap <= GuaranteedNonWeakerGap)
            return (higher, maximum);

        int curveLoss = bonusBasis * (gap - GuaranteedNonWeakerGap) / (4 * (100 + gap));
        int loss = Math.Min(MaximumLossPoints, Math.Min(3 * higher / 20, curveLoss));
        int minimum = Math.Max(lower, higher - loss);
        return (minimum, maximum);
    }

    /// <summary>
    /// Calculates the widest legal result interval produced by any two normal-material BSTs.
    /// </summary>
    private static int MaximumFusionRangeWidth(IReadOnlyList<int[]> stats)
    {
        int[] values = [.. stats.Skip(1).Where(value => value is not null).Select(value => value.Sum()).Distinct()];
        int maximum = 0;
        foreach (int first in values)
        {
            foreach (int second in values)
            {
                (int minimum, int upper) = FusionBstRange(first, second);
                maximum = Math.Max(maximum, upper - minimum);
            }
        }
        return maximum;
    }

    /// <summary>
    /// Finds the compatible global reverse pair closest to the rolled target.
    /// </summary>
    private static (int First, int Second)? ClosestResults(WorkerState state, int firstId, int secondId, ulong sourceTypes, int targetBst, int minimum, int maximum)
    {
        (int Score, ulong Rank, int First, int Second)? best = null;
        int maximumDistance = Math.Max(Math.Abs(targetBst - state.MinimumTargetBst), Math.Abs(state.MaximumTargetBst - targetBst));
        for (int distance = 0; distance <= maximumDistance; distance++)
        {
            if (best is not null && distance > best.Value.Score)
                break;

            best = ClosestResultsAtBst(state, firstId, secondId, sourceTypes, targetBst, targetBst - distance, minimum, maximum, best);
            if (distance > 0)
                best = ClosestResultsAtBst(state, firstId, secondId, sourceTypes, targetBst, targetBst + distance, minimum, maximum, best);
        }
        return best is null ? null : (best.Value.First, best.Value.Second);
    }

    /// <summary>
    /// Compares every oriented global pair at one first-result BST.
    /// </summary>
    private static (int Score, ulong Rank, int First, int Second)? ClosestResultsAtBst(WorkerState state, int firstId, int secondId, ulong sourceTypes, int targetBst, int firstBst, int minimum, int maximum, (int Score, ulong Rank, int First, int Second)? best)
    {
        if (firstBst < minimum || firstBst > maximum)
            return best;

        if (!state.BstIndex.TryGetValue(firstBst, out IReadOnlyList<OrientedTarget>? entries))
            return best;

        foreach (OrientedTarget entry in entries)
        {
            TargetPair pair = state.Pairs[entry.PairPosition];
            TargetData first = entry.Reversed ? pair.Second : pair.First;
            TargetData second = entry.Reversed ? pair.First : pair.Second;
            if ((first.TypeMask & sourceTypes) == 0 || (second.TypeMask & sourceTypes) == 0)
                continue;

            if (second.Bst < minimum || second.Bst > maximum)
                continue;

            int score = Math.Abs(firstBst - targetBst) + Math.Abs(second.Bst - targetBst);
            if (best is not null && score > best.Value.Score)
                continue;

            ulong rank = DeterministicValue(state.GeneratorVersion, state.Seed, "player_fusion", "target_match", firstId, secondId, first.Id, second.Id);
            if (best is null || score < best.Value.Score || score == best.Value.Score && rank < best.Value.Rank)
                best = (score, rank, first.Id, second.Id);
        }
        return best;
    }

    /// <summary>
    /// Calculates an oriented fusion BST from six generated component stats.
    /// </summary>
    private static int FusedBst(IReadOnlyList<int> body, IReadOnlyList<int> head)
        => 2 * head[0] / 3 + body[0] / 3 + 2 * body[1] / 3 + head[1] / 3 + 2 * body[2] / 3 + head[2] / 3 + 2 * head[3] / 3 + body[3] / 3 + 2 * head[4] / 3 + body[4] / 3 + 2 * body[5] / 3 + head[5] / 3;

    /// <summary>
    /// Computes a delimiter-joined deterministic FNV-1a value.
    /// </summary>
    private static ulong DeterministicValue(params object[] parts)
        => Fnv1a(string.Join('|', parts));

    /// <summary>
    /// Computes a UTF-8 FNV-1a value from one initial state.
    /// </summary>
    private static ulong Fnv1a(string input, ulong initialValue = FnvOffsetBasis)
    {
        ulong value = initialValue;
        foreach (byte item in Encoding.UTF8.GetBytes(input))
        {
            value ^= item;
            value *= FnvPrime;
        }

        return value;
    }

    /// <summary>
    /// Stores one seed-specific immutable mapping model.
    /// </summary>
    /// <param name="Seed">The Ironmon run seed.</param>
    /// <param name="GeneratorVersion">The player-fusion schema version.</param>
    /// <param name="Stats">The generated normal-species stat arrays.</param>
    /// <param name="Types">The normal-species type masks.</param>
    /// <param name="Pairs">The global strength-aware reverse pairs.</param>
    /// <param name="BstIndex">Both pair orientations indexed by first-result BST.</param>
    /// <param name="ReversePartners">The numeric reverse partner indexed by custom-fusion identifier.</param>
    /// <param name="MinimumTargetBst">The smallest custom-result BST.</param>
    /// <param name="MaximumTargetBst">The largest custom-result BST.</param>
    /// <param name="MaximumLegalRangeWidth">The widest result interval allowed by the material formula.</param>
    private sealed record WorkerState(long Seed, int GeneratorVersion, IReadOnlyList<int[]> Stats, IReadOnlyList<ulong> Types, IReadOnlyList<TargetPair> Pairs, IReadOnlyDictionary<int, IReadOnlyList<OrientedTarget>> BstIndex, IReadOnlyList<int> ReversePartners, int MinimumTargetBst, int MaximumTargetBst, int MaximumLegalRangeWidth);

    /// <summary>
    /// Stores one custom result's seed-specific metrics and components.
    /// </summary>
    /// <param name="Id">The numeric fusion species identifier.</param>
    /// <param name="BodyId">The body component identifier.</param>
    /// <param name="HeadId">The head component identifier.</param>
    /// <param name="Bst">The generated fusion BST.</param>
    /// <param name="TypeMask">The target's type mask.</param>
    private sealed record TargetData(int Id, int BodyId, int HeadId, int Bst, ulong TypeMask);

    /// <summary>
    /// Stores one global, reversible pair of custom fusion results.
    /// </summary>
    /// <param name="First">The first custom result.</param>
    /// <param name="Second">The result's unique global reverse partner.</param>
    private sealed record TargetPair(TargetData First, TargetData Second);

    /// <summary>
    /// Locates one orientation of a global reverse pair.
    /// </summary>
    /// <param name="PairPosition">The pair's stable position.</param>
    /// <param name="Reversed">Whether the pair's second result comes first.</param>
    private sealed record OrientedTarget(int PairPosition, bool Reversed);

}

/// <summary>
/// Stores both custom results selected for one unordered normal-material pair.
/// </summary>
/// <param name="FirstMaterialId">The lower-position material identifier.</param>
/// <param name="SecondMaterialId">The upper-position material identifier.</param>
/// <param name="FirstResultId">The result when the first material is Body.</param>
/// <param name="SecondResultId">The result when the second material is Body.</param>
internal sealed record PlayerFusionMappedPair(int FirstMaterialId, int SecondMaterialId, int FirstResultId, int SecondResultId);

/// <summary>
/// Identifies one reusable seed-specific player-fusion mapping state.
/// </summary>
/// <param name="Seed">The Ironmon run seed.</param>
/// <param name="GeneratorVersion">The player-fusion generator schema version.</param>
internal sealed record PlayerFusionStateKey(long Seed, int GeneratorVersion);

/// <summary>
/// Summarizes the strength and type quality of one global reverse pairing.
/// </summary>
/// <param name="PairCount">The number of global reverse pairs.</param>
/// <param name="MaximumBstDifference">The largest BST gap within one global reverse pair.</param>
/// <param name="PairsWithoutSharedType">The number of pairs whose results share no type.</param>
/// <param name="MaximumLegalRangeWidth">The widest result interval allowed by the material formula.</param>
internal sealed record PlayerFusionPairingAudit(int PairCount, int MaximumBstDifference, int PairsWithoutSharedType, int MaximumLegalRangeWidth);
