using System.Globalization;

namespace Ironmon.Tracker.Connection.Presentation;

/// <summary>
/// Completes numeric defense ranges from game-selected public rule mechanics before state publication.
/// </summary>
/// <remarks>
/// Uses the game's ordered double arithmetic and rule identities. Selection, conditions, source labels,
/// privacy filtering, and protection or recovery projection remain authoritative in the game.
/// </remarks>
internal static class TrackerDefenseDamageProfilePresentation
{
    private const string PhysicalCategory = "physical";
    private const string SpecialCategory = "special";
    private const string RoundTripFormat = "R";

    /// <summary>
    /// Selects the supported numeric facts version for the connection handshake.
    /// </summary>
    /// <param name="offeredVersion">The version offered by the current game peer.</param>
    /// <returns>The supported version, or zero when native composition must remain enabled.</returns>
    internal static int SupportedVersion(int offeredVersion)
        => offeredVersion == DefenseDamageProfileFactsSnapshot.CurrentVersion ? offeredVersion : 0;

    /// <summary>
    /// Completes deferred numeric profiles or safely removes unavailable numeric advice.
    /// </summary>
    /// <remarks>
    /// Explicit native ranges take precedence. Facts must be marked, versioned, negotiated, and fully
    /// validated. Failed completion does not replace the game's selected protection or recovery data.
    /// </remarks>
    /// <param name="overview">The decoded game overview, or null when no overview is available.</param>
    /// <param name="negotiatedVersion">The facts version supported by the current game connection.</param>
    internal static void Complete(DefenseOverviewSnapshot? overview, int negotiatedVersion)
    {
        if (overview is null || !overview.DamageProfilesDeferred && overview.DamageProfileFacts is null)
            return;

        if (overview.TypeMatchups is { Count: > 0 } nativeRows && nativeRows.All(row => row is not null && row.HasExplicitDamageProfiles))
        {
            overview.CompleteDamageProfiles(nativeRows);
            return;
        }

        DefenseDamageProfileFactsSnapshot? facts = overview.DamageProfileFacts;
        if (!overview.DamageProfilesDeferred || SupportedVersion(negotiatedVersion) == 0 || facts is null || facts.Version != negotiatedVersion)
        {
            overview.CompleteDamageProfiles(null);
            return;
        }

        overview.CompleteDamageProfiles(TryCompose(overview.TypeMatchups, facts, out IReadOnlyList<DefenseTypeSnapshot> rows) ? rows : null);
    }

