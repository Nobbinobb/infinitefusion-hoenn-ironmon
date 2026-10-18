using Ironmon.Sqlite.Querying;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Defines generated resources storage operations.
/// </summary>
internal static partial class CollectionQueries
{
    /// <summary>
    /// Declares typed resources queries for compile-time generation.
    /// </summary>
    private static partial class Definitions
    {
        /// <summary>
        /// Replaces only the exact current container snapshot and source metadata.
        /// </summary>
        /// <param name="family">The registered aggregate family identity.</param>
        /// <param name="scope">The interned exact scope identity.</param>
        /// <param name="container">The interned exact inventory container identity.</param>
        /// <param name="payload">The complete compressed current inventory snapshot.</param>
        /// <param name="stream">The exact sixteen-byte source stream identity.</param>
        /// <param name="sequence">The exact eight-byte contiguous source frontier.</param>
        /// <param name="revision">The exact eight-byte collection revision.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> SaveCurrentInventory(int family, long scope, long container, byte[] payload, byte[] stream, byte[] sequence, byte[] revision)
        {
            return Sql.Insert(new CurrentInventoryRow { Family = family, Scope = scope, Container = container, Format = 1, Payload = payload, Stream = stream, Sequence = sequence, Revision = revision })
                .OnConflict(row => new { row.Family, row.Scope, row.Container })
                .UpdateFromInserted(row => new { row.Payload, row.Stream, row.Sequence, row.Revision })
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Upserts only the parent at the exact child resource and ordinal.
        /// </summary>
        /// <param name="resource">The interned exact child resource identity.</param>
        /// <param name="ordinal">The zero-based parent relationship ordinal.</param>
        /// <param name="parent">The interned exact parent resource identity.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> SaveResourceParent(long resource, int ordinal, long parent)
        {
            return Sql.Insert(new ResourceParentRow { Id = resource, Ordinal = ordinal, Parent = parent })
                .OnConflict(row => new { row.Id, row.Ordinal })
                .UpdateFromInserted(row => row.Parent)
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Retains original item ownership and preserves disclosed provenance when later evidence is absent.
        /// </summary>
        /// <param name="id">The exact id value retained by the original operation.</param>
        /// <param name="item">The exact item value retained by the original operation.</param>
        /// <param name="origin">The exact origin value retained by the original operation.</param>
        /// <param name="controller">The exact controller value retained by the original operation.</param>
        /// <param name="location">The exact location value retained by the original operation.</param>
        /// <param name="quantity">The exact quantity value retained by the original operation.</param>
        /// <param name="block">The exact block value retained by the original operation.</param>
        /// <param name="stream">The exact stream value retained by the original operation.</param>
        /// <param name="sequence">The exact sequence value retained by the original operation.</param>
        /// <param name="revision">The exact revision value retained by the original operation.</param>
        /// <returns>The original typed result within the caller-owned transaction.</returns>
        public static SqlQuery<int> SaveResourceLot(long id, long item, long? origin, long? controller, long? location, long quantity, long block, byte[] stream, byte[] sequence, byte[] revision)
        {
            return Sql.Insert(new ResourceLotWriteRow { Id = id, Item = item, Origin = origin, Controller = controller, Location = location, Quantity = quantity, Block = block, Stream = stream, Sequence = sequence, Revision = revision })
                .OnConflict(row => row.Id)
                .Update((current, incoming) => new ResourceLotWriteRow { Origin = incoming.Origin ?? current.Origin, Controller = incoming.Controller ?? current.Controller, Location = incoming.Location ?? current.Location, Quantity = incoming.Quantity, Block = incoming.Block, Stream = incoming.Stream, Sequence = incoming.Sequence, Revision = incoming.Revision })
                .ExecuteNonQuery();
        }
    }
}
