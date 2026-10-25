using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ironmon.Tracker.Collection.Reduction;

/// <summary>
/// Retains bounded battle evidence and folds complete terminal relationships without an event archive.
/// </summary>
public sealed partial class CollectionEventReducer
{
    /// <summary>
    /// Gets a named object within bounded causal state.
    /// </summary>
    /// <param name="parent">The bounded mutable state object that owns the named child.</param>
    /// <param name="key">The child property name to retrieve or initialize as an object.</param>
    /// <returns>The existing child object, or a new object attached to the parent under the requested key.</returns>
    private static JsonObject EvidenceObject(JsonObject parent, string key)
    {
        if (parent[key] is not JsonObject value)
        {
            value = new JsonObject();
            parent[key] = value;
        }

        return value;
    }

    /// <summary>
    /// Returns one actor's stable appearance identity without using a reusable battler slot.
    /// </summary>
    /// <param name="actor">The source actor reference whose appearance identity is preferred over reusable battler slots.</param>
    /// <returns>The first available appearance, Pokemon instance or actor identity, or null when none was disclosed.</returns>
    private static string? EvidenceActorId(JsonElement actor)
        => Text(actor, CollectionTokens.AppearanceId) ?? Text(actor, CollectionTokens.PokemonInstanceId) ?? Text(actor, CollectionTokens.ActorId);

    /// <summary>
    /// Copies one actual source field into bounded state.
    /// </summary>
    /// <param name="destination">The bounded state object receiving an independent copy.</param>
    /// <param name="key">The exact destination property name.</param>
    /// <param name="value">The source JSON value; an undefined value is retained as JSON null.</param>
    private static void EvidenceCopy(JsonObject destination, string key, JsonElement value)
        => destination[key] = value.ValueKind == JsonValueKind.Undefined ? null : JsonNode.Parse(value.GetRawText());

