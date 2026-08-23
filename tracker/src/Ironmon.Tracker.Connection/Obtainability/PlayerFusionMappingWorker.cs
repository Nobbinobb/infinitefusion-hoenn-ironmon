using System.Text;

namespace Ironmon.Tracker.Connection.Obtainability;

/// <summary>
/// Reproduces schema-3 player-fusion mappings in parallel outside the game runtime.
/// </summary>
internal sealed class PlayerFusionMappingWorker
{
    private const ulong FnvOffsetBasis = 14_695_981_039_346_656_037;
    private const ulong FnvPrime = 1_099_511_628_211;
    private const int MaterialIdBits = 10;
    private const int MaterialIdMask = (1 << MaterialIdBits) - 1;
    private const int PreferredMinimumPercent = 90;
    private const int PreferredMaximumPercent = 115;
    private readonly PlayerFusionMappingWorkerCatalog _catalog;

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
    /// Maps every unordered pair from a bounded normal-material set using all available worker threads.
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

        WorkerState state = BuildState(seed, generatorVersion, cancellationToken);
        int pairCount = checked(materials.Length * (materials.Length + 1) / 2);
        PlayerFusionMappedPair[] mappings = new PlayerFusionMappedPair[pairCount];
        Parallel.For(0, materials.Length, new ParallelOptions { CancellationToken = cancellationToken }, firstIndex =>
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
    /// Computes the stable flat output offset for one triangular material row.
    /// </summary>
    private static int PairOffset(int materialCount, int firstIndex)
        => checked(firstIndex * materialCount - firstIndex * (firstIndex - 1) / 2);

    /// <summary>
    /// Builds the seed-specific randomized stats and disjoint reverse-result pairs.
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
        TargetPair[] pairs = BuildTargetPairs(seed, generatorVersion, targets, cancellationToken);
        return new WorkerState(seed, generatorVersion, stats, types, pairs);
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
    /// Builds the seed-specific disjoint reverse-result pair roster.
    /// </summary>
    private static TargetPair[] BuildTargetPairs(long seed, int generatorVersion, IReadOnlyList<TargetData> targets, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            TargetData[] shuffled = DeterministicShuffle(seed, generatorVersion, targets, attempt);
            List<TargetPair> pairs = [];
            bool failed = false;
            for (int position = 0; position < shuffled.Length; position += 2)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int partner = position + 1;
                while (partner < shuffled.Length && SharesComponent(shuffled[position], shuffled[partner]))
                    partner++;

                if (partner >= shuffled.Length)
                {
                    failed = true;
                    break;
                }

                (shuffled[position + 1], shuffled[partner]) = (shuffled[partner], shuffled[position + 1]);
                pairs.Add(new TargetPair(shuffled[position], shuffled[position + 1]));
            }

            if (!failed)
                return [.. pairs];
        }
        throw new InvalidOperationException("The custom fusion pool could not form disjoint reverse pairs.");
    }

    /// <summary>
    /// Reproduces the schema pairing shuffle.
    /// </summary>
    private static TargetData[] DeterministicShuffle(long seed, int generatorVersion, IReadOnlyList<TargetData> targets, int attempt)
    {
        TargetData[] shuffled = [.. targets];
        ulong state = DeterministicValue(generatorVersion, seed, "player_fusion", "pairing", attempt);
        if (state == 0)
            state = FnvOffsetBasis;

        for (int index = shuffled.Length - 1; index > 0; index--)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            int swapIndex = (int)(state % (ulong)(index + 1));
            (shuffled[index], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[index]);
        }
        return shuffled;
    }

    /// <summary>
    /// Determines whether two custom targets share either normal component.
    /// </summary>
    private static bool SharesComponent(TargetData first, TargetData second)
        => first.BodyId == second.BodyId || first.BodyId == second.HeadId || first.HeadId == second.BodyId || first.HeadId == second.HeadId;

    /// <summary>
    /// Selects both deterministic result orientations for one unordered material pair.
    /// </summary>
    private static (int First, int Second) SelectResults(WorkerState state, int firstId, int secondId)
    {
        ulong sourceTypes = state.Types[firstId] | state.Types[secondId];
        int forwardBst = FusedBst(state.Stats[firstId], state.Stats[secondId]);
        int reverseBst = FusedBst(state.Stats[secondId], state.Stats[firstId]);
        int forwardMinimum = DivideRoundUp(forwardBst * PreferredMinimumPercent, 100);
        int forwardMaximum = forwardBst * PreferredMaximumPercent / 100;
        int reverseMinimum = DivideRoundUp(reverseBst * PreferredMinimumPercent, 100);
        int reverseMaximum = reverseBst * PreferredMaximumPercent / 100;
        int start = (int)(DeterministicResultValue(state.GeneratorVersion, state.Seed, firstId, secondId) % (ulong)state.Pairs.Count);
        bool reverseFirst = (DeterministicValue(state.GeneratorVersion, state.Seed, "player_fusion", "orientation", firstId, secondId) & 1) == 1;
        for (int offset = 0; offset < state.Pairs.Count; offset++)
        {
            TargetPair pair = state.Pairs[(start + offset) % state.Pairs.Count];
            if (reverseFirst)
            {
                if (Matches(pair.Second, sourceTypes, forwardMinimum, forwardMaximum) && Matches(pair.First, sourceTypes, reverseMinimum, reverseMaximum))
                    return (pair.Second.Id, pair.First.Id);

                if (Matches(pair.First, sourceTypes, forwardMinimum, forwardMaximum) && Matches(pair.Second, sourceTypes, reverseMinimum, reverseMaximum))
                    return (pair.First.Id, pair.Second.Id);
            }
            else
            {
                if (Matches(pair.First, sourceTypes, forwardMinimum, forwardMaximum) && Matches(pair.Second, sourceTypes, reverseMinimum, reverseMaximum))
                    return (pair.First.Id, pair.Second.Id);

                if (Matches(pair.Second, sourceTypes, forwardMinimum, forwardMaximum) && Matches(pair.First, sourceTypes, reverseMinimum, reverseMaximum))
                    return (pair.Second.Id, pair.First.Id);
            }
        }
        return ClosestResults(state, firstId, secondId, sourceTypes, forwardBst, reverseBst, reverseFirst);
    }

    /// <summary>
    /// Selects the deterministic closest-result fallback when no preferred pair exists.
    /// </summary>
    private static (int First, int Second) ClosestResults(WorkerState state, int firstId, int secondId, ulong sourceTypes, int forwardBst, int reverseBst, bool reverseFirst)
    {
        (int Score, ulong Rank, int First, int Second)? best = null;
        foreach (TargetPair pair in state.Pairs)
        {
            if (reverseFirst)
            {
                best = ClosestCandidate(state, firstId, secondId, sourceTypes, forwardBst, reverseBst, pair.Second, pair.First, best);
                best = ClosestCandidate(state, firstId, secondId, sourceTypes, forwardBst, reverseBst, pair.First, pair.Second, best);
            }
            else
            {
                best = ClosestCandidate(state, firstId, secondId, sourceTypes, forwardBst, reverseBst, pair.First, pair.Second, best);
                best = ClosestCandidate(state, firstId, secondId, sourceTypes, forwardBst, reverseBst, pair.Second, pair.First, best);
            }
        }

        (int _, ulong _, int first, int second) = best ?? throw new InvalidOperationException("No custom fusion pair shares a consumed Pokémon type.");
        return (first, second);
    }

    /// <summary>
    /// Compares one closest-result fallback orientation with the current best candidate.
    /// </summary>
    private static (int Score, ulong Rank, int First, int Second)? ClosestCandidate(WorkerState state, int firstId, int secondId, ulong sourceTypes, int forwardBst, int reverseBst, TargetData first, TargetData second, (int Score, ulong Rank, int First, int Second)? best)
    {
        if ((first.TypeMask & sourceTypes) == 0 || (second.TypeMask & sourceTypes) == 0)
            return best;

        int score = Math.Abs(first.Bst - forwardBst) + Math.Abs(second.Bst - reverseBst);
        if (best is not null && score > best.Value.Score)
            return best;

        ulong rank = DeterministicValue(state.GeneratorVersion, state.Seed, "player_fusion", "fallback", firstId, secondId, first.Id, second.Id);
        if (best is null || score < best.Value.Score || score == best.Value.Score && rank < best.Value.Rank)
            return (score, rank, first.Id, second.Id);

        return best;
    }

    /// <summary>
    /// Tests one custom target against the consumed types and preferred BST interval.
    /// </summary>
    private static bool Matches(TargetData target, ulong sourceTypes, int minimum, int maximum)
        => (target.TypeMask & sourceTypes) != 0 && target.Bst >= minimum && target.Bst <= maximum;

    /// <summary>
    /// Calculates an oriented fusion BST from six generated component stats.
    /// </summary>
    private static int FusedBst(IReadOnlyList<int> body, IReadOnlyList<int> head)
        => 2 * head[0] / 3 + body[0] / 3 + 2 * body[1] / 3 + head[1] / 3 + 2 * body[2] / 3 + head[2] / 3 + 2 * head[3] / 3 + body[3] / 3 + 2 * head[4] / 3 + body[4] / 3 + 2 * body[5] / 3 + head[5] / 3;

    /// <summary>
    /// Divides positive integers while rounding upward.
    /// </summary>
    private static int DivideRoundUp(int value, int divisor)
        => (value + divisor - 1) / divisor;

    /// <summary>
    /// Computes the optimized deterministic result-position hash.
    /// </summary>
    private static ulong DeterministicResultValue(int generatorVersion, long seed, int firstId, int secondId)
    {
        ulong value = Fnv1a($"{generatorVersion}|{seed}|player_fusion|result|");
        value = Fnv1a(firstId.ToString(), value);
        value = Fnv1a("|", value);
        return Fnv1a(secondId.ToString(), value);
    }

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
    /// <param name="Pairs">The ordered disjoint custom-result pairs.</param>
    private sealed record WorkerState(long Seed, int GeneratorVersion, IReadOnlyList<int[]> Stats, IReadOnlyList<ulong> Types, IReadOnlyList<TargetPair> Pairs);

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
    /// Stores one disjoint pair of reversible custom results.
    /// </summary>
    /// <param name="First">The first result orientation.</param>
    /// <param name="Second">The paired reverse result.</param>
    private sealed record TargetPair(TargetData First, TargetData Second);
}

/// <summary>
/// Stores both custom results selected for one unordered normal-material pair.
/// </summary>
/// <param name="FirstMaterialId">The lower-position material identifier.</param>
/// <param name="SecondMaterialId">The upper-position material identifier.</param>
/// <param name="FirstResultId">The result when the first material is Body.</param>
/// <param name="SecondResultId">The result when the second material is Body.</param>
internal sealed record PlayerFusionMappedPair(int FirstMaterialId, int SecondMaterialId, int FirstResultId, int SecondResultId);
