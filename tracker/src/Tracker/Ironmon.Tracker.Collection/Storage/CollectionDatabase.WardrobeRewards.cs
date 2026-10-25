using System.Security.Cryptography;
using System.Text.Json;
using Ironmon.Tracker.Collection.Achievements;
using Ironmon.Tracker.Collection.Queries.Definitions;
using Ironmon.Tracker.Collection.Queries.Results;
using Ironmon.Tracker.Collection.Storage.Encoding;

namespace Ironmon.Tracker.Collection.Storage;

/// <summary>
/// Retains future achievement rewards until the game has durably credited its shared cosmetic profile.
/// </summary>
internal sealed partial class CollectionDatabase
{
    private const string _wardrobeRewardIdentity = "0a74fe46-8d20-47c6-9d9a-bafc46b0bf59";
    private const string _wardrobeRewardPolicy = "ironmon.wardrobe.achievement-rewards.v1:100";
    private static readonly byte[] _wardrobeRewardConsumer = Guid.Parse(_wardrobeRewardIdentity).ToByteArray();
    private static readonly byte[] _wardrobeRewardBinding = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_wardrobeRewardPolicy));

    /// <summary>
    /// Queues only identities newly earned by this evaluation, within the same collection transaction.
    /// </summary>
    /// <param name="ids">The newly committed achievement identities, excluding all prior unlocks.</param>
    private void QueueWardrobeRewards(IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count == 0)
            return;

        AchievementStateRecord? stored = CollectionQueries.ReadAchievementState(_db, _transaction, _wardrobeRewardConsumer);
        HashSet<Guid> pending = ReadWardrobeRewards(stored);
        pending.UnionWith(ids);
        SaveWardrobeRewards(pending, stored?.StateRevision ?? 0);
    }

    /// <summary>
    /// Reads durable future-unlock rewards without backfilling the existing achievement ledger.
    /// </summary>
    /// <returns>The earned identities still awaiting a durable game acknowledgement.</returns>
    public JsonElement WardrobeRewardsSnapshot()
    {
        if (_profile is null)
            return JsonSerializer.SerializeToElement(Array.Empty<Guid>());

        HashSet<Guid> pending = ReadWardrobeRewards(CollectionQueries.ReadAchievementState(_db, _transaction, _wardrobeRewardConsumer));
        return JsonSerializer.SerializeToElement(pending.Order());
    }

    /// <summary>
    /// Removes only rewards already committed to the game's exactly-once cosmetic ledger.
    /// </summary>
    /// <param name="ids">The identities acknowledged by the connected game.</param>
    /// <returns>An empty acknowledgement object after the transaction commits.</returns>
    public JsonElement AcknowledgeWardrobeRewards(IReadOnlySet<Guid> ids)
    {
        if (_profile is null)
            return JsonSerializer.SerializeToElement(new { });

        using var transaction = _db.BeginTransaction();
        _transaction = transaction;
        try
        {
            AchievementStateRecord? stored = CollectionQueries.ReadAchievementState(_db, _transaction, _wardrobeRewardConsumer);
            HashSet<Guid> pending = ReadWardrobeRewards(stored);
            if (pending.RemoveWhere(ids.Contains) > 0)
                SaveWardrobeRewards(pending, stored?.StateRevision ?? 0);

            transaction.Commit();
        }
        finally
        {
            _transaction = null;
        }

        return JsonSerializer.SerializeToElement(new { });
    }

    /// <summary>
    /// Authenticates the separate reward outbox without changing the achievement notification queue.
    /// </summary>
    /// <param name="stored">The encrypted reward row, or null before the first future unlock.</param>
    /// <returns>The pending earned identities.</returns>
    private HashSet<Guid> ReadWardrobeRewards(AchievementStateRecord? stored)
    {
        if (stored is null)
            return [];

        if (stored.Format != 1 || stored.Slot != 1 || !stored.Binding.AsSpan().SequenceEqual(_wardrobeRewardBinding))
            throw new NotSupportedException("The wardrobe reward outbox requires a compatible migration. Existing progress is preserved.");

        byte[] plaintext = new byte[stored.Ciphertext.Length];
        try
        {
            using AesGcm cipher = new(AchievementPack.StateKey(_profile!, _epoch!), 16);
            cipher.Decrypt(stored.Nonce, stored.Ciphertext, stored.Tag, plaintext, stored.Binding);
            return JsonSerializer.Deserialize<HashSet<Guid>>(plaintext) ?? throw new InvalidDataException("The wardrobe reward outbox is invalid.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>
    /// Commits encrypted reward identities independently of achievement popup acknowledgements.
    /// </summary>
    /// <param name="pending">The future unlocks still awaiting game credit.</param>
    /// <param name="priorRevision">The reward row revision before this transaction.</param>
    private void SaveWardrobeRewards(HashSet<Guid> pending, long priorRevision)
    {
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(pending);
        byte[] nonce = RandomNumberGenerator.GetBytes(12), tag = new byte[16], ciphertext = new byte[plaintext.Length];
        try
        {
            using AesGcm cipher = new(AchievementPack.StateKey(_profile!, _epoch!), 16);
            cipher.Encrypt(nonce, plaintext, ciphertext, tag, _wardrobeRewardBinding);
            CollectionQueries.SaveAchievementState(_db, _transaction, _wardrobeRewardConsumer, _wardrobeRewardBinding, checked(priorRevision + 1), CollectionEncoding.Revision(_revision), 1, 1, nonce, tag, ciphertext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
