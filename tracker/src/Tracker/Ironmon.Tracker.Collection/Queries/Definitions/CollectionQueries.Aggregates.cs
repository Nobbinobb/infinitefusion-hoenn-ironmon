using Ironmon.Sqlite.Definitions;
using Ironmon.Tracker.Collection.Queries.Results;
using Ironmon.Tracker.Collection.Queries.Tables;

namespace Ironmon.Tracker.Collection.Queries.Definitions;

/// <summary>
/// Defines generated aggregates storage operations.
/// </summary>
internal static partial class CollectionQueries
{
    /// <summary>
    /// Declares typed aggregates queries for compile-time generation.
    /// </summary>
    private static partial class Definitions
    {
        /// <summary>
        /// Reads a scalar and all four native or overflow coverage populations.
        /// </summary>
        /// <param name="series">The exact dimension-series identity.</param>
        /// <param name="measure">The stable measure ordinal.</param>
        /// <returns>The selected exact values, or null when no matching row exists.</returns>
        public static SqlQuery<AggregateCellRecord?> ReadCell(long series, int measure)
        {
            return Sql.From<AggregateCellRow>()
                .Where(row => row.Series == series && row.Measure == measure)
                .Select(row => new AggregateCellRecord { Value = row.Value, Known = row.Known, Unknown = row.Unknown, Unsupported = row.Unsupported, NotApplicable = row.NotApplicable })
                .FirstOrDefault();
        }

