using Ironmon.Sqlite.Definitions;
using Ironmon.Tracker.Collection.Queries.Results;
using Ironmon.Tracker.Collection.Queries.Tables;

namespace Ironmon.Tracker.Collection.Queries.Definitions;

/// <summary>
/// Defines generated blocks storage operations.
/// </summary>
internal static partial class CollectionQueries
{
    /// <summary>
    /// Declares typed blocks queries for compile-time generation.
    /// </summary>
    private static partial class Definitions
    {
        /// <summary>
        /// Inserts exact immutable block bytes using the existing insert-and-identity statement shape.
        /// </summary>
        /// <param name="blockKind">The registered immutable-block kind identity.</param>
        /// <param name="format">The stored binary format version.</param>
        /// <param name="codec">The stored raw or compressed block encoding.</param>
        /// <param name="length">The uncompressed byte length.</param>
        /// <param name="digest">The complete immutable-block digest.</param>
        /// <param name="payload">The complete immutable block bytes.</param>
        /// <returns>The signed 64-bit identity allocated on the current connection.</returns>
        public static SqlQuery<long> InsertBlock(long blockKind, int format, string codec, long length, byte[] digest, byte[] payload)
        {
            return Sql.Insert(new ImmutableBlockRow { BlockKind = blockKind, Format = format, Codec = codec, Length = length, Digest = digest, Payload = payload })
                .LastInsertId();
        }

        /// <summary>
        /// Reads complete retained block bytes and every decoding and integrity field.
        /// </summary>
        /// <param name="block">The exact immutable block identity.</param>
        /// <returns>The exact selected columns, or null when absent.</returns>
        public static SqlQuery<ImmutableBlockRecord?> ReadImmutableBlock(long block)
        {
            return Sql.From<ImmutableBlockRow>()
                .Where(row => row.Id == block)
                .Select(row => new ImmutableBlockRecord { Payload = row.Payload, Length = row.Length, Digest = row.Digest, Format = row.Format, Codec = row.Codec })
                .FirstOrDefault();
        }

        /// <summary>
        /// Interns an immutable block category by its exact name.
        /// </summary>
        /// <param name="kind">The immutable block category.</param>
        /// <returns>The signed 64-bit matching identity, whether the row was inserted or already existed.</returns>
        public static SqlQuery<long> InternBlockKind(string kind)
        {
            return Sql.Insert(new BlockKindRow { Kind = kind })
                .OnConflict(row => row.Kind)
                .DoNothing()
                .ResolveId(row => row.Id);
        }

        /// <summary>
        /// Streams exact digest candidates while preserving the prefix-index predicate and byte verification.
        /// </summary>
        /// <param name="kind">The exact kind value retained by the original operation.</param>
        /// <param name="format">The exact format value retained by the original operation.</param>
        /// <param name="prefix">The exact prefix value retained by the original operation.</param>
        /// <param name="digest">The exact digest value retained by the original operation.</param>
        /// <param name="length">The exact length value retained by the original operation.</param>
        /// <returns>The original typed result within the caller-owned transaction.</returns>
        public static SqlQuery<IEnumerable<BlockCandidateRecord>> FindBlockCandidates(long kind, int format, byte[] prefix, byte[] digest, long length)
        {
            return Sql.From<ImmutableBlockRow>()
                .Where(row => row.BlockKind == kind && row.Format == format && Sql.Substring(row.Digest, 1, 8) == prefix && row.Digest == digest && row.Length == length)
                .Select(row => new BlockCandidateRecord { Id = row.Id, Codec = row.Codec, Payload = row.Payload }).AsEnumerable();
        }
    }
}