    /// <summary>
    /// Validates a bounded facts payload and constructs a complete set of numeric type rows.
    /// </summary>
    /// <param name="types">The game's current baseline type multipliers and public labels.</param>
    /// <param name="facts">The ordered public numeric rule facts.</param>
    /// <param name="rows">The new completed rows on success, or an empty list on failure.</param>
    /// <returns>Whether every deferred row can be completed without publishing partial arithmetic.</returns>
    private static bool TryCompose(IReadOnlyList<DefenseTypeSnapshot> types, DefenseDamageProfileFactsSnapshot facts, out IReadOnlyList<DefenseTypeSnapshot> rows)
    {
        rows = [];
        if (types is null || types.Count > DefenseDamageProfileFactsSnapshot.MaximumTypes || facts.Rules is null || facts.Rules.Count > DefenseDamageProfileFactsSnapshot.MaximumRules)
            return false;

        if (facts.Rules.Count == 0)
            return TryComposeWithoutRules(types, out rows);

        Dictionary<int, DefenseDamageRuleFactSnapshot> identities = [];
        foreach (DefenseDamageRuleFactSnapshot rule in facts.Rules)
        {
            if (!ValidRule(rule))
                return false;

            if (identities.TryGetValue(rule.RuleIdentity, out DefenseDamageRuleFactSnapshot? first))
            {
                if (!SameRule(first, rule))
                    return false;
            }
            else
            {
                if (rule.RuleIdentity != identities.Count)
                    return false;

                identities.Add(rule.RuleIdentity, rule);
            }
        }

        List<DefenseTypeSnapshot> completed = new(types.Count);
        HashSet<string> seenTypes = new(StringComparer.Ordinal);
        try
        {
            foreach (DefenseTypeSnapshot entry in types)
            {
                if (entry is null || string.IsNullOrEmpty(entry.Type) || entry.Name is null || !seenTypes.Add(entry.Type))
                    return false;

                if (entry.HasExplicitDamageProfiles)
                {
                    completed.Add(entry);
                    continue;
                }

                if (HasPartialProfiles(entry))
                    return false;

                AdjustmentCollector adjustments = default;
                (double physicalMin, double physicalMax) = Compose((double)entry.Multiplier, entry.Type, PhysicalCategory, facts.Rules, false, ref adjustments);
                (double typePhysicalMin, double typePhysicalMax) = Compose((double)entry.Multiplier, entry.Type, PhysicalCategory, facts.Rules, true, ref adjustments);
                (double specialMin, double specialMax) = Compose((double)entry.Multiplier, entry.Type, SpecialCategory, facts.Rules, false, ref adjustments);
                (double typeSpecialMin, double typeSpecialMax) = Compose((double)entry.Multiplier, entry.Type, SpecialCategory, facts.Rules, true, ref adjustments);
                if (!adjustments.HasRules)
                {
                    completed.Add(CreateUnmodifiedRow(entry));
                    continue;
                }

                completed.Add(new DefenseTypeSnapshot
                {
                    Type = entry.Type, Name = entry.Name, BaseMultiplier = entry.BaseMultiplier, Multiplier = entry.Multiplier,
                    PhysicalMin = ToDecimal(physicalMin), PhysicalMax = ToDecimal(physicalMax),
                    SpecialMin = ToDecimal(specialMin), SpecialMax = ToDecimal(specialMax),
                    TypePhysicalMin = ToDecimal(typePhysicalMin), TypePhysicalMax = ToDecimal(typePhysicalMax),
                    TypeSpecialMin = ToDecimal(typeSpecialMin), TypeSpecialMax = ToDecimal(typeSpecialMax),
                    Adjustments = adjustments.Snapshots, Exceptions = entry.Exceptions
                });
            }
        }
        catch (Exception exception) when (exception is OverflowException or FormatException)
        {
            return false;
        }

        rows = completed;
        return true;
    }

    /// <summary>
    /// Completes an empty rule set directly from native decimal multipliers without float conversion or adjustment collections.
    /// </summary>
    /// <param name="types">The native type labels and current multipliers.</param>
    /// <param name="rows">The complete rows on success, or an empty list on invalid input.</param>
    /// <returns>Whether every row is a complete native profile or valid unmodified deferred row.</returns>
    private static bool TryComposeWithoutRules(IReadOnlyList<DefenseTypeSnapshot> types, out IReadOnlyList<DefenseTypeSnapshot> rows)
    {
        rows = [];
        List<DefenseTypeSnapshot> completed = new(types.Count);
        HashSet<string> seenTypes = new(StringComparer.Ordinal);
        foreach (DefenseTypeSnapshot entry in types)
        {
            if (entry is null || string.IsNullOrEmpty(entry.Type) || entry.Name is null || !seenTypes.Add(entry.Type))
                return false;

            if (entry.HasExplicitDamageProfiles)
            {
                completed.Add(entry);
            }
            else
            {
                if (HasPartialProfiles(entry))
                    return false;

                completed.Add(CreateUnmodifiedRow(entry));
            }
        }

        rows = completed;
        return true;
    }

    /// <summary>
    /// Copies a valid unchanged matchup into all eight ranges while preserving native exception metadata.
    /// </summary>
    /// <param name="entry">The type row whose applicable rule set is empty.</param>
    /// <returns>A completed row with the original multiplier in every range and no applied adjustments.</returns>
    private static DefenseTypeSnapshot CreateUnmodifiedRow(DefenseTypeSnapshot entry)
    {
        return new DefenseTypeSnapshot
        {
            Type = entry.Type, Name = entry.Name, BaseMultiplier = entry.BaseMultiplier, Multiplier = entry.Multiplier,
            PhysicalMin = entry.Multiplier, PhysicalMax = entry.Multiplier,
            SpecialMin = entry.Multiplier, SpecialMax = entry.Multiplier,
            TypePhysicalMin = entry.Multiplier, TypePhysicalMax = entry.Multiplier,
            TypeSpecialMin = entry.Multiplier, TypeSpecialMax = entry.Multiplier,
            Adjustments = [], Exceptions = entry.Exceptions
        };
    }

