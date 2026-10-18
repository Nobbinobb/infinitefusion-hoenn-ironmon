using Ironmon.Sqlite.Querying;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Defines generated values storage operations.
/// </summary>
internal static partial class CollectionQueries
{
    private const string _nativeIdentityKind = "native_id";

    /// <summary>
    /// Declares typed values queries for compile-time generation.
    /// </summary>
    private static partial class Definitions
    {
        /// <summary>
        /// Resolves one immutable value by its exact database identity.
        /// </summary>
        /// <param name="id">The signed 64-bit value identity.</param>
        /// <returns>The stored representation and integrity bytes, or null if the identity is absent.</returns>
        public static SqlQuery<CollectionValueRecord?> ReadValue(long id)
        {
            return Sql.From<CollectionValueRow>()
                .Where(row => row.Id == id)
                .Select(row => new CollectionValueRecord { Kind = row.Kind, Payload = row.Payload, Digest = row.Digest })
                .FirstOrDefault();
        }

        /// <summary>
        /// Streams possible intern matches without treating a digest collision as value equality.
        /// </summary>
        /// <param name="kind">The stored primitive representation code.</param>
        /// <param name="digest">The exact eight-byte lookup digest.</param>
        /// <returns>Candidate identities and complete payloads for the caller's byte comparison.</returns>
        public static SqlQuery<IEnumerable<CollectionValueCandidate>> FindValues(int kind, byte[] digest)
        {
            return Sql.From<CollectionValueRow>()
                .Where(row => row.Kind == kind && row.Digest == digest)
                .Select(row => new CollectionValueCandidate { Id = row.Id, Payload = row.Payload })
                .AsEnumerable();
        }

        /// <summary>
        /// Inserts one immutable value using the existing insert-and-identity statement shape.
        /// </summary>
        /// <param name="kind">The exact primitive representation code.</param>
        /// <param name="payload">The complete immutable binary body.</param>
        /// <param name="digest">The matching eight-byte integrity digest.</param>
        /// <returns>The signed 64-bit identity allocated by SQLite on this connection.</returns>
        public static SqlQuery<long> InsertValue(int kind, byte[] payload, byte[] digest)
        {
            return Sql.Insert(new CollectionValueRow { Kind = kind, Payload = payload, Digest = digest })
                .LastInsertId();
        }

        /// <summary>
        /// Streams original immutable string bytes and digests in ascending identity order.
        /// </summary>
        /// <returns>Matching original rows; enumeration owns and disposes the command and reader.</returns>
        public static SqlQuery<IEnumerable<CollectionStringRecord>> FindStoredStrings()
        {
            return Sql.From<CollectionValueRow>()
                .Where(row => row.Kind == 5)
                .OrderBy(row => row.Id)
                .Select(row => new CollectionStringRecord { Id = row.Id, Payload = row.Payload, Digest = row.Digest })
                .AsEnumerable();
        }

        /// <summary>
        /// Streams complete original dictionary fields for retained-reference tracing.
        /// </summary>
        /// <returns>Matching original rows; enumeration owns and disposes the command and reader.</returns>
        public static SqlQuery<IEnumerable<CollectionValueReferenceRecord>> FindValueReferences()
        {
            return Sql.From<CollectionValueRow>()
                .Select(row => new CollectionValueReferenceRecord { Id = row.Id, Kind = row.Kind, Payload = row.Payload, Digest = row.Digest })
                .AsEnumerable();
        }

        /// <summary>
        /// Streams complete encoded ordered-state bodies that use the retained value-reference codec.
        /// </summary>
        /// <returns>Matching retained records, with the command and reader owned by enumeration.</returns>
        public static SqlQuery<IEnumerable<OrderedStateRecord>> FindOrderedReferenceStates()
        {
            return Sql.From<OrderedStateRow>()
                .Where(row => row.Codec == _orderedCodec)
                .Select(row => new OrderedStateRecord { Payload = row.Payload })
                .AsEnumerable();
        }

