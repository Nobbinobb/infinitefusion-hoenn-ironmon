using Ironmon.Tracker.Collection.Achievements;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Ironmon.Tracker.Collection.Queries.Definitions;
using Ironmon.Tracker.Collection.Queries.Results;
using Ironmon.Tracker.Collection.Storage.Encoding;

namespace Ironmon.Tracker.Collection.Storage;

/// <summary>
/// Commits hidden achievement state alongside collection receipts and directly retained evidence.
/// </summary>
internal sealed partial class CollectionDatabase
{
    private const string _achievementConsumerIdentity = "417a26dd-35bb-48e1-b450-707d56a1872b";
    private const string _achievementValue = "value";
    private const string _achievementCoverage = "coverage";
    private const string _achievementKind = "kind";
    private const string _achievementSet = "set";
    private static readonly byte[] _achievementConsumer = Guid.Parse(_achievementConsumerIdentity).ToByteArray();
    private readonly HashSet<string> _achievementChangedGroups = [];
    private AchievementPack? _achievementPack;
    private ulong? _achievementEvaluationRevision;

    /// <summary>
    /// Evaluates newly relevant active rules to a fixed point without exposing dormant definitions.
    /// </summary>
    /// <param name="changedOnly">Whether unchanged previously active groups can be skipped.</param>
    private void EvaluateAchievements(bool changedOnly)
    {
        if (_profile is null || _epoch is null)
            return;

        _achievementPack ??= new AchievementPack();
        AchievementStateRecord? stored = CollectionQueries.ReadAchievementState(_db, _transaction, _achievementConsumer);
        AchievementCheckpoint checkpoint = ReadAchievementCheckpoint(stored);
        HashSet<Guid> earned = [.. checkpoint.Earned.Keys];
        HashSet<Guid> evaluated = [];
        List<Guid> newlyEarned = [];
        Dictionary<string, IReadOnlyList<AchievementEvidence>> evidence = [];
        Dictionary<long, JsonElement> dimensions = [];
        bool changed = stored is null || !stored.Binding.AsSpan().SequenceEqual(_achievementPack.Binding);
        while (true)
        {
            bool advanced = false;
            foreach (SealedAchievement node in _achievementPack.Nodes)
            {
                if (earned.Contains(node.Id) || evaluated.Contains(node.Id) || !AchievementPack.Eligible(node, earned))
                    continue;

                bool previouslyActive = _achievementPack.IsOpen(node.Id);
                AchievementRule rule = _achievementPack.Open(node.Id, earned);
                evaluated.Add(node.Id);
                if (changedOnly && previouslyActive && !_achievementChangedGroups.Contains(rule.Group))
                    continue;

                if (!evidence.TryGetValue(rule.Group, out IReadOnlyList<AchievementEvidence>? rows))
                {
                    rows = ReadAchievementEvidence(rule.Group, dimensions);
                    evidence[rule.Group] = rows;
                }

                if (!AchievementEvaluator.Satisfied(rule, rows))
                    continue;

                checkpoint.Earned[node.Id] = DateTimeOffset.UtcNow;
                checkpoint.Pending.Add(node.Id);
                earned.Add(node.Id);
                newlyEarned.Add(node.Id);
                advanced = true;
                changed = true;
            }

            if (!advanced)
                break;
        }

        if (changed)
            SaveAchievementCheckpoint(checkpoint, stored?.StateRevision ?? 0);

        QueueWardrobeRewards(newlyEarned);

        _achievementEvaluationRevision = _revision;
    }

    /// <summary>
    /// Returns revealed earned records or their durable pending notification subset.
    /// </summary>
    /// <param name="pendingOnly">Whether to return only notices awaiting displayed acknowledgements.</param>
    /// <returns>Only unlocked player-facing metadata; locked names, conditions and progress are absent.</returns>
    public JsonElement AchievementSnapshot(bool pendingOnly)
    {
        if (_profile is null)
            return JsonSerializer.SerializeToElement(Array.Empty<UnlockedAchievement>());

        if (_achievementEvaluationRevision != _revision)
        {
            using var transaction = _db.BeginTransaction();
            _transaction = transaction;
            try
            {
                EvaluateAchievements(false);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                _achievementPack = null;
                _achievementEvaluationRevision = null;
                throw;
            }
            finally
            {
                _transaction = null;
            }
        }

        AchievementCheckpoint checkpoint = ReadAchievementCheckpoint(CollectionQueries.ReadAchievementState(_db, _transaction, _achievementConsumer));
        HashSet<Guid> earnedIds = [.. checkpoint.Earned.Keys];
        IEnumerable<KeyValuePair<Guid, DateTimeOffset>> earned = checkpoint.Earned.Where(pair => !pendingOnly || checkpoint.Pending.Contains(pair.Key));
        return JsonSerializer.SerializeToElement(earned.Select(pair =>
        {
            AchievementRule rule = _achievementPack!.Open(pair.Key, earnedIds);
            return new UnlockedAchievement(pair.Key, rule.Number, rule.Name, rule.Condition, pair.Value);
        }).OrderBy(item => item.UnlockedAt).ThenBy(item => item.Number).ToArray());
    }