    /// <summary>
    /// Rejects incomplete, unbounded, or inconsistent rule mechanics before numeric composition.
    /// </summary>
    /// <param name="rule">The selected rule fact to validate.</param>
    /// <returns>Whether the fact has exactly one finite factor representation and matching public metadata.</returns>
    private static bool ValidRule(DefenseDamageRuleFactSnapshot rule)
    {
        if (rule is null || rule.RuleIdentity < 0 || rule.Snapshot is null || rule.Types is null || rule.Types.Count > DefenseDamageProfileFactsSnapshot.MaximumTypes
            || rule.Types.Any(string.IsNullOrEmpty)
            || rule.Snapshot.Id is null || rule.Snapshot.Source is null || rule.Snapshot.Description is null || rule.Snapshot.Summary is null
            || rule.Snapshot.AttackTypes is null || rule.Snapshot.Moves is null || rule.Active != rule.Snapshot.Active
            || !rule.Types.SequenceEqual(rule.Snapshot.AttackTypes) || rule.HasMoves != (rule.Snapshot.Moves.Count > 0)
            || rule.Factor.HasValue == (rule.TypeFactors is not null))
        {
            return false;
        }


        if (rule.Factor is double factor && !double.IsFinite(factor))
            return false;

        if (rule.TypeFactors is { } factors && (factors.Count > DefenseDamageProfileFactsSnapshot.MaximumTypes || factors.Any(pair => string.IsNullOrEmpty(pair.Key) || !double.IsFinite(pair.Value))))
            return false;

        bool global = rule.Active != false && !rule.Conditional && rule.Types.Count == 0 && rule.Factor is double scalar && scalar != 1 && !rule.HasMoves;
        return rule.Global == global;
    }

    /// <summary>
    /// Checks repeated identity facts describe the same rule rather than merging distinct public rules.
    /// </summary>
    /// <param name="first">The first occurrence of an identity.</param>
    /// <param name="second">The repeated occurrence of that identity.</param>
    /// <returns>Whether their mechanics and adjustment snapshots are identical.</returns>
    private static bool SameRule(DefenseDamageRuleFactSnapshot first, DefenseDamageRuleFactSnapshot second)
    {
        return first.Active == second.Active && first.Category == second.Category && first.Conditional == second.Conditional && first.Global == second.Global
            && first.HasMoves == second.HasMoves && first.Factor == second.Factor && first.Types.SequenceEqual(second.Types)
            && SameFactors(first.TypeFactors, second.TypeFactors) && first.Snapshot.Id == second.Snapshot.Id && first.Snapshot.Source == second.Snapshot.Source
            && first.Snapshot.Description == second.Snapshot.Description && first.Snapshot.Summary == second.Snapshot.Summary && first.Snapshot.Moves.SequenceEqual(second.Snapshot.Moves);
    }

    /// <summary>
    /// Compares optional per-type factors exactly for repeated rule identities.
    /// </summary>
    /// <param name="first">The first optional factor map.</param>
    /// <param name="second">The second optional factor map.</param>
    /// <returns>Whether both maps are absent or have the same type keys and double values.</returns>
    private static bool SameFactors(IReadOnlyDictionary<string, double>? first, IReadOnlyDictionary<string, double>? second)
        => first is null ? second is null : second is not null && first.Count == second.Count && first.All(pair => second.TryGetValue(pair.Key, out double value) && pair.Value == value);

