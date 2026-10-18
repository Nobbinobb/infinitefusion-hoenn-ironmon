using Ironmon.Sqlite.Querying;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Defines generated inspector storage operations.
/// </summary>
internal static partial class CollectionQueries
{
    /// <summary>
    /// Declares typed inspector queries for compile-time generation.
    /// </summary>
    private static partial class Definitions
    {
        private const string _inspectorTableType = "table";
        private const string _inspectorInternalTablePattern = "sqlite_%";

        /// <summary>
        /// Streams the original physical user table names in SQLite name order.
        /// </summary>
        /// <returns>Physical user tables whose counts are inspected in the same read snapshot.</returns>
        public static SqlQuery<IEnumerable<InspectorTableRecord>> FindInspectorTables()
        {
            return Sql.From<InspectorSchemaRow>()
                .Where(row => row.Type == _inspectorTableType && !Sql.Like(row.Name, _inspectorInternalTablePattern))
                .OrderBy(row => row.Name)
                .Select(row => new InspectorTableRecord { Name = row.Name })
                .AsEnumerable();
        }

        /// <summary>
        /// Streams native correlated family row counts without transferring every counted row to managed memory.
        /// </summary>
        /// <returns>Family keys and complete hot-series, hot-partition and cold-partition counts in family identity order.</returns>
        public static SqlQuery<IEnumerable<InspectorFamilyRecord>> FindInspectorFamilies()
        {
            return Sql.From<AggregateFamilyRow>()
                .OrderBy(row => row.Id)
                .Select(row => new InspectorFamilyRecord { Key = row.Key, Series = Sql.Count<AggregateSeriesRow>(series => series.Family == row.Id), Hot = Sql.Count<HotPartitionRow>(partition => partition.Family == row.Id), Cold = Sql.Count<ColdPartitionRow>(partition => partition.Family == row.Id) })
                .AsEnumerable();
        }

        /// <summary>
        /// Streams exact stream receipt bytes in the original binary stream identity order.
        /// </summary>
        /// <returns>Stream identities and their retained unsigned receipt/revision representations.</returns>
        public static SqlQuery<IEnumerable<InspectorReceiptRecord>> FindInspectorReceipts()
        {
            return Sql.From<StreamReceiptRow>()
                .OrderBy(row => row.Stream)
                .Select(row => new InspectorReceiptRecord { Stream = row.Stream, Sequence = row.Sequence, Revision = row.Revision })
                .AsEnumerable();
        }

        /// <summary>
        /// Reads the original operational counter singleton without manufacturing an absent row.
        /// </summary>
        /// <returns>The complete counter/timing projection, or null before it has been recorded.</returns>
        public static SqlQuery<InspectorOperationsRecord?> ReadInspectorOperations()
        {
            return Sql.From<InspectorOperationsRow>()
                .Where(row => row.Id == 1)
                .Select(row => new InspectorOperationsRecord { Batches = row.Batches, Events = row.Events, Duplicates = row.Duplicates, Bytes = row.Bytes, CommitMilliseconds = row.CommitMilliseconds, Updated = row.Updated })
                .FirstOrDefault();
        }

        /// <summary>
        /// Reads the original schema/revision singleton without exporting profile identifiers.
        /// </summary>
        /// <returns>The stored schema version and exact revision bytes, or null during initialization.</returns>
        public static SqlQuery<InspectorMetadataRecord?> ReadInspectorMetadata()
        {
            return Sql.From<CollectionMetadataRow>()
                .Where(row => row.Id == 1)
                .Select(row => new InspectorMetadataRecord { Version = row.Version, Revision = row.Revision })
                .FirstOrDefault();
        }

        /// <summary>
        /// Streams the original native coverage union using indexed joins and retaining duplicate scalar/set evidence.
        /// </summary>
        /// <returns>Every joined hot scalar and exact-set coverage population in its original storage class.</returns>
        public static SqlQuery<IEnumerable<InspectorCoverageRecord>> FindInspectorCoverage()
        {
            return Sql.From<AggregateCellRow>()
                .Join<AggregateSeriesRow>((cell, series) => series.Id == cell.Series)
                .Join<HotPartitionRow>((cell, series, partition) => partition.Id == series.Partition)
                .Join<CollectionScopeRow>((cell, series, partition, scope) => scope.Id == partition.Scope)
                .Join<AggregateFamilyRow>((cell, series, partition, scope, family) => family.Id == series.Family)
                .Join<FamilyMeasureRow>((cell, series, partition, scope, family, measure) => measure.Family == cell.Family && measure.Id == cell.Measure)
                .Select((cell, series, partition, scope, family, measure) => new InspectorCoverageRecord { Family = family.Key, Measure = measure.Key, Scope = scope.Kind, Known = cell.Known, Unknown = cell.Unknown, Unsupported = cell.Unsupported, NotApplicable = cell.NotApplicable })
                .UnionAll(Sql.From<AggregateSetCoverageRow>()
                    .Join<AggregateSeriesRow>((cell, series) => series.Id == cell.Series)
                    .Join<HotPartitionRow>((cell, series, partition) => partition.Id == series.Partition)
                    .Join<CollectionScopeRow>((cell, series, partition, scope) => scope.Id == partition.Scope)
                    .Join<AggregateFamilyRow>((cell, series, partition, scope, family) => family.Id == series.Family)
                    .Join<FamilyMeasureRow>((cell, series, partition, scope, family, measure) => measure.Family == cell.Family && measure.Id == cell.Measure)
                    .Select((cell, series, partition, scope, family, measure) => new InspectorCoverageRecord { Family = family.Key, Measure = measure.Key, Scope = scope.Kind, Known = cell.Known, Unknown = cell.Unknown, Unsupported = cell.Unsupported, NotApplicable = cell.NotApplicable }))
                .AsEnumerable();
        }

        /// <summary>
        /// Accumulates original operational counters while replacing only the latest timing fields.
        /// </summary>
        /// <param name="events">The exact events value retained by the original operation.</param>
        /// <param name="duplicates">The exact duplicates value retained by the original operation.</param>
        /// <param name="bytes">The exact bytes value retained by the original operation.</param>
        /// <param name="milliseconds">The exact milliseconds value retained by the original operation.</param>
        /// <param name="now">The exact now value retained by the original operation.</param>
        /// <returns>The original typed result within the caller-owned transaction.</returns>
        public static SqlQuery<int> RecordCollectionOperation(long events, long duplicates, long bytes, double milliseconds, long now)
        {
            return Sql.Insert(new StoreOperationWriteRow { Id = 1, Batches = 1, Events = events, Duplicates = duplicates, Bytes = bytes, Milliseconds = milliseconds, Updated = now })
                .OnConflict(row => row.Id)
                .Update((current, incoming) => new StoreOperationWriteRow { Batches = current.Batches + 1, Events = current.Events + incoming.Events, Duplicates = current.Duplicates + incoming.Duplicates, Bytes = current.Bytes + incoming.Bytes, Milliseconds = incoming.Milliseconds, Updated = incoming.Updated })
                .ExecuteNonQuery();
        }
    }
}
