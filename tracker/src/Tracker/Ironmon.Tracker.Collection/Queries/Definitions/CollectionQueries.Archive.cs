using Ironmon.Sqlite.Querying;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Defines generated archive storage operations.
/// </summary>
internal static partial class CollectionQueries
{
    /// <summary>
    /// Declares typed archive queries for compile-time generation.
    /// </summary>
    private static partial class Definitions
    {
        /// <summary>
        /// Streams only scopes belonging to the exact retained attempt.
        /// </summary>
        /// <param name="attempt">The exact sixteen-byte owning attempt identity.</param>
        /// <returns>The matching scopes, with the command and reader owned by enumeration.</returns>
        public static SqlQuery<IEnumerable<AttemptScopeRecord>> FindAttemptScopes(byte[] attempt)
        {
            return Sql.From<CollectionScopeRow>()
                .Where(row => row.Attempt == attempt)
                .Select(row => new AttemptScopeRecord { Id = row.Id })
                .AsEnumerable();
        }

        /// <summary>
        /// Removes only cold partition relationships in the requested scope.
        /// </summary>
        /// <param name="scope">The exact lifetime, attempt or battle scope identity.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> DeleteScopeColdPartitions(long scope)
        {
            return Sql.From<ColdPartitionRow>()
                .Where(row => row.Scope == scope)
                .Delete();
        }

        /// <summary>
        /// Deletes only series in the requested partition using the existing cascade relationships.
        /// </summary>
        /// <param name="partition">The exact active relational partition identity.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> DeletePartitionSeries(long partition)
        {
            return Sql.From<AggregateSeriesRow>()
                .Where(row => row.Partition == partition)
                .Delete();
        }

        /// <summary>
        /// Removes only the specified active partition after its dependent rows are removed.
        /// </summary>
        /// <param name="partition">The exact active relational partition identity.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> DeleteHotPartition(long partition)
        {
            return Sql.From<HotPartitionRow>()
                .Where(row => row.Id == partition)
                .Delete();
        }

        /// <summary>
        /// Removes only the complete requested scope and family archive relationship.
        /// </summary>
        /// <param name="scope">The exact lifetime, attempt or battle scope identity.</param>
        /// <param name="family">The registered aggregate family identity.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> DeleteColdPartition(long scope, int family)
        {
            return Sql.From<ColdPartitionRow>()
                .Where(row => row.Scope == scope && row.Family == family)
                .Delete();
        }

        /// <summary>
        /// Deletes only ordered state belonging to the complete scope and family key.
        /// </summary>
        /// <param name="scope">The exact lifetime, attempt or battle scope identity.</param>
        /// <param name="family">The registered aggregate family identity.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> DeleteOrderedState(long scope, int family)
        {
            return Sql.From<OrderedStateRow>()
                .Where(row => row.Scope == scope && row.Family == family)
                .Delete();
        }

        /// <summary>
        /// Resolves the immutable publication using both exact revision columns.
        /// </summary>
        /// <param name="revision">The exact eight-byte collection revision.</param>
        /// <returns>The selected exact publication identity.</returns>
        public static SqlQuery<long> InternColdPublication(byte[] revision)
        {
            return Sql.Insert(new ColdPublicationRow { Revision = revision, Through = revision })
                .OnConflict(row => new { row.Revision, row.Through })
                .DoNothing()
                .ResolveId(row => row.Id);
        }