    /// <summary>
    /// Increments an exact sparse histogram without retaining its source events.
    /// </summary>
    /// <param name="histogram">The sparse exact-count histogram to update.</param>
    /// <param name="key">The observed value identifying the histogram bucket.</param>
    /// <param name="amount">The arbitrary-precision count to add to that bucket.</param>
    private static void EvidenceIncrement(JsonObject histogram, string key, BigInteger amount)
    {
        histogram[key] = (ReadNodeBigInteger(histogram[key]) + amount).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Retains the lowest observed HP fraction using each observation's actual maximum HP.
    /// </summary>
    /// <param name="row">The bounded actor evidence receiving the exact fraction.</param>
    /// <param name="hp">Observed current HP, or an unavailable value.</param>
    /// <param name="maximum">The maximum HP applicable to that observation.</param>
    private static void ObserveMinimumHpRatio(JsonObject row, JsonElement hp, JsonElement maximum)
    {
        if (Integer(hp) is not BigInteger actual || Integer(maximum) is not BigInteger max || actual < 0 || max <= 0)
            return;

        if (actual > 0 && row[CollectionBattleKeys.MinHpRatioIncomplete]?.GetValue<bool>() == true)
            return;

        JsonNode? prior = row[CollectionBattleKeys.MinHpRatio];
        if (prior is JsonObject && actual * ReadNodeBigInteger(prior[CollectionBattleKeys.Denominator]) >= ReadNodeBigInteger(prior[CollectionBattleKeys.Numerator]) * max)
            return;

        BigInteger divisor = BigInteger.GreatestCommonDivisor(actual, max);
        row[CollectionBattleKeys.MinHpRatio] = new JsonObject
        {
            [CollectionBattleKeys.Numerator] = (actual / divisor).ToString(CultureInfo.InvariantCulture),
            [CollectionBattleKeys.Denominator] = (max / divisor).ToString(CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Accumulates exact distributions, current episodes, source totals and the last terminal application.
    /// </summary>
    /// <param name="type">The actual source event type selecting the evidence to fold.</param>
    /// <param name="envelope">The ordered source envelope identifying the actual attempt, battle and sequence.</param>
    /// <param name="payload">The actual source payload to inspect or fold into sufficient state.</param>
    private void ObserveAdvancedBattle(string type, JsonElement envelope, JsonElement payload)
    {
        string? battleId = Text(envelope, _battleKey);
        if (battleId is null || StateMap(_battlesState)[battleId] is not JsonObject battle || Bool(Path(envelope, CollectionBattleKeys.ContextCombatCleanup)) == true || Text(payload, CollectionBattleKeys.Lifecycle) == CollectionBattleKeys.Cleanup)
            return;

        JsonObject evidence = EvidenceObject(battle, CollectionBattleKeys.SourceEvidence);
        JsonObject actors = EvidenceObject(evidence, CollectionBattleKeys.Actors);
        var propertyKey = type is _eventHealing or _eventPpRestored
            ? CollectionBattleKeys.Recipient
            : type is _eventHpLoss or _eventSubstituteChanged or _eventHitResolved or CollectionBattleKeys.BattlerFainted or CollectionBattleKeys.StatusMajorApplicationResolved or CollectionBattleKeys.StatusMajorCureResolved or CollectionBattleKeys.VolatileApplicationResolved or CollectionBattleKeys.VolatileRemovalResolved
                ? CollectionBattleKeys.Target
                : CollectionBattleKeys.Actor;

        JsonElement actor = Property(payload, propertyKey);
        string? actorId = EvidenceActorId(actor);
        if (actorId is null)
            return;

        JsonObject row = EvidenceObject(actors, actorId);
        EvidenceCopy(row, CollectionBattleKeys.Actor, actor);
        string? sequence = Text(envelope, CollectionBattleKeys.Sequence) ?? Text(payload, CollectionBattleKeys.Sequence);
        if (type == CollectionBattleKeys.BattlerEntered)
        {
            JsonElement snapshot = Property(payload, CollectionBattleKeys.EntrySnapshot);
            EvidenceCopy(row, CollectionBattleKeys.Entry, snapshot);
            EvidenceCopy(row, CollectionBattleKeys.Snapshot, snapshot);
            row[CollectionBattleKeys.MinimumHp] = JsonNode.Parse(Property(snapshot, CollectionBattleKeys.Hp).GetRawText());
            row[CollectionBattleKeys.MaximumHp] = JsonNode.Parse(Property(snapshot, CollectionBattleKeys.Hp).GetRawText());
            row[CollectionBattleKeys.FirstFloorSequence] = sequence;
            ObserveMinimumHpRatio(row, Property(snapshot, CollectionBattleKeys.Hp), Property(snapshot, CollectionBattleKeys.MaxHp));
        }

        if (type is CollectionBattleKeys.BattlerLeft or CollectionBattleKeys.BattlerFainted)
        {
            JsonElement snapshot = Property(payload, type == CollectionBattleKeys.BattlerLeft ? CollectionBattleKeys.ExitSnapshot : CollectionBattleKeys.PreCleanupSnapshot);
            if (snapshot.ValueKind == JsonValueKind.Object)
            {
                EvidenceCopy(row, CollectionBattleKeys.Snapshot, snapshot);
                ObserveMinimumHpRatio(row, Property(snapshot, CollectionBattleKeys.Hp), Property(snapshot, CollectionBattleKeys.MaxHp));
            }
        }

        if (type is _eventHpLoss or _eventHealing)
        {
            JsonElement hp = Property(payload, CollectionBattleKeys.HpAfter);
            JsonElement maximumHp = Property(payload, CollectionBattleKeys.MaxHp);
            ObserveMinimumHpRatio(row, Property(payload, CollectionBattleKeys.HpBefore), maximumHp);
            ObserveMinimumHpRatio(row, hp, maximumHp);
            if (Integer(hp) is not null)
            {
                JsonObject latest = EvidenceObject(row, CollectionBattleKeys.Snapshot);
                EvidenceCopy(latest, CollectionBattleKeys.Hp, hp);
                EvidenceCopy(latest, CollectionBattleKeys.MaxHp, maximumHp);
            }

            JsonObject distribution = EvidenceObject(row, CollectionBattleKeys.HpDistribution);
            if (Integer(hp) is BigInteger actual)
            {
                if (row[CollectionBattleKeys.MinimumHp] is null || actual < ReadNodeBigInteger(row[CollectionBattleKeys.MinimumHp]))
                {
                    row[CollectionBattleKeys.MinimumHp] = actual.ToString(CultureInfo.InvariantCulture);
                    row[CollectionBattleKeys.FirstFloorSequence] = sequence;
                }

                if (row[CollectionBattleKeys.MaximumHp] is null || actual > ReadNodeBigInteger(row[CollectionBattleKeys.MaximumHp]))
                {
                    row[CollectionBattleKeys.MaximumHp] = actual.ToString(CultureInfo.InvariantCulture);
                    row[CollectionBattleKeys.MaximumSequence] = sequence;
                }

                if (type == _eventHpLoss)
                {
                    EvidenceIncrement(EvidenceObject(distribution, CollectionBattleKeys.PostLossHp), actual.ToString(CultureInfo.InvariantCulture), BigInteger.One);
                    BigInteger? maximum = Integer(Property(payload, CollectionBattleKeys.MaxHp));
                    if (maximum > 0)
                    {
                        BigInteger divisor = BigInteger.GreatestCommonDivisor(actual, maximum.Value);
                        EvidenceIncrement(EvidenceObject(distribution, CollectionBattleKeys.PostLossRatio), (actual / divisor).ToString(CultureInfo.InvariantCulture) + CollectionBattleKeys.Slash + (maximum.Value / divisor).ToString(CultureInfo.InvariantCulture), BigInteger.One);
                    }

                    EvidenceCopy(row, CollectionBattleKeys.LastLoss, payload);
                    row[CollectionBattleKeys.LastLoss]![CollectionBattleKeys.SourceSequence] = sequence;
                }
                else if (Integer(Property(payload, CollectionBattleKeys.ActualRestoredHp)) > 0)
                {
                    string start = Integer(Property(payload, CollectionBattleKeys.HpBefore))?.ToString(CultureInfo.InvariantCulture) ?? CollectionBattleKeys.Unknown;
                    EvidenceIncrement(EvidenceObject(distribution, CollectionBattleKeys.RecoveryFromHp), start, BigInteger.One);
                    JsonObject floor = EvidenceObject(EvidenceObject(row, CollectionBattleKeys.RecoveryOrder), ReadNodeBigInteger(row[CollectionBattleKeys.MinimumHp]).ToString(CultureInfo.InvariantCulture));
                    floor[CollectionBattleKeys.PositiveRecoveryCount] = (ReadNodeBigInteger(floor[CollectionBattleKeys.PositiveRecoveryCount]) + 1).ToString(CultureInfo.InvariantCulture);
                    floor[CollectionBattleKeys.FirstFloorSequence] ??= row[CollectionBattleKeys.FirstFloorSequence]?.DeepClone();
                    floor[CollectionBattleKeys.FirstRecoverySequence] ??= sequence;
                    floor[CollectionBattleKeys.RecoveryStartHp] ??= JsonNode.Parse(Property(payload, CollectionBattleKeys.HpBefore).GetRawText());
                    floor[CollectionBattleKeys.RecoverySourceKind] ??= JsonNode.Parse(Path(payload, CollectionTokens.EffectSourceKind).GetRawText());
                    floor[CollectionBattleKeys.LastFloorSequence] = row[CollectionBattleKeys.FirstFloorSequence]?.DeepClone();
                    floor[CollectionBattleKeys.LastRecoverySequence] = sequence;
                }
            }
        }

        if (type is _eventHpLoss or _eventSubstituteChanged)
        {
            JsonElement cause = Property(payload, CollectionBattleKeys.Cause);
            string? role = Text(Path(cause, CollectionBattleKeys.OriginInstigator), CollectionBattleKeys.OwnerRole) ?? Text(Path(cause, CollectionBattleKeys.SourceActor), CollectionBattleKeys.OwnerRole);
            BigInteger? amount = Integer(Property(payload, type == _eventHpLoss ? CollectionBattleKeys.ActualHpLost : CollectionBattleKeys.ActualSubstituteHpLost));
            if (role is not null && role != Text(actor, CollectionBattleKeys.OwnerRole) && amount > 0)
            {
                JsonObject totals = EvidenceObject(EvidenceObject(evidence, CollectionBattleKeys.DamageTotals), role);
                string pool = type == _eventHpLoss ? CollectionBattleKeys.PokemonHpDamage : CollectionBattleKeys.SubstituteHpDamage;
                EvidenceIncrement(totals, pool, amount.Value);
                EvidenceIncrement(totals, Text(payload, CollectionBattleKeys.LossKind) is CollectionBattleKeys.MoveHit or CollectionBattleKeys.Delayed || type == _eventSubstituteChanged ? CollectionBattleKeys.DirectDamage : CollectionBattleKeys.IndirectDamage, amount.Value);
                EvidenceIncrement(totals, CollectionBattleKeys.TotalEligibleDamage, amount.Value);
            }
        }

        if (type == _eventHitResolved)
        {
            EvidenceCopy(row, CollectionBattleKeys.LastHit, payload);
            string? action = Text(Property(envelope, _contextKey), _actionKey);
            if (action is not null && StateMap(_actionsState)[action] is JsonObject actionState)
                row[CollectionBattleKeys.LastPriority] = actionState[CollectionBattleKeys.PriorityContext]?.DeepClone();
        }

        if (type == CollectionBattleKeys.BattlerFainted)
            EvidenceCopy(row, CollectionBattleKeys.Faint, payload);

        if (type == CollectionBattleKeys.StatusMajorApplicationResolved && Text(payload, CollectionTokens.ApplicationOutcome) == CollectionTokens.Applied)
            EvidenceCopy(EvidenceObject(row, CollectionBattleKeys.Snapshot), CollectionBattleKeys.StatusId, Property(payload, CollectionTokens.StatusAfterApplication));

        if (type == CollectionBattleKeys.StatusMajorCureResolved && Text(payload, CollectionTokens.CureOutcome) == CollectionTokens.Cured)
            EvidenceCopy(EvidenceObject(row, CollectionBattleKeys.Snapshot), CollectionBattleKeys.StatusId, Property(payload, CollectionTokens.StatusAfter));

        if (type is CollectionBattleKeys.StatusMajorApplicationResolved or CollectionBattleKeys.VolatileApplicationResolved)
        {
            bool major = type == CollectionBattleKeys.StatusMajorApplicationResolved;
            string? identifier = Text(payload, major ? CollectionBattleKeys.StatusRequested : CollectionBattleKeys.EffectId);
            if (identifier is not null)
            {
                JsonObject episode = new()
                {
                    [CollectionBattleKeys.EpisodeKind] = major ? CollectionBattleKeys.MajorStatus : CollectionBattleKeys.Volatile, [CollectionBattleKeys.StatusOrEffectId] = identifier,
                    [CollectionBattleKeys.Mode] = major ? Text(payload, CollectionBattleKeys.StatusMode) : CollectionBattleKeys.NotApplicable, [CollectionBattleKeys.ApplicationSourceOwnerRole] = Text(Path(payload, CollectionBattleKeys.EffectSourceSourceActor), CollectionBattleKeys.OwnerRole) ?? Text(Path(payload, CollectionBattleKeys.EffectSourceActor), CollectionBattleKeys.OwnerRole),
                    [CollectionBattleKeys.ApplicationMechanicId] = Text(Property(payload, CollectionBattleKeys.EffectSource), CollectionBattleKeys.MechanicId) ?? Text(Property(payload, CollectionBattleKeys.EffectSource), CollectionBattleKeys.CanonicalId),
                    [CollectionBattleKeys.TargetOwnerRole] = Text(actor, CollectionBattleKeys.OwnerRole), [CollectionBattleKeys.EpisodeId] = Text(payload, major ? CollectionBattleKeys.StatusEpisodeId : CollectionBattleKeys.EffectEpisodeId), [CollectionBattleKeys.Count] = 1
                };

                EvidenceObject(row, CollectionBattleKeys.ActiveEpisodes)[major ? CollectionBattleKeys.Major : identifier] = episode;
            }
        }

        if (type is CollectionBattleKeys.StatusMajorCureResolved or CollectionBattleKeys.VolatileRemovalResolved)
        {
            string key = type == CollectionBattleKeys.StatusMajorCureResolved ? CollectionBattleKeys.Major : Text(payload, CollectionBattleKeys.EffectId) ?? CollectionBattleKeys.Unknown;
            JsonObject active = EvidenceObject(row, CollectionBattleKeys.ActiveEpisodes);
            if (active[key] is JsonObject episode)
            {
                episode[CollectionBattleKeys.ResolutionKind] = Text(payload, type == CollectionBattleKeys.StatusMajorCureResolved ? CollectionBattleKeys.CureReason : CollectionBattleKeys.RemovalKind);
                episode[CollectionBattleKeys.FaintRelation] = CollectionBattleKeys.None;
                EvidenceObject(row, CollectionBattleKeys.ResolvedEpisodes)[episode.ToJsonString()] = episode.DeepClone();
                active.Remove(key);
            }
        }
    }

    /// <summary>
    /// Normalizes all terminal joins and retains only sufficient context for a following attempt completion.
    /// </summary>
    /// <param name="battle">The completed battle's bounded source evidence and terminal snapshots.</param>
    /// <param name="envelope">The ordered source envelope identifying the actual attempt, battle and sequence.</param>
    /// <param name="actors">The per-actor summary rows enriched with exact health, episode and terminal relationships.</param>
    /// <param name="summary">The finalized sufficient summary receiving the derived relationships and distributions.</param>
    private void CompleteAdvancedSummary(JsonObject battle, JsonElement envelope, JsonArray actors, JsonObject summary)
    {
        JsonObject evidence = EvidenceObject(battle, CollectionBattleKeys.SourceEvidence);
        JsonObject actorEvidence = EvidenceObject(evidence, CollectionBattleKeys.Actors);
        JsonElement terminal = Property(Element(battle), CollectionBattleKeys.FinalActorStates);
        JsonArray effects = [], episodes = [], damage = [];
        JsonObject distributions = [], recovery = [], causes = [];
        if (terminal.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement snapshot in terminal.EnumerateArray())
            {
                string? id = EvidenceActorId(Property(snapshot, CollectionBattleKeys.Actor));
                if (id is not null)
                {
                    JsonObject row = EvidenceObject(actorEvidence, id);
                    EvidenceCopy(row, CollectionBattleKeys.Snapshot, snapshot);
                    ObserveMinimumHpRatio(row, Property(snapshot, CollectionBattleKeys.Hp), Property(snapshot, CollectionBattleKeys.MaxHp));
                }
            }
        }

        foreach (JsonObject actor in actors.OfType<JsonObject>())
        {
            JsonElement actorRef = Property(Element(actor), CollectionBattleKeys.Actor);
            string? id = EvidenceActorId(actorRef);
            if (id is null)
                continue;

            JsonObject row = EvidenceObject(actorEvidence, id);
            JsonElement snapshot = row[CollectionBattleKeys.Snapshot] is JsonObject final ? Element(final) : Null();
            actor[CollectionBattleKeys.IsUsableRunner] = Bool(Property(actorRef, CollectionBattleKeys.UsableRunner));
            actor[CollectionBattleKeys.EligibleRunnerRole] = Bool(Property(actorRef, CollectionBattleKeys.UsableRunner)) == true ? CollectionBattleKeys.Runner : CollectionBattleKeys.OtherRetainedTeam;
            actor[CollectionBattleKeys.ParticipationKind] = CollectionBattleKeys.Entered;
            if (Available(Property(snapshot, CollectionBattleKeys.Hp)))
            {
                EvidenceCopy(actor, CollectionBattleKeys.ExitHp, Property(snapshot, CollectionBattleKeys.Hp));
                EvidenceCopy(actor, CollectionBattleKeys.ExitMaxHp, Property(snapshot, CollectionBattleKeys.MaxHp));
                if (Available(Property(snapshot, CollectionBattleKeys.StatusId)))
                    EvidenceCopy(actor, CollectionBattleKeys.TerminalMajorStatus, Property(snapshot, CollectionBattleKeys.StatusId));
            }

            actor[CollectionTokens.TerminalHp] = actor[CollectionBattleKeys.ExitHp]?.DeepClone();
            actor[CollectionBattleKeys.MinHpRatio] = row[CollectionBattleKeys.MinHpRatio]?.DeepClone();

            actor[CollectionBattleKeys.ObservedFloorHp] = row[CollectionBattleKeys.MinimumHp]?.DeepClone();
            actor[CollectionBattleKeys.RecoveryStartHp] = actor[CollectionTokens.LastRecoveryStartHp]?.DeepClone();
            actor[CollectionBattleKeys.FloorBeforeRecovery] = actor[CollectionTokens.ObservedFloorBeforeRecovery]?.DeepClone();
            actor[CollectionBattleKeys.RecoverySourceKind] = actor[CollectionTokens.LastRecoverySourceKind]?.DeepClone();
            distributions[id] = row[CollectionBattleKeys.HpDistribution]?.DeepClone();
            recovery[id] = row[CollectionBattleKeys.RecoveryOrder]?.DeepClone();
            JsonObject distribution = EvidenceObject(row, CollectionBattleKeys.HpDistribution);
            distribution[CollectionBattleKeys.MinObservation] = new JsonObject { [CollectionBattleKeys.Hp] = row[CollectionBattleKeys.MinimumHp]?.DeepClone(), [CollectionBattleKeys.Sequence] = row[CollectionBattleKeys.FirstFloorSequence]?.DeepClone() };
            distribution[CollectionBattleKeys.MaxObservation] = new JsonObject { [CollectionBattleKeys.Hp] = row[CollectionBattleKeys.MaximumHp]?.DeepClone(), [CollectionBattleKeys.Sequence] = row[CollectionBattleKeys.MaximumSequence]?.DeepClone() };
            distributions[id] = distribution.DeepClone();
            actor[CollectionBattleKeys.HpDistribution] = distribution.DeepClone();
            actor[CollectionBattleKeys.RecoveryOrderedHpSummary] = row[CollectionBattleKeys.RecoveryOrder]?.DeepClone();
            if (row[CollectionBattleKeys.Faint] is JsonObject faint)
                causes[id] = ActorTerminalEvidence(row, battle);


            foreach ((string key, JsonNode? value) in ActorTerminalEvidence(row, battle))
                actor[key] = value?.DeepClone();

            JsonElement activeEffects = Property(snapshot, CollectionBattleKeys.ActiveEffects);
            if (activeEffects.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement effect in activeEffects.EnumerateArray())
                {
                    JsonObject item = JsonNode.Parse(effect.GetRawText())!.AsObject();
                    item[CollectionBattleKeys.TargetOwnerRole] = Text(actorRef, CollectionBattleKeys.OwnerRole);
                    item[CollectionBattleKeys.TargetHp] = actor[CollectionBattleKeys.ExitHp]?.DeepClone();
                    item[CollectionBattleKeys.TargetMaxHp] = actor[CollectionBattleKeys.ExitMaxHp]?.DeepClone();
                    item[CollectionBattleKeys.Outcome] = battle[_outcomeKey]?.DeepClone();
                    item[CollectionBattleKeys.BattleKind] = battle[CollectionTokens.BattleKind]?.DeepClone();
                    item[CollectionBattleKeys.Count] = 1;
                    effects.Add(item);
                }
            }

            foreach (JsonObject episode in EvidenceObject(row, CollectionBattleKeys.ResolvedEpisodes).Select(pair => pair.Value).OfType<JsonObject>().Concat(EvidenceObject(row, CollectionBattleKeys.ActiveEpisodes).Select(pair => pair.Value).OfType<JsonObject>()))
            {
                JsonObject item = (JsonObject)episode.DeepClone();
                item[CollectionBattleKeys.ResolutionKind] ??= row[CollectionBattleKeys.Faint] is null ? CollectionBattleKeys.ActiveAtBattleEnd : CollectionBattleKeys.Fainted;
                item[CollectionBattleKeys.FaintRelation] ??= row[CollectionBattleKeys.Faint] is null ? CollectionBattleKeys.None : CollectionBattleKeys.ActiveSameEpisode;
                JsonNode? terminalEpisode = row[CollectionBattleKeys.Faint]?[CollectionBattleKeys.Cause]?[CollectionBattleKeys.StatusEpisodeId] ?? row[CollectionBattleKeys.Faint]?[CollectionBattleKeys.Cause]?[CollectionBattleKeys.EffectEpisodeId];
                if (terminalEpisode is not null && JsonNode.DeepEquals(terminalEpisode, episode[CollectionBattleKeys.EpisodeId]))
                    item[CollectionBattleKeys.FaintRelation] = CollectionBattleKeys.CausedBySameEpisode;

                item[CollectionBattleKeys.FaintCauseKind] = row[CollectionBattleKeys.Faint]?[CollectionBattleKeys.Cause]?[CollectionBattleKeys.Kind]?.DeepClone();
                item[CollectionBattleKeys.BattleKind] = battle[CollectionTokens.BattleKind]?.DeepClone();
                item[CollectionBattleKeys.Outcome] = battle[_outcomeKey]?.DeepClone();
                episodes.Add(item);
            }
        }

        foreach ((string role, JsonNode? value) in EvidenceObject(evidence, CollectionBattleKeys.DamageTotals))
        {
            JsonObject item = value?.DeepClone().AsObject() ?? [];
            item[CollectionBattleKeys.SourceOwnerRole] = role;
            item[CollectionBattleKeys.Coverage] = CollectionBattleKeys.Known;
            foreach (string key in new[] { CollectionBattleKeys.PokemonHpDamage, CollectionBattleKeys.SubstituteHpDamage, CollectionBattleKeys.DirectDamage, CollectionBattleKeys.IndirectDamage, CollectionBattleKeys.TotalEligibleDamage })
                item[key] ??= CollectionBattleKeys.Value0;

            damage.Add(item);
        }

        summary[CollectionBattleKeys.OutcomeRef] = battle[CollectionBattleKeys.EventId]?.DeepClone();
        summary[CollectionBattleKeys.SourceEvidenceFolded] = true;
        summary[CollectionBattleKeys.HpDistribution] = distributions;
        summary[CollectionBattleKeys.RecoveryOrderedHpSummary] = recovery;
        summary[CollectionBattleKeys.TerminalCauseContext] = causes;
        summary[CollectionBattleKeys.AggregateDeltas] = new JsonArray();
        summary[CollectionBattleKeys.DedupWatermark] = new JsonObject { [CollectionBattleKeys.BattleId] = Text(envelope, _battleKey), [CollectionBattleKeys.Revision] = 1, [CollectionBattleKeys.LastSequence] = Text(envelope, CollectionBattleKeys.Sequence) };
        summary[CollectionBattleKeys.TerminalPopulationAndHealth] = new JsonObject { [CollectionBattleKeys.SideStates] = battle[CollectionBattleKeys.TerminalSideStates]?.DeepClone(), [CollectionBattleKeys.ActiveActorHp] = battle[CollectionBattleKeys.FinalActorStates]?.DeepClone() };
        summary[CollectionBattleKeys.TerminalEffectStateJoints] = effects;
        summary[CollectionBattleKeys.StatusEpisodeOutcomeJoints] = episodes;
        summary[CollectionBattleKeys.DamageTotalsBySourceRole] = damage;
        summary[CollectionTokens.ActorHealthOutcomeJoints] = actors.DeepClone();
        summary[CollectionTokens.CompoundContext] = actors.DeepClone();
        JsonObject attempt = AttemptState(Text(envelope, _attemptKey));
        summary[CollectionBattleKeys.OrderedBagRecoveryStreakState] = new JsonObject { [CollectionBattleKeys.AttemptInstanceId] = Text(envelope, _attemptKey), [CollectionBattleKeys.CurrentConsecutiveTrainerWins] = ReadNodeInteger(attempt[_currentKey]) + (Text(Element(battle), CollectionTokens.BattleKind) == _trainerKind && Text(Element(battle), _outcomeKey) == _winOutcome ? 1 : 0), [CollectionBattleKeys.BestConsecutiveTrainerWins] = ReadNodeInteger(attempt[_bestKey]), [CollectionBattleKeys.LastFoldedEventSequence] = Text(envelope, CollectionBattleKeys.Sequence), [CollectionBattleKeys.PendingBattleId] = null };
        attempt[CollectionBattleKeys.TerminalContext] = BuildTerminalContext(battle, actorEvidence, envelope);
    }

    /// <summary>
    /// Builds exact deferred run-completion dimensions from the engine terminal snapshot and last applications.
    /// </summary>
    /// <param name="battle">The battle state containing its actual terminal decision and pre-cleanup population.</param>
    /// <param name="actors">The actor evidence keyed by stable appearance identity.</param>
    /// <param name="envelope">The ordered source envelope identifying the actual attempt, battle and sequence.</param>
    /// <returns>The deferred terminal dimensions associated with this exact battle and its actual final decision.</returns>
    private JsonObject BuildTerminalContext(JsonObject battle, JsonObject actors, JsonElement envelope)
    {
        JsonObject result = new() { [CollectionBattleKeys.BattleId] = Text(envelope, _battleKey), [CollectionBattleKeys.EngineDecision] = battle[CollectionBattleKeys.EngineDecision]?.DeepClone(), [CollectionBattleKeys.RawBattleDecision] = battle[CollectionBattleKeys.EngineDecision]?.DeepClone(), [CollectionBattleKeys.BattleKind] = battle[CollectionTokens.BattleKind]?.DeepClone() };
        result[CollectionBattleKeys.Encounter] = battle[CollectionBattleKeys.Encounter]?.DeepClone();
        result[CollectionBattleKeys.AuthoredEntryIds] = battle[CollectionBattleKeys.AuthoredEntryIds]?.DeepClone() ?? battle[CollectionBattleKeys.Encounter]?[CollectionBattleKeys.AuthoredEntryIds]?.DeepClone();
        result[CollectionBattleKeys.TrainerClass] = battle[CollectionBattleKeys.Encounter]?[CollectionBattleKeys.TrainerClassIds]?.DeepClone();
        JsonElement sides = Property(Element(battle), CollectionBattleKeys.TerminalSideStates);
        if (sides.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement side in sides.EnumerateArray())
            {
                bool player = Integer(Path(side, CollectionBattleKeys.SideSideId)) == 0;
                EvidenceCopy(result, player ? CollectionBattleKeys.PlayerAbleCount : CollectionBattleKeys.OpponentAbleCount, Property(side, CollectionBattleKeys.EngineAblePartyCount));
                EvidenceCopy(result, player ? CollectionBattleKeys.PlayerReserveAbleCount : CollectionBattleKeys.OpponentReserveAbleCount, Property(side, CollectionBattleKeys.ReserveAbleCount));
            }
        }

        foreach (JsonObject row in actors.Select(pair => pair.Value).OfType<JsonObject>())
        {
            JsonElement actor = Property(Element(row), CollectionBattleKeys.Actor);
            bool runner = Bool(Property(actor, CollectionBattleKeys.UsableRunner)) == true;
            if (!runner)
                continue;

            foreach ((string key, JsonNode? value) in ActorTerminalEvidence(row, battle))
                result[key] = value?.DeepClone();

            result[CollectionBattleKeys.RunnerFainted] = row[CollectionBattleKeys.Faint] is not null;

        }

        result[CollectionBattleKeys.FinalOpponentFainted] = result[CollectionBattleKeys.OpponentAbleCount] is not null ? ReadNodeBigInteger(result[CollectionBattleKeys.OpponentAbleCount]) == 0 : null;
        JsonObject[] opponents = [.. actors.Select(pair => pair.Value).OfType<JsonObject>().Where(row => Text(Property(Element(row), CollectionBattleKeys.Actor), CollectionBattleKeys.OwnerRole) == _opponentRole)];
        JsonObject[] surviving = [.. opponents.Where(row => Integer(Path(Element(row), CollectionBattleKeys.SnapshotHp)) > 0)];
        if (ReadNodeBigInteger(result[CollectionBattleKeys.OpponentAbleCount]) == 1 && surviving.Length == 1)
            result[CollectionBattleKeys.TerminalSurvivingOpponentHp] = surviving[0][CollectionBattleKeys.Snapshot]?[CollectionBattleKeys.Hp]?.DeepClone();

        JsonObject? runnerRow = actors.Select(pair => pair.Value).OfType<JsonObject>().FirstOrDefault(row => Bool(Path(Element(row), CollectionBattleKeys.ActorUsableRunner)) == true);
        JsonObject? finalOpponent = opponents.LastOrDefault(row => row[CollectionBattleKeys.Faint] is not null);
        JsonNode? runnerAction = runnerRow?[CollectionBattleKeys.LastLoss]?[CollectionBattleKeys.Cause]?[CollectionBattleKeys.ActionId];
        JsonNode? opponentAction = finalOpponent?[CollectionBattleKeys.LastLoss]?[CollectionBattleKeys.Cause]?[CollectionBattleKeys.ActionId];
        result[CollectionBattleKeys.SameTerminalResolution] = runnerAction is not null && opponentAction is not null ? JsonNode.DeepEquals(runnerAction, opponentAction) : null;
        return result;
    }

    /// <summary>
    /// Indexes each distinct observed floor with a later real recovery once per actor and battle.
    /// </summary>
    /// <param name="battle">The battle state containing observed HP floors and later positive recovery.</param>
    /// <param name="envelope">The ordered source envelope identifying the actual attempt, battle and sequence.</param>
    /// <param name="payload">The finalized battle summary used as projection context.</param>
    /// <param name="output">Receives directly indexed projections for the observed relationship.</param>
    private void ProjectRecoveryDirect(JsonObject battle, JsonElement envelope, JsonElement payload, List<CollectionDirectProjection> output)
    {
        CollectionFamilyDefinition family = _catalogue.Families.Single(row => row.Key == CollectionBattleKeys.BattleCompoundMetrics);
        JsonObject actors = EvidenceObject(EvidenceObject(battle, CollectionBattleKeys.SourceEvidence), CollectionBattleKeys.Actors);
        foreach (JsonObject row in actors.Select(pair => pair.Value).OfType<JsonObject>())
        {
            foreach ((string floorHp, JsonNode? value) in EvidenceObject(row, CollectionBattleKeys.RecoveryOrder))
            {
                if (value is not JsonObject recovery)
                    continue;

                JsonObject observed = new()
                {
                    [CollectionBattleKeys.Actor] = row[CollectionBattleKeys.Actor]?.DeepClone(), [CollectionBattleKeys.ObservedFloorHp] = floorHp,
                    [CollectionBattleKeys.RecoveryStartHp] = recovery[CollectionBattleKeys.RecoveryStartHp]?.DeepClone(),
                    [CollectionBattleKeys.FloorBeforeRecovery] = recovery[CollectionBattleKeys.FirstFloorSequence] is not null && recovery[CollectionBattleKeys.FirstRecoverySequence] is not null ? ReadNodeBigInteger(recovery[CollectionBattleKeys.FirstFloorSequence]) < ReadNodeBigInteger(recovery[CollectionBattleKeys.FirstRecoverySequence]) : null,
                    [CollectionBattleKeys.IsUsableRunner] = row[CollectionBattleKeys.Actor]?[CollectionBattleKeys.UsableRunner]?.DeepClone(),
                    [CollectionBattleKeys.RecoverySourceKind] = recovery[CollectionBattleKeys.RecoverySourceKind]?.DeepClone(), [CollectionBattleKeys.Outcome] = battle[_outcomeKey]?.DeepClone()
                };

                Frame frame = CreateFrame(envelope, payload, Element(observed));
                ProjectDirect(family, [], [new CollectionMeasureUpdate(_occurrenceKey, _scalarKind, _sumOperator, _known, BigInteger.One, null, null)], frame, true, output, CollectionBattleKeys.RecoveryDirectGroup);
            }
        }
    }

    /// <summary>
    /// Joins a confirmed terminal HP application with its own hit and scheduled priority, never an older hit.
    /// </summary>
    /// <param name="row">One actor's actual last HP loss, matching hit, priority and confirmed faint evidence.</param>
    /// <param name="battle">The terminal battle state supplying battle kind and actual outcome.</param>
    /// <returns>Exact terminal dimensions; hit-only fields remain null when the last HP loss has no matching application.</returns>
    private JsonObject ActorTerminalEvidence(JsonObject row, JsonObject battle)
    {
        JsonObject result = [];
        JsonObject? loss = row[CollectionBattleKeys.LastLoss] as JsonObject;
        JsonObject? hit = row[CollectionBattleKeys.LastHit] as JsonObject;
        bool matched = loss?[CollectionBattleKeys.HitRef] is not null && JsonNode.DeepEquals(loss[CollectionBattleKeys.HitRef], hit?[CollectionBattleKeys.HitId]);
        JsonObject? application = matched ? hit?[CollectionBattleKeys.ApplicationSnapshot] as JsonObject : null;
        JsonObject? cause = loss?[CollectionBattleKeys.Cause] as JsonObject;
        bool confirmed = row[CollectionBattleKeys.Faint] is JsonObject faint && Integer(Property(Element(faint), CollectionBattleKeys.TerminalHpLossRef)) is BigInteger terminalSequence && terminalSequence == Integer(loss is null ? Null() : Property(Element(loss), CollectionBattleKeys.SourceSequence));
        result[CollectionBattleKeys.Cause] = cause?.DeepClone();
        result[CollectionBattleKeys.TerminalCauseKind] = cause?[CollectionBattleKeys.Kind]?.DeepClone();
        result[CollectionBattleKeys.SourceOwnerRole] = cause?[CollectionBattleKeys.OriginInstigator]?[CollectionBattleKeys.OwnerRole]?.DeepClone();
        result[CollectionBattleKeys.TargetOwnerRole] = row[CollectionBattleKeys.Actor]?[CollectionBattleKeys.OwnerRole]?.DeepClone();
        result[CollectionBattleKeys.MechanicalMoveId] = application?[CollectionBattleKeys.MechanicalMoveId]?.DeepClone() ?? cause?[CollectionBattleKeys.MechanicId]?.DeepClone();
        result[CollectionBattleKeys.TerminalCalculationKind] = application?[CollectionBattleKeys.CalculationKind]?.DeepClone();
        result[CollectionBattleKeys.Critical] = application?[CollectionBattleKeys.Critical]?.DeepClone();
        result[CollectionBattleKeys.RealHpPool] = loss is not null;
        result[CollectionBattleKeys.ConfirmedFaintRelation] = confirmed ? CollectionBattleKeys.Confirmed : CollectionBattleKeys.None;
        result[CollectionBattleKeys.HpBeforeEqualsMax] = application?[CollectionBattleKeys.HpBeforeEqualsMax]?.DeepClone();
        result[CollectionBattleKeys.MechanicsFunctionFamily] = application?[CollectionBattleKeys.MechanicsFunctionFamily]?.DeepClone();
        result[CollectionBattleKeys.MechanicsCatalogueVersion] = application?[CollectionBattleKeys.MechanicsCatalogueVersion]?.DeepClone();
        if (application is null && loss?[CollectionBattleKeys.LossKind] is JsonNode nonHitKind)
            result[CollectionBattleKeys.MechanicsFunctionFamily] = nonHitKind.DeepClone();

        result[CollectionBattleKeys.ActualDamagingMoveId] = application?[CollectionBattleKeys.MechanicalMoveId]?.DeepClone();
        result[CollectionBattleKeys.EffectiveTypeId] = application?[CollectionBattleKeys.EffectiveType]?.DeepClone();
        result[CollectionBattleKeys.LossKind] = loss?[CollectionBattleKeys.LossKind]?.DeepClone();
        result[CollectionBattleKeys.DamagingAttack] = application is not null && Integer(Property(Element(application), CollectionBattleKeys.ActualPoolHpLost)) > 0;
        result[CollectionBattleKeys.EffectKind] = cause?[CollectionBattleKeys.Kind]?.DeepClone();
        result[CollectionBattleKeys.EffectTerminalResolution] = confirmed ? CollectionBattleKeys.ConfirmedFaint : CollectionBattleKeys.None;
        result[CollectionBattleKeys.EffectivePriority] = matched ? row[CollectionBattleKeys.LastPriority]?[CollectionBattleKeys.EffectivePriority]?.DeepClone() : null;
        result[CollectionBattleKeys.BasePriority] = matched ? row[CollectionBattleKeys.LastPriority]?[CollectionBattleKeys.BasePriority]?.DeepClone() : null;
        result[CollectionBattleKeys.SingleHitCompleteLoss] = matched && Integer(application is null ? Null() : Property(Element(application), CollectionBattleKeys.HpBefore)) is BigInteger before && before > 0 && Integer(Property(Element(application!), CollectionBattleKeys.ActualPoolHpLost)) == before;
        if (application?[CollectionBattleKeys.TypeModifier] is JsonNode numerator && application[CollectionBattleKeys.TypeModifierNormal] is JsonNode denominator)
            result[CollectionBattleKeys.TypeModifierRatio] = new JsonObject { [CollectionBattleKeys.Numerator] = numerator.DeepClone(), [CollectionBattleKeys.Denominator] = denominator.DeepClone() };

        result[CollectionBattleKeys.BattleKind] = battle[CollectionTokens.BattleKind]?.DeepClone();
        result[CollectionBattleKeys.Outcome] = battle[_outcomeKey]?.DeepClone();
        JsonObject? terminalHit = application?.DeepClone().AsObject();
        if (terminalHit is not null)
        {
            terminalHit[CollectionBattleKeys.ConfirmedFaint] = confirmed;
            terminalHit[CollectionBattleKeys.FaintRelation] = confirmed ? CollectionBattleKeys.ThisHit : CollectionBattleKeys.None;
        }

        result[CollectionBattleKeys.TerminalHit] = terminalHit;
        result[CollectionBattleKeys.PriorityContext] = matched ? row[CollectionBattleKeys.LastPriority]?.DeepClone() : null;
        return result;
    }

    /// <summary>
    /// Reads deferred terminal dimensions only for their matching run completion and actual terminal decision.
    /// </summary>
    /// <param name="path">The deferred terminal field requested by a compiled selector.</param>
    /// <param name="frame">The immutable source views and bounded causal context for this projection.</param>
    /// <returns>The requested terminal value only for the matching attempt-completion battle; otherwise JSON null.</returns>
    private JsonElement AdvancedComputed(string path, Frame frame)
    {
        if (Text(frame.Envelope, _typeKey) != _eventAttemptCompleted)
            return Null();

        JsonObject attempt = AttemptState(Text(frame.Envelope, _attemptKey));
        if (attempt[CollectionBattleKeys.TerminalContext] is not JsonObject terminal)
            return Null();

        string? reference = Text(frame.Payload, CollectionBattleKeys.TerminalBattleRef);
        if (reference is null || reference != Text(Element(terminal), CollectionBattleKeys.BattleId))
            return Null();

        return Path(Element(terminal), path);
    }
}