    /// <summary>
    /// Removes only acknowledged displayed notices while retaining every permanent unlock.
    /// </summary>
    /// <param name="ids">Stable earned identities actually displayed by the game.</param>
    /// <returns>An empty acknowledgement object after the durable transaction commits.</returns>
    public JsonElement AcknowledgeAchievements(IReadOnlySet<Guid> ids)
    {
        if (_profile is null)
            return JsonSerializer.SerializeToElement(new { });

        _achievementPack ??= new AchievementPack();
        using var transaction = _db.BeginTransaction();
        _transaction = transaction;
        try
        {
            AchievementStateRecord? stored = CollectionQueries.ReadAchievementState(_db, _transaction, _achievementConsumer);
            AchievementCheckpoint state = ReadAchievementCheckpoint(stored);
            if (state.Pending.RemoveWhere(ids.Contains) > 0)
                SaveAchievementCheckpoint(state, stored?.StateRevision ?? 0);

            transaction.Commit();
        }
        finally
        {
            _transaction = null;
        }

        return JsonSerializer.SerializeToElement(new { });
    }

    /// <summary>
    /// Authenticates one private ledger against its profile, collection epoch and exact pack.
    /// </summary>
    /// <param name="stored">The encrypted checkpoint, or null for the first consumer registration.</param>
    /// <returns>The authenticated unlock ledger and durable pending queue.</returns>
    private AchievementCheckpoint ReadAchievementCheckpoint(AchievementStateRecord? stored)
    {
        if (stored is null)
            return new AchievementCheckpoint();

        if (stored.Format != 1 || stored.Slot != 1)
            throw new NotSupportedException("The encrypted achievement checkpoint requires a compatible pack migration. Existing collection data is preserved.");

        byte[] plaintext = new byte[stored.Ciphertext.Length];
        using AesGcm cipher = new(AchievementPack.StateKey(_profile!, _epoch!), 16);
        cipher.Decrypt(stored.Nonce, stored.Ciphertext, stored.Tag, plaintext, stored.Binding);
        try
        {
            AchievementCheckpoint state = JsonSerializer.Deserialize<AchievementCheckpoint>(plaintext) ?? throw new InvalidDataException("The achievement checkpoint is invalid.");
            HashSet<Guid> known = [.. _achievementPack!.Nodes.Select(node => node.Id)];
            if (state.Earned.Keys.Any(id => !known.Contains(id)) || !state.Pending.IsSubsetOf(state.Earned.Keys))
                throw new NotSupportedException("The achievement pack requires an explicit identity migration. Existing unlocks and collection data are preserved.");

            return state;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>
    /// Encrypts the ledger and pending queue within the caller's current SQLite transaction.
    /// </summary>
    /// <param name="state">The complete private ledger to preserve.</param>
    /// <param name="priorRevision">The prior monotonic private-state revision.</param>
    private void SaveAchievementCheckpoint(AchievementCheckpoint state, long priorRevision)
    {
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(state);
        byte[] nonce = RandomNumberGenerator.GetBytes(12), tag = new byte[16], ciphertext = new byte[plaintext.Length];
        try
        {
            using AesGcm cipher = new(AchievementPack.StateKey(_profile!, _epoch!), 16);
            cipher.Encrypt(nonce, plaintext, ciphertext, tag, _achievementPack!.Binding);
            CollectionQueries.SaveAchievementState(_db, _transaction, _achievementConsumer, _achievementPack.Binding, checked(priorRevision + 1), CollectionEncoding.Revision(_revision), 1, 1, nonce, tag, ciphertext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>
    /// Loads one generic group using indexed generated queries and preserves all same-row correlations.
    /// </summary>
    /// <param name="stableGroup">The opaque generic group identity named by an active rule.</param>
    /// <param name="decodedDimensions">Typed generic atoms already decoded during this evaluation.</param>
    /// <returns>Known typed evidence without decoding historical archives.</returns>
    private IReadOnlyList<AchievementEvidence> ReadAchievementEvidence(string stableGroup, Dictionary<long, JsonElement> decodedDimensions)
        => [.. ReadAchievementEvidenceRows(stableGroup, decodedDimensions).Values];

    /// <summary>
    /// Keeps stable observation identities when classifying whole rows for history maintenance.
    /// </summary>
    /// <param name="stableGroup">The exact registered group identity.</param>
    /// <param name="decodedDimensions">Shared decoded dimension values.</param>
    /// <returns>Joint evidence keyed by its actual retained series identity.</returns>
    private Dictionary<long, AchievementEvidence> ReadAchievementEvidenceRows(string stableGroup, Dictionary<long, JsonElement> decodedDimensions)
    {
        (int group, JsonElement definition) = _directGroups.TryGetValue(stableGroup, out var registered) ? registered : throw new InvalidDataException("An achievement references an unregistered evidence group.");
        string[] dimensions = [.. definition.GetProperty(_dimensionsProperty).EnumerateArray().Select(value => value.GetProperty(_keyProperty).GetString()!)];
        string[] measures = [.. definition.GetProperty(_measuresProperty).EnumerateArray().Select(value => value.GetProperty(_keyProperty).GetString()!)];
        Dictionary<long, AchievementEvidence> rows = [];
        foreach (AchievementDimensionRecord value in CollectionQueries.FindAchievementDimensions(_db, _transaction, group))
        {
            JsonElement decoded = ReadAchievementDimension(value.Value, decodedDimensions);
            if (decoded.ValueKind != JsonValueKind.Undefined)
                AchievementEvidenceRow(rows, value.Series).Dimensions[dimensions[value.Dimension]] = decoded;
        }

        foreach (AchievementScalarRecord value in CollectionQueries.FindAchievementScalars(_db, _transaction, group))
            AchievementEvidenceRow(rows, value.Series).Scalars[measures[value.Measure]] = value.Integer.HasValue ? new BigInteger(value.Integer.Value) : BigInteger.Parse(value.Wide!, CultureInfo.InvariantCulture);

        foreach (AchievementSetRecord value in CollectionQueries.FindAchievementSets(_db, _transaction, group))
        {
            AchievementEvidence row = AchievementEvidenceRow(rows, value.Series);
            string measure = measures[value.Measure];
            if (!row.Sets.TryGetValue(measure, out List<JsonElement>? members))
            {
                members = [];
                row.Sets[measure] = members;
            }

            members.Add(_values.Read(value.Member));
        }

        foreach (AchievementRatioRecord value in CollectionQueries.FindAchievementRatios(_db, _transaction, group))
            AchievementEvidenceRow(rows, value.Series).Ratios[measures[value.Measure]] = (value.Numerator.HasValue ? new BigInteger(value.Numerator.Value) : BigInteger.Parse(value.NumeratorWide!, CultureInfo.InvariantCulture), value.Denominator.HasValue ? new BigInteger(value.Denominator.Value) : BigInteger.Parse(value.DenominatorWide!, CultureInfo.InvariantCulture));

        return rows;
    }

    /// <summary>
    /// Decodes shared typed dimensions once per evaluation while preserving unavailable observations.
    /// </summary>
    /// <param name="id">The exact interned atom value identity.</param>
    /// <param name="cache">The evaluation-local decoded atom cache.</param>
    /// <returns>The known typed value, or an undefined element for unavailable evidence.</returns>
    private JsonElement ReadAchievementDimension(long id, Dictionary<long, JsonElement> cache)
    {
        if (cache.TryGetValue(id, out JsonElement cached))
            return cached;

        JsonElement atom = _values.Read(id);
        JsonElement decoded = default;
        if (atom.GetProperty(_achievementCoverage).GetString() == _known)
        {
            JsonElement stored = atom.GetProperty(_achievementValue);
            decoded = atom.GetProperty(_achievementKind).GetString() == _achievementSet
                ? JsonSerializer.SerializeToElement(stored.EnumerateArray().Select(member => JsonSerializer.Deserialize<JsonElement>(Convert.FromHexString(member.GetString()!))))
                : stored.Clone();
        }

        cache[id] = decoded;
        return decoded;
    }

    /// <summary>
    /// Resolves one correlated summary row without merging unrelated series.
    /// </summary>
    /// <param name="rows">Rows already read from this generic group.</param>
    /// <param name="id">The exact series identity.</param>
    /// <returns>The existing row or an empty row registered under this identity.</returns>
    private static AchievementEvidence AchievementEvidenceRow(Dictionary<long, AchievementEvidence> rows, long id)
    {
        if (!rows.TryGetValue(id, out AchievementEvidence? row))
        {
            row = new AchievementEvidence();
            rows[id] = row;
        }

        return row;
    }
}