        /// <summary>
        /// Upserts a scalar and coverage without replacing its original family identity.
        /// </summary>
        /// <param name="series">The exact dimension-series identity.</param>
        /// <param name="family">The registered aggregate family identity.</param>
        /// <param name="measure">The stable measure ordinal.</param>
        /// <param name="value">The native integer, overflow bytes or DBNull for an unknown scalar.</param>
        /// <param name="known">The exact known population as an integer or overflow bytes.</param>
        /// <param name="unknown">The exact unknown population as an integer or overflow bytes.</param>
        /// <param name="unsupported">The exact unsupported population as an integer or overflow bytes.</param>
        /// <param name="notApplicable">The exact not-applicable population as an integer or overflow bytes.</param>
        /// <param name="revision">The exact eight-byte collection revision.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> SaveCell(long series, int family, int measure, object value, object known, object unknown, object unsupported, object notApplicable, byte[] revision)
        {
            return Sql.Insert(new AggregateCellRow { Series = series, Family = family, Measure = measure, Value = value, Known = known, Unknown = unknown, Unsupported = unsupported, NotApplicable = notApplicable, Revision = revision })
                .OnConflict(row => new { row.Series, row.Measure })
                .UpdateFromInserted(row => new { row.Value, row.Known, row.Unknown, row.Unsupported, row.NotApplicable, row.Revision })
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Reads all four exact-set coverage populations without interpreting unavailable as zero.
        /// </summary>
        /// <param name="series">The exact dimension-series identity.</param>
        /// <param name="measure">The stable measure ordinal.</param>
        /// <returns>The selected exact values, or null when no matching row exists.</returns>
        public static SqlQuery<AggregateCoverageRecord?> ReadSetCoverage(long series, int measure)
        {
            return Sql.From<AggregateSetCoverageRow>()
                .Where(row => row.Series == series && row.Measure == measure)
                .Select(row => new AggregateCoverageRecord { Known = row.Known, Unknown = row.Unknown, Unsupported = row.Unsupported, NotApplicable = row.NotApplicable })
                .FirstOrDefault();
        }

        /// <summary>
        /// Upserts exact-set coverage while retaining the original family identity.
        /// </summary>
        /// <param name="series">The exact dimension-series identity.</param>
        /// <param name="family">The registered aggregate family identity.</param>
        /// <param name="measure">The stable measure ordinal.</param>
        /// <param name="known">The exact known population as an integer or overflow bytes.</param>
        /// <param name="unknown">The exact unknown population as an integer or overflow bytes.</param>
        /// <param name="unsupported">The exact unsupported population as an integer or overflow bytes.</param>
        /// <param name="notApplicable">The exact not-applicable population as an integer or overflow bytes.</param>
        /// <param name="revision">The exact eight-byte collection revision.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> SaveSetCoverage(long series, int family, int measure, object known, object unknown, object unsupported, object notApplicable, byte[] revision)
        {
            return Sql.Insert(new AggregateSetCoverageRow { Series = series, Family = family, Measure = measure, Known = known, Unknown = unknown, Unsupported = unsupported, NotApplicable = notApplicable, Revision = revision })
                .OnConflict(row => new { row.Series, row.Measure })
                .UpdateFromInserted(row => new { row.Known, row.Unknown, row.Unsupported, row.NotApplicable, row.Revision })
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Adds exact set membership once while retaining the original row on duplicate membership.
        /// </summary>
        /// <param name="series">The exact dimension-series identity.</param>
        /// <param name="family">The registered aggregate family identity.</param>
        /// <param name="measure">The stable measure ordinal.</param>
        /// <param name="member">The interned exact member identity.</param>
        /// <returns>The affected-row count; ignored duplicate members return zero.</returns>
        public static SqlQuery<int> AddSetMember(long series, int family, int measure, long member)
        {
            return Sql.Insert(new AggregateSetMemberRow { Series = series, Family = family, Measure = measure, Member = member })
                .OnConflict(row => new { row.Series, row.Measure, row.Member })
                .DoNothing()
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Deletes only one series and measure membership population.
        /// </summary>
        /// <param name="series">The exact dimension-series identity.</param>
        /// <param name="measure">The stable measure ordinal.</param>
        /// <returns>The number of members deleted from this specific measure.</returns>
        public static SqlQuery<int> DeleteSetMembers(long series, int measure)
        {
            return Sql.From<AggregateSetMemberRow>()
                .Where(row => row.Series == series && row.Measure == measure)
                .Delete();
        }

        /// <summary>
        /// Reads sufficient ordered state using its complete family, scope and tuple key.
        /// </summary>
        /// <param name="family">The registered aggregate family identity.</param>
        /// <param name="scope">The interned lifetime, attempt or battle scope.</param>
        /// <param name="tuple">The interned ordered dimension tuple.</param>
        /// <returns>The selected exact values, or null when no matching row exists.</returns>
        public static SqlQuery<OrderedStateRecord?> ReadOrdered(int family, long scope, long tuple)
        {
            return Sql.From<OrderedStateRow>()
                .Where(row => row.Family == family && row.Scope == scope && row.Tuple == tuple)
                .Select(row => new OrderedStateRecord { Payload = row.Payload })
                .FirstOrDefault();
        }

        /// <summary>
        /// Upserts complete sufficient state while retaining unassigned context metadata.
        /// </summary>
        /// <param name="family">The registered aggregate family identity.</param>
        /// <param name="scope">The interned lifetime, attempt or battle scope.</param>
        /// <param name="tuple">The interned ordered dimension tuple.</param>
        /// <param name="payload">The complete compressed sufficient state.</param>
        /// <param name="stream">The exact sixteen-byte producer stream identity.</param>
        /// <param name="sequence">The exact eight-byte source sequence.</param>
        /// <param name="revision">The exact eight-byte collection revision.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> SaveOrdered(int family, long scope, long tuple, byte[] payload, byte[] stream, byte[] sequence, byte[] revision)
        {
            return Sql.Insert(new OrderedStateRow { Family = family, Scope = scope, Tuple = tuple, Format = 2, Codec = _orderedCodec, Payload = payload, Stream = stream, Sequence = sequence, Revision = revision })
                .OnConflict(row => new { row.Family, row.Scope, row.Tuple })
                .UpdateFromInserted(row => new { row.Payload, row.Format, row.Codec, row.Revision, row.Stream, row.Sequence })
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Resolves a hot partition and updates only its latest collection revision.
        /// </summary>
        /// <param name="family">The registered aggregate family identity.</param>
        /// <param name="scope">The interned lifetime, attempt or battle scope.</param>
        /// <param name="revision">The exact eight-byte latest collection revision.</param>
        /// <returns>The signed 64-bit matching identity, whether the row was inserted or already existed.</returns>
        public static SqlQuery<long> TouchPartition(int family, long scope, byte[] revision)
        {
            return Sql.Insert(new HotPartitionRow { Family = family, Scope = scope, Revision = revision })
                .OnConflict(row => new { row.Family, row.Scope })
                .UpdateFromInserted(row => row.Revision)
                .ResolveId(row => row.Id);
        }

        /// <summary>
        /// Interns an aggregate series while retaining its original family identity.
        /// </summary>
        /// <param name="partition">The owning hot partition identity.</param>
        /// <param name="family">The registered aggregate family identity.</param>
        /// <param name="tuple">The interned ordered dimension tuple.</param>
        /// <returns>The signed 64-bit matching identity, whether the row was inserted or already existed.</returns>
        public static SqlQuery<long> InternSeries(long partition, int family, long tuple)
        {
            return Sql.Insert(new AggregateSeriesRow { Partition = partition, Family = family, Tuple = tuple })
                .OnConflict(row => new { row.Partition, row.Tuple })
                .DoNothing()
                .ResolveId(row => row.Id);
        }
    }
}
