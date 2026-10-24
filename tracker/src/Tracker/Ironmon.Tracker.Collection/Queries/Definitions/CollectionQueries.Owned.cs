using Ironmon.Sqlite.Definitions;
using Ironmon.Tracker.Collection.Queries.Results;
using Ironmon.Tracker.Collection.Queries.Tables;

namespace Ironmon.Tracker.Collection.Queries.Definitions;

/// <summary>
/// Defines generated owned storage operations.
/// </summary>
internal static partial class CollectionQueries
{
    private const string _initialAttemptStatus = "open";
    private const string _continuationLineageRole = "continuation";
    private const string _otherLineageRole = "other";
    private const string _completedAttemptCodec = "zlib-json-v1";

    /// <summary>
    /// Declares typed owned queries for compile-time generation.
    /// </summary>
    private static partial class Definitions
    {
        /// <summary>
        /// Creates a technical attempt anchor without modifying an existing attempt.
        /// </summary>
        /// <param name="attempt">The exact sixteen-byte attempt identity.</param>
        /// <param name="context">The initial retained context identity.</param>
        /// <param name="started">The creation time in UTC Unix milliseconds.</param>
        /// <param name="revision">The exact eight-byte collection revision.</param>
        /// <returns>The provider affected-row count, with duplicate suppression where declared.</returns>
        public static SqlQuery<int> EnsureAttempt(byte[] attempt, long context, long started, byte[] revision)
        {
            return Sql.Insert(new OwnedAttemptRow { Attempt = attempt, Context = context, Started = started, Status = _initialAttemptStatus, Revision = revision })
                .OnConflict(row => row.Attempt)
                .DoNothing()
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Resolves an owned entity using its complete attempt and entity key.
        /// </summary>
        /// <param name="attempt">The exact sixteen-byte attempt identity.</param>
        /// <param name="key">The complete canonical owned entity key.</param>
        /// <returns>The exact selected columns, or null when no matching row exists.</returns>
        public static SqlQuery<OwnedIdentityRecord?> FindOwned(byte[] attempt, byte[] key)
        {
            return Sql.From<OwnedIndividualRow>()
                .Where(row => row.Attempt == attempt && row.Key == key)
                .Select(row => new OwnedIdentityRecord { Id = row.Id })
                .FirstOrDefault();
        }

        /// <summary>
        /// Inserts the actual retained individual and reads its signed 64-bit identity.
        /// </summary>
        /// <param name="attempt">The exact sixteen-byte attempt identity.</param>
        /// <param name="key">The complete canonical owned entity key.</param>
        /// <param name="revision">The exact eight-byte first recorded revision.</param>
        /// <returns>The selected signed 64-bit identity.</returns>
        public static SqlQuery<long> InsertOwned(byte[] attempt, byte[] key, byte[] revision)
        {
            return Sql.Insert(new OwnedIndividualRow { Attempt = attempt, Key = key, Revision = revision })
                .LastInsertId();
        }

        /// <summary>
        /// Reads meaningful configuration history state without reconstructing its detail blocks.
        /// </summary>
        /// <param name="individual">The owned individual identity.</param>
        /// <returns>The exact selected columns, or null when no matching row exists.</returns>
        public static SqlQuery<OwnedStateRecord?> ReadOwnedState(long individual)
        {
            return Sql.From<OwnedRecordStateRow>()
                .Where(row => row.Individual == individual)
                .Select(row => new OwnedStateRecord { Fingerprint = row.Fingerprint, Revision = row.Revision, Configuration = row.Configuration })
                .FirstOrDefault();
        }

        /// <summary>
        /// Upserts the complete meaningful configuration fingerprint and retained revision.
        /// </summary>
        /// <param name="individual">The owned individual identity.</param>
        /// <param name="fingerprint">The complete meaningful configuration fingerprint.</param>
        /// <param name="revision">The last retained per-individual configuration revision number.</param>
        /// <param name="configuration">The last retained complete configuration identity.</param>
        /// <returns>The provider affected-row count, with duplicate suppression where declared.</returns>
        public static SqlQuery<int> SaveOwnedState(long individual, byte[] fingerprint, long revision, long configuration)
        {
            return Sql.Insert(new OwnedRecordStateRow { Individual = individual, Fingerprint = fingerprint, Revision = revision, Configuration = configuration })
                .OnConflict(row => row.Individual)
                .UpdateFromInserted(row => new { row.Fingerprint, row.Revision, row.Configuration })
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Interns all six exact stat values and resolves the matching immutable vector.
        /// </summary>
        /// <param name="hp">The exact nonnegative HP stat value.</param>
        /// <param name="attack">The exact nonnegative attack stat value.</param>
        /// <param name="defense">The exact nonnegative defense stat value.</param>
        /// <param name="specialAttack">The exact nonnegative special attack stat value.</param>
        /// <param name="specialDefense">The exact nonnegative special defense stat value.</param>
        /// <param name="speed">The exact nonnegative speed stat value.</param>
        /// <returns>The selected signed 64-bit identity.</returns>
        public static SqlQuery<long> InternStatVector(long hp, long attack, long defense, long specialAttack, long specialDefense, long speed)
        {
            return Sql.Insert(new StatVectorRow { Hp = hp, Attack = attack, Defense = defense, SpecialAttack = specialAttack, SpecialDefense = specialDefense, Speed = speed })
                .OnConflict(row => new { row.Hp, row.Attack, row.Defense, row.SpecialAttack, row.SpecialDefense, row.Speed })
                .DoNothing()
                .ResolveId(row => row.Id);
        }

        /// <summary>
        /// Resolves a simultaneous moveset using the existing digest and slot-count predicate.
        /// </summary>
        /// <param name="digest">The complete moveset digest including slot and PP capacity metadata.</param>
        /// <param name="slots">The number of simultaneously retained move slots, zero through four.</param>
        /// <returns>The exact selected columns, or null when no matching row exists.</returns>
        public static SqlQuery<MovesetIdentityRecord?> FindMoveset(byte[] digest, int slots)
        {
            return Sql.From<OwnedMovesetRow>()
                .Where(row => row.Digest == digest && row.Slots == slots)
                .Select(row => new MovesetIdentityRecord { Id = row.Id })
                .FirstOrDefault();
        }

        /// <summary>
        /// Inserts the simultaneous moveset header without changing its immutable slot metadata.
        /// </summary>
        /// <param name="slots">The number of simultaneously retained move slots, zero through four.</param>
        /// <param name="digest">The complete moveset digest including slot and PP capacity metadata.</param>
        /// <returns>The selected signed 64-bit identity.</returns>
        public static SqlQuery<long> InsertMoveset(int slots, byte[] digest)
        {
            return Sql.Insert(new OwnedMovesetRow { Slots = slots, Digest = digest })
                .LastInsertId();
        }

        /// <summary>
        /// Inserts one exact simultaneous move slot with complete PP capacity metadata.
        /// </summary>
        /// <param name="moveset">The owning simultaneous moveset identity.</param>
        /// <param name="slot">The zero-based move slot ordinal.</param>
        /// <param name="move">The interned exact move identity.</param>
        /// <param name="maxPp">The exact nonnegative PP capacity.</param>
        /// <param name="ppUps">The PP Up count, or null when unavailable.</param>
        /// <returns>The provider affected-row count, with duplicate suppression where declared.</returns>
        public static SqlQuery<int> InsertMovesetSlot(long moveset, int slot, long move, long maxPp, long? ppUps)
        {
            return Sql.Insert(new OwnedMoveSlotRow { Moveset = moveset, Slot = slot, Move = move, MaxPp = maxPp, PpUps = ppUps })
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Inserts every retained Pokemon configuration field and preserves all nullable identities and detail references.
        /// </summary>
        /// <param name="individual">The owned individual identity.</param>
        /// <param name="revision">The per-individual configuration revision number.</param>
        /// <param name="previous">The preceding per-individual revision number, or null for the first.</param>
        /// <param name="branch">The interned exact play branch identity.</param>
        /// <param name="stream">The exact sixteen-byte source stream identity.</param>
        /// <param name="sequence">The exact eight-byte source sequence.</param>
        /// <param name="recorded">The exact eight-byte global collection revision.</param>
        /// <param name="capture">The normalized configuration capture kind.</param>
        /// <param name="role">The actual retained ownership role.</param>
        /// <param name="retained">The whether the individual remains owned after this commit, zero or one.</param>
        /// <param name="species">The interned actual species identity.</param>
        /// <param name="form">The interned form identity, or null when absent.</param>
        /// <param name="head">The interned fusion head species identity, or null when absent.</param>
        /// <param name="body">The interned fusion body species identity, or null when absent.</param>
        /// <param name="level">The actual Pokemon level.</param>
        /// <param name="experience">The exact cumulative Pokemon experience.</param>
        /// <param name="generated">The complete generated base-stat vector identity.</param>
        /// <param name="reference">The reference base-stat vector identity, or null when unavailable.</param>
        /// <param name="computed">The complete permanent computed-stat vector identity.</param>
        /// <param name="rawIv">The raw IV vector identity, or null when unavailable.</param>
        /// <param name="effectiveIv">The effective IV vector identity, or null when unavailable.</param>
        /// <param name="ev">The EV vector identity, or null when unavailable.</param>
        /// <param name="moveset">The complete simultaneous moveset identity.</param>
        /// <param name="ability">The interned ability identity, or null when unavailable.</param>
        /// <param name="nature">The interned nature identity, or null when unavailable.</param>
        /// <param name="effectiveNature">The interned effective nature identity, or null when unavailable.</param>
        /// <param name="heldItem">The interned actual held-item identity, or null when absent.</param>
        /// <param name="favorite">The retained favorite context identity, or null when absent.</param>
        /// <param name="detail">The immutable complete detail block identity.</param>
        /// <param name="checkpoint">The complete resource checkpoint block identity, or null when absent.</param>
        /// <returns>The selected signed 64-bit identity.</returns>
        public static SqlQuery<long> InsertConfiguration(long individual, long revision, long? previous, long branch, byte[] stream, byte[] sequence, byte[] recorded, string capture, string role, int retained, long species, long? form, long? head, long? body, long level, long experience, long generated, long? reference, long computed, long? rawIv, long? effectiveIv, long? ev, long moveset, long? ability, long? nature, long? effectiveNature, long? heldItem, long? favorite, long detail, long? checkpoint)
        {
            return Sql.Insert(new OwnedConfigurationRow { Individual = individual, Revision = revision, Previous = previous, Branch = branch, Stream = stream, Sequence = sequence, Recorded = recorded, Capture = capture, Role = role, Retained = retained, Species = species, Form = form, Head = head, Body = body, Level = level, Experience = experience, Generated = generated, Reference = reference, Computed = computed, RawIv = rawIv, EffectiveIv = effectiveIv, Ev = ev, Moveset = moveset, Ability = ability, Nature = nature, EffectiveNature = effectiveNature, HeldItem = heldItem, Favorite = favorite, Detail = detail, Checkpoint = checkpoint })
                .LastInsertId();
        }

        /// <summary>
        /// Retains partial evidence once per exact individual and source occurrence.
        /// </summary>
        /// <param name="individual">The owned individual identity.</param>
        /// <param name="stream">The exact sixteen-byte source stream identity.</param>
        /// <param name="sequence">The exact eight-byte source sequence.</param>
        /// <param name="detail">The immutable complete detail block identity.</param>
        /// <param name="revision">The exact eight-byte collection revision.</param>
        /// <returns>The provider affected-row count, with duplicate suppression where declared.</returns>
        public static SqlQuery<int> SavePartialConfiguration(long individual, byte[] stream, byte[] sequence, long detail, byte[] revision)
        {
            return Sql.Insert(new OwnedPartialConfigurationRow { Individual = individual, Stream = stream, Sequence = sequence, Detail = detail, Revision = revision })
                .OnConflict(row => new { row.Individual, row.Stream, row.Sequence })
                .DoNothing()
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Retains the actual preceding configuration as a continuation lineage edge.
        /// </summary>
        /// <param name="child">The actual child configuration identity.</param>
        /// <param name="source">The actual retained parent configuration, or null when only a source context exists.</param>
        /// <param name="transactionContext">The actual ownership transaction context identity.</param>
        /// <returns>The provider affected-row count, with duplicate suppression where declared.</returns>
        public static SqlQuery<int> InsertContinuationLineage(long child, long? source, long transactionContext)
        {
            return Sql.Insert(new OwnedLineageRow { Child = child, Role = _continuationLineageRole, Ordinal = 0, Source = source, SourceContext = null, Transaction = transactionContext })
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Retains an actual parent configuration or its unretained source context.
        /// </summary>
        /// <param name="child">The actual child configuration identity.</param>
        /// <param name="ordinal">The zero-based parent relationship ordinal.</param>
        /// <param name="source">The actual retained parent configuration, or null when only a source context exists.</param>
        /// <param name="sourceContext">The actual unretained parent context, or null for a retained parent.</param>
        /// <param name="transactionContext">The actual ownership transaction context identity.</param>
        /// <returns>The provider affected-row count, with duplicate suppression where declared.</returns>
        public static SqlQuery<int> InsertParentLineage(long child, int ordinal, long? source, long? sourceContext, long transactionContext)
        {
            return Sql.Insert(new OwnedLineageRow { Child = child, Role = _otherLineageRole, Ordinal = ordinal, Source = source, SourceContext = sourceContext, Transaction = transactionContext })
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Reads complete sufficient state for genuinely new continued-attempt gameplay.
        /// </summary>
        /// <param name="attempt">The exact sixteen-byte attempt identity.</param>
        /// <returns>The exact selected columns, or null when no matching row exists.</returns>
        public static SqlQuery<CompletedAttemptRecord?> ReadCompletedAttemptState(byte[] attempt)
        {
            return Sql.From<CompletedAttemptRow>()
                .Where(row => row.Attempt == attempt)
                .Select(row => new CompletedAttemptRecord { Payload = row.Payload, Length = row.Length })
                .FirstOrDefault();
        }

        /// <summary>
        /// Upserts complete closed-attempt state and source evidence while retaining format metadata.
        /// </summary>
        /// <param name="attempt">The exact sixteen-byte attempt identity.</param>
        /// <param name="payload">The complete compressed sufficient attempt state.</param>
        /// <param name="length">The exact uncompressed state length in bytes.</param>
        /// <param name="stream">The exact sixteen-byte source stream identity.</param>
        /// <param name="sequence">The exact eight-byte source sequence.</param>
        /// <param name="revision">The exact eight-byte collection revision.</param>
        /// <returns>The provider affected-row count, with duplicate suppression where declared.</returns>
        public static SqlQuery<int> SaveCompletedAttemptState(byte[] attempt, byte[] payload, long length, byte[] stream, byte[] sequence, byte[] revision)
        {
            return Sql.Insert(new CompletedAttemptRow { Attempt = attempt, Format = 1, Codec = _completedAttemptCodec, Payload = payload, Length = length, Stream = stream, Sequence = sequence, Revision = revision })
                .OnConflict(row => row.Attempt)
                .UpdateFromInserted(row => new { row.Payload, row.Length, row.Stream, row.Sequence, row.Revision })
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Distinguishes an existing actual attempt from an absent attempt.
        /// </summary>
        /// <param name="attempt">The exact attempt value retained by the original operation.</param>
        /// <returns>The original typed result within the caller-owned transaction.</returns>
        public static SqlQuery<ValueRootRecord?> FindAttemptPresence(byte[] attempt)
        {
            return Sql.From<OwnedAttemptRow>().Where(row => row.Attempt == attempt)
                .Select(row => new ValueRootRecord { Value = 1L }).FirstOrDefault();
        }

        /// <summary>
        /// Stores complete terminal state without changing attempt identity or opening evidence.
        /// </summary>
        /// <param name="attempt">The exact attempt value retained by the original operation.</param>
        /// <param name="now">The exact now value retained by the original operation.</param>
        /// <param name="status">The exact status value retained by the original operation.</param>
        /// <param name="block">The exact block value retained by the original operation.</param>
        /// <param name="revision">The exact revision value retained by the original operation.</param>
        /// <returns>The original typed result within the caller-owned transaction.</returns>
        public static SqlQuery<int> CloseActualAttempt(byte[] attempt, long now, string status, long block, byte[] revision)
        {
            return Sql.From<StoreAttemptWriteRow>().Where(row => row.Attempt == attempt)
                .Update(row => new StoreAttemptWriteRow { Closed = now, Status = status, Summary = block, Revision = revision }).ExecuteNonQuery();
        }

        /// <summary>
        /// Resolves the retained parent configuration through its actual owned individual.
        /// </summary>
        /// <param name="attempt">The exact attempt value retained by the original operation.</param>
        /// <param name="key">The exact key value retained by the original operation.</param>
        /// <returns>The original typed result within the caller-owned transaction.</returns>
        public static SqlQuery<ParentConfigurationRecord?> FindParentConfiguration(byte[] attempt, byte[] key)
        {
            return Sql.From<OwnedIndividualRow>().Join<OwnedRecordStateRow>((individual, state) => state.Individual == individual.Id)
                .Where((individual, state) => individual.Attempt == attempt && individual.Key == key)
                .Select((individual, state) => new ParentConfigurationRecord { Configuration = state.Configuration }).FirstOrDefault();
        }
    }
}