        /// <summary>
        /// Publishes one scope and family relationship to the exact immutable block and publication.
        /// </summary>
        /// <param name="scope">The exact lifetime, attempt or battle scope identity.</param>
        /// <param name="family">The registered aggregate family identity.</param>
        /// <param name="block">The exact immutable block identity.</param>
        /// <param name="publication">The immutable shared publication identity.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> InsertColdPartition(long scope, int family, long block, long publication)
        {
            return Sql.Insert(new ColdPartitionRow { Scope = scope, Family = family, Block = block, Publication = publication })
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Restores every ordered-state field without replacing its original format, context or source evidence.
        /// </summary>
        /// <param name="family">The registered aggregate family identity.</param>
        /// <param name="scope">The exact lifetime, attempt or battle scope identity.</param>
        /// <param name="tuple">The exact ordered dimension tuple identity.</param>
        /// <param name="context">The retained context identity, or null when absent.</param>
        /// <param name="format">The exact positive retained binary format version.</param>
        /// <param name="codec">The exact retained encoding identifier.</param>
        /// <param name="payload">The complete retained ordered-state bytes.</param>
        /// <param name="stream">The exact sixteen-byte source stream identity.</param>
        /// <param name="sequence">The exact eight-byte source sequence.</param>
        /// <param name="revision">The exact eight-byte collection revision.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> RestoreOrderedState(int family, long scope, long tuple, long? context, long format, string codec, byte[] payload, byte[] stream, byte[] sequence, byte[] revision)
        {
            return Sql.Insert(new OrderedStateRow { Family = family, Scope = scope, Tuple = tuple, Context = context, Format = format, Codec = codec, Payload = payload, Stream = stream, Sequence = sequence, Revision = revision })
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Resolves the archive block using the complete scope and family key.
        /// </summary>
        /// <param name="scope">The interned exact scope identity.</param>
        /// <param name="family">The registered aggregate family identity.</param>
        /// <returns>The exact selected columns, or null when no matching row exists.</returns>
        public static SqlQuery<ColdPartitionRecord?> ReadColdPartition(long scope, int family)
        {
            return Sql.From<ColdPartitionRow>()
                .Where(row => row.Scope == scope && row.Family == family)
                .Select(row => new ColdPartitionRecord { Block = row.Block })
                .FirstOrDefault();
        }

        /// <summary>
        /// Resolves the active relational partition using the complete scope and family key.
        /// </summary>
        /// <param name="scope">The interned exact scope identity.</param>
        /// <param name="family">The registered aggregate family identity.</param>
        /// <returns>The exact selected columns, or null when no matching row exists.</returns>
        public static SqlQuery<HotPartitionRecord?> ReadHotPartition(long scope, int family)
        {
            return Sql.From<HotPartitionRow>()
                .Where(row => row.Scope == scope && row.Family == family)
                .Select(row => new HotPartitionRecord { Id = row.Id })
                .FirstOrDefault();
        }

        /// <summary>
        /// Streams original cold partition relationships in ascending family order.
        /// </summary>
        /// <param name="scope">The complete retained scope identity.</param>
        /// <returns>Matching original rows; enumeration owns and disposes the command and reader.</returns>
        public static SqlQuery<IEnumerable<ArchiveColdPartitionRecord>> FindScopeColdPartitions(long scope)
        {
            return Sql.From<ColdPartitionRow>()
                .Where(row => row.Scope == scope)
                .OrderBy(row => row.Family)
                .Select(row => new ArchiveColdPartitionRecord { Family = row.Family, Block = row.Block, Publication = row.Publication })
                .AsEnumerable();
        }

        /// <summary>
        /// Streams original aggregate series in ascending tuple order.
        /// </summary>
        /// <param name="partition">The complete active partition identity.</param>
        /// <returns>Matching original rows; enumeration owns and disposes the command and reader.</returns>
        public static SqlQuery<IEnumerable<ArchiveSeriesRecord>> FindPartitionSeries(long partition)
        {
            return Sql.From<AggregateSeriesRow>()
                .Where(row => row.Partition == partition)
                .OrderBy(row => row.Tuple)
                .Select(row => new ArchiveSeriesRecord { Series = row.Id, Tuple = row.Tuple })
                .AsEnumerable();
        }

        /// <summary>
        /// Streams original scalar and coverage storage classes in ascending full-width measure order.
        /// </summary>
        /// <param name="series">The exact dimension-series identity.</param>
        /// <returns>Matching original rows; enumeration owns and disposes the command and reader.</returns>
        public static SqlQuery<IEnumerable<ArchiveCellRecord>> FindPartitionCells(long series)
        {
            return Sql.From<ArchiveCellRow>()
                .Where(row => row.Series == series)
                .OrderBy(row => row.Measure)
                .Select(row => new ArchiveCellRecord { Measure = row.Measure, Value = row.Value, Known = row.Known, Unknown = row.Unknown, Unsupported = row.Unsupported, NotApplicable = row.NotApplicable, Revision = row.Revision })
                .AsEnumerable();
        }

        /// <summary>
        /// Streams complete exact-set members ordered by measure and then member identity.
        /// </summary>
        /// <param name="series">The exact dimension-series identity.</param>
        /// <returns>Matching original rows; enumeration owns and disposes the command and reader.</returns>
        public static SqlQuery<IEnumerable<ArchiveMemberRecord>> FindPartitionMembers(long series)
        {
            return Sql.From<ArchiveMemberRow>()
                .Where(row => row.Series == series)
                .OrderBy(row => row.Measure)
                .ThenBy(row => row.Member)
                .Select(row => new ArchiveMemberRecord { Measure = row.Measure, Member = row.Member })
                .AsEnumerable();
        }

        /// <summary>
        /// Streams original independent coverage populations in ascending full-width measure order.
        /// </summary>
        /// <param name="series">The exact dimension-series identity.</param>
        /// <returns>Matching original rows; enumeration owns and disposes the command and reader.</returns>
        public static SqlQuery<IEnumerable<ArchiveCoverageRecord>> FindPartitionCoverage(long series)
        {
            return Sql.From<ArchiveCoverageRow>()
                .Where(row => row.Series == series)
                .OrderBy(row => row.Measure)
                .Select(row => new ArchiveCoverageRecord { Measure = row.Measure, Known = row.Known, Unknown = row.Unknown, Unsupported = row.Unsupported, NotApplicable = row.NotApplicable, Revision = row.Revision })
                .AsEnumerable();
        }

        /// <summary>
        /// Streams every original ordered-state field in ascending tuple order.
        /// </summary>
        /// <param name="family">The registered aggregate family identity.</param>
        /// <param name="scope">The complete retained scope identity.</param>
        /// <returns>Matching original rows; enumeration owns and disposes the command and reader.</returns>
        public static SqlQuery<IEnumerable<ArchiveOrderedRecord>> FindPartitionOrdered(int family, long scope)
        {
            return Sql.From<OrderedStateRow>()
                .Where(row => row.Family == family && row.Scope == scope)
                .OrderBy(row => row.Tuple)
                .Select(row => new ArchiveOrderedRecord { Tuple = row.Tuple, Context = row.Context, Format = row.Format, Codec = row.Codec, Payload = row.Payload, Stream = row.Stream, Sequence = row.Sequence, Revision = row.Revision })
                .AsEnumerable();
        }

        /// <summary>
        /// Removes a replaced immutable block only after both retained reference classes are absent.
        /// </summary>
        /// <param name="block">The exact block value retained by the original operation.</param>
        /// <returns>The original typed result within the caller-owned transaction.</returns>
        public static SqlQuery<int> DeleteReplacedColdBlock(long block)
        {
            return Sql.From<ImmutableBlockRow>()
                .Where(row => row.Id == block && !Sql.Exists<ColdPartitionRow>(partition => partition.Block == block) && !Sql.Exists<ColdOverlayLookupRow>(overlay => overlay.Block == block))
                .Delete();
        }

        /// <summary>
        /// Deletes only cold publications without any remaining partition reference.
        /// </summary>
        /// <returns>The original typed result within the caller-owned transaction.</returns>
        public static SqlQuery<int> DeleteUnusedPublications()
        {
            return Sql.From<ColdPublicationRow>()
                .Where(row => !Sql.Exists<ColdPartitionRow>(partition => partition.Publication == row.Id)).Delete();
        }

        /// <summary>
        /// Streams only active partitions belonging to the exact attempt and scope.
        /// </summary>
        /// <param name="attempt">The exact attempt value retained by the original operation.</param>
        /// <param name="scope">The exact scope value retained by the original operation.</param>
        /// <returns>The original typed result within the caller-owned transaction.</returns>
        public static SqlQuery<IEnumerable<ScopePartitionRecord>> FindScopePartitions(byte[] attempt, long scope)
        {
            return Sql.From<HotPartitionRow>().Join<CollectionScopeRow>((partition, owner) => partition.Scope == owner.Id)
                .Where((partition, owner) => owner.Attempt == attempt && owner.Id == scope)
                .Select((partition, owner) => new ScopePartitionRecord { Partition = partition.Id, Family = partition.Family }).AsEnumerable();
        }
    }
}