        /// <summary>
        /// Streams complete newer-format block fields without changing the caller's reference tracing policy.
        /// </summary>
        /// <returns>Matching retained records, with the command and reader owned by enumeration.</returns>
        public static SqlQuery<IEnumerable<ReferenceBlockRecord>> FindReferenceBlocks()
        {
            return Sql.From<ImmutableBlockRow>()
                .Where(row => row.Format >= 2)
                .Select(row => new ReferenceBlockRecord { Id = row.Id, Format = row.Format, Codec = row.Codec, Payload = row.Payload })
                .AsEnumerable();
        }

        /// <summary>
        /// Streams the exact union of all directly retained dictionary value identities.
        /// </summary>
        /// <returns>The original typed result within the caller-owned transaction.</returns>
        public static SqlQuery<IEnumerable<ValueRootRecord>> FindRelationalValueRoots()
        {
            return Sql.From<NativeIdentityRow>().Select(row => new ValueRootRecord { Value = row.Value })
                .Union(Sql.From<DirectAtomLookupRow>().Select(row => new ValueRootRecord { Value = row.ValueId }))
                .Union(Sql.From<DirectAtomMemberRow>().Select(row => new ValueRootRecord { Value = row.Member }))
                .Union(Sql.From<DirectSetMemberRow>().Select(row => new ValueRootRecord { Value = row.Member })).AsEnumerable();
        }

        /// <summary>
        /// Streams every retained encoded key without deduplicating separate evidence rows.
        /// </summary>
        /// <returns>The original typed result within the caller-owned transaction.</returns>
        public static SqlQuery<IEnumerable<BinaryRootRecord>> FindBinaryValueRoots()
        {
            return Sql.From<CollectionContextRow>().Select(row => new BinaryRootRecord { Value = row.Key })
                .UnionAll(Sql.From<OwnedIndividualRow>().Select(row => new BinaryRootRecord { Value = row.Key }))
                .UnionAll(Sql.From<CollectionScopeRow>().Where(row => row.Kind == _keyBattleScope || row.Kind == _keyIndividualScope || row.Kind == _keySessionScope || row.Kind == _keySaveLineageScope || row.Kind == _keyWardrobeSessionScope).Select(row => new BinaryRootRecord { Value = row.Key }))
                .UnionAll(Sql.From<DirectAtomRow>().Where(row => row.Kind == _nativeIdentityKind && row.Blob != null).Select(row => new BinaryRootRecord { Value = row.Blob })).AsEnumerable();
        }

        /// <summary>
        /// Streams the exact union of base and overlay archive references.
        /// </summary>
        /// <returns>The original typed result within the caller-owned transaction.</returns>
        public static SqlQuery<IEnumerable<ValueRootRecord>> FindArchiveValueRoots()
        {
            return Sql.From<ColdPartitionRow>().Distinct().Select(row => new ValueRootRecord { Value = row.Block })
                .Union(Sql.From<ColdOverlayLookupRow>().Select(row => new ValueRootRecord { Value = row.Block })).AsEnumerable();
        }

        /// <summary>
        /// Rewrites a native representation without changing its immutable value identity.
        /// </summary>
        /// <param name="id">The exact id value retained by the original operation.</param>
        /// <param name="payload">The exact payload value retained by the original operation.</param>
        /// <param name="digest">The exact digest value retained by the original operation.</param>
        /// <returns>The original typed result within the caller-owned transaction.</returns>
        public static SqlQuery<int> SaveNativeString(long id, byte[] payload, byte[] digest)
        {
            return Sql.From<CollectionValueRow>().Where(row => row.Id == id)
                .Update(row => new CollectionValueRow { Payload = payload, Digest = digest }).ExecuteNonQuery();
        }

        /// <summary>
        /// Deletes one verified unreachable dictionary value within the current transaction.
        /// </summary>
        /// <param name="id">The exact id value retained by the original operation.</param>
        /// <returns>The original typed result within the caller-owned transaction.</returns>
        [SqlCommandFactory]
        public static SqlQuery<int> DeleteUnreferencedValue(long id)
        {
            return Sql.From<CollectionValueRow>().Where(row => row.Id == id).Delete();
        }
    }
}