    /// <summary>
    /// Detects partially supplied numeric rows that cannot safely be treated as deferred input.
    /// </summary>
    /// <param name="entry">The decoded type row.</param>
    /// <returns>Whether any combined range value is present without a complete native profile.</returns>
    private static bool HasPartialProfiles(DefenseTypeSnapshot entry)
        => entry.PhysicalMin.HasValue || entry.PhysicalMax.HasValue || entry.SpecialMin.HasValue || entry.SpecialMax.HasValue || entry.TypePhysicalMin.HasValue || entry.TypePhysicalMax.HasValue || entry.TypeSpecialMin.HasValue || entry.TypeSpecialMax.HasValue;

    /// <summary>
    /// Multiplies one category's applicable factors in the game's original rule order.
    /// </summary>
    /// <param name="multiplier">The current native matchup multiplier.</param>
    /// <param name="attack">The incoming attack type identifier.</param>
    /// <param name="category">The physical or special damage category.</param>
    /// <param name="rules">The ordered numeric rule facts.</param>
    /// <param name="excludeGlobal">Whether separately displayed all-type rules are omitted.</param>
    /// <param name="adjustments">The lazy collector shared by full physical and special passes; type-local passes leave it unchanged.</param>
    /// <returns>The combined minimum and maximum relative damage factors.</returns>
    private static (double Min, double Max) Compose(double multiplier, string attack, string category, IReadOnlyList<DefenseDamageRuleFactSnapshot> rules, bool excludeGlobal, ref AdjustmentCollector adjustments)
    {
        double low = multiplier;
        double high = multiplier;
        foreach (DefenseDamageRuleFactSnapshot rule in rules)
        {
            if (rule.Active == false || excludeGlobal && rule.Global || rule.Category is not null && rule.Category != category || rule.Types.Count > 0 && !rule.Types.Contains(attack, StringComparer.Ordinal))
                continue;

            double? factor = rule.Factor;
            if (rule.TypeFactors is not null)
                factor = rule.TypeFactors.TryGetValue(attack, out double value) ? value : null;

            if (factor is not double number || number == 1 || high == 0)
                continue;

            bool unknown = rule.Conditional || rule.Active is null;
            low *= unknown ? Math.Min(number, 1.0) : number;
            high *= unknown ? Math.Max(number, 1.0) : number;
            if (!excludeGlobal)
                adjustments.Add(rule);
        }

        return (low, high);
    }

    /// <summary>
    /// Preserves the game's double result when exposing it through the existing decimal output contract.
    /// </summary>
    /// <param name="value">The composed IEEE double value.</param>
    /// <returns>The decimal parsed from invariant round-trip text rather than a rounded double cast.</returns>
    /// <exception cref="FormatException">Thrown when a non-finite result has no decimal representation.</exception>
    /// <exception cref="OverflowException">Thrown when the result exceeds the decimal output range.</exception>
    private static decimal ToDecimal(double value)
        => decimal.Parse(value.ToString(RoundTripFormat, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>
    /// Collects adjustment snapshots lazily while preserving rule-object identities across category passes.
    /// </summary>
    /// <remarks>
    /// A default collector allocates no collections. Its first applied rule creates the identity set and snapshot list.
    /// </remarks>
    private struct AdjustmentCollector
    {
        private List<DefenseRuleSnapshot>? _snapshots;
        private HashSet<int>? _identities;

        /// <summary>
        /// Gets whether any numeric rule applied to either complete category profile.
        /// </summary>
        internal readonly bool HasRules => _snapshots is not null;

        /// <summary>
        /// Gets the ordered public adjustment snapshots, or an empty list when no rule applied.
        /// </summary>
        internal readonly IReadOnlyList<DefenseRuleSnapshot> Snapshots => _snapshots ?? [];

        /// <summary>
        /// Adds the first occurrence of a rule identity without merging distinct same-ID rules.
        /// </summary>
        /// <param name="rule">The applied rule whose identity and public snapshot are recorded.</param>
        internal void Add(DefenseDamageRuleFactSnapshot rule)
        {
            _identities ??= [];
            if (_identities.Add(rule.RuleIdentity))
            {
                _snapshots ??= [];
                _snapshots.Add(rule.Snapshot);
            }
        }
    }
}
