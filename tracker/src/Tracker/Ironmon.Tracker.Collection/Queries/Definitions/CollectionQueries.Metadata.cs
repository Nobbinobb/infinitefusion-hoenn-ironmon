using Ironmon.Sqlite.Querying;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Defines generated metadata storage operations.
/// </summary>
internal static partial class CollectionQueries
{
    private const string _freshTrackingCoverage = "fresh_at_release";
    private const string _tableObjectKind = "table";
    private const string _collectionMetaTable = "collection_meta";

    /// <summary>
    /// Declares typed metadata queries for compile-time generation.
    /// </summary>
    private static partial class Definitions
    {
        /// <summary>
        /// Reads only the original singleton profile, epoch, incarnation and collection revision.
        /// </summary>
        /// <returns>The exact selected columns, or null when absent.</returns>
        public static SqlQuery<CollectionMetadataRecord?> ReadCollectionMetadata()
        {
            return Sql.From<CollectionMetadataRow>()
                .Where(row => row.Id == 1)
                .Select(row => new CollectionMetadataRecord { Profile = row.Profile, Epoch = row.Epoch, Incarnation = row.Incarnation, Revision = row.Revision })
                .FirstOrDefault();
        }

        /// <summary>
        /// Binds the original fresh-release profile and epoch with every required metadata field.
        /// </summary>
        /// <param name="profile">The exact sixteen-byte collection profile identity.</param>
        /// <param name="epoch">The exact sixteen-byte collection epoch identity.</param>
        /// <param name="incarnation">The exact sixteen-byte store incarnation identity.</param>
        /// <param name="now">The recording time in UTC Unix milliseconds.</param>
        /// <param name="revision">The exact eight-byte collection revision.</param>
        /// <returns>The provider affected-row count, including zero for suppressed duplicates.</returns>
        public static SqlQuery<int> InsertCollectionMetadata(byte[] profile, byte[] epoch, byte[] incarnation, long now, byte[] revision)
        {
            return Sql.Insert(new CollectionMetadataRow { Id = 1, Profile = profile, Epoch = epoch, Incarnation = incarnation, Version = 3, Created = now, Revision = revision, MinimumReader = 3, Coverage = _freshTrackingCoverage })
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Reads the retained schema version and exact revision before selecting a migration path.
        /// </summary>
        /// <returns>The exact selected columns, or null when absent.</returns>
        public static SqlQuery<StorageVersionRecord?> ReadStorageVersion()
        {
            return Sql.From<CollectionMetadataRow>()
                .Where(row => row.Id == 1)
                .Select(row => new StorageVersionRecord { Version = row.Version, Revision = row.Revision })
                .FirstOrDefault();
        }

        /// <summary>
        /// Counts the exact collection schema anchor before initialization.
        /// </summary>
        /// <returns>The original typed result within the caller-owned transaction.</returns>
        public static SqlQuery<long> HasCollectionSchema()
        {
            return Sql.From<StoreSchemaLookupRow>().Where(row => row.Type == _tableObjectKind && row.Name == _collectionMetaTable).Count();
        }

        /// <summary>
        /// Advances the collection revision inside its atomic receipt transaction.
        /// </summary>
        /// <param name="revision">The exact revision value retained by the original operation.</param>
        /// <returns>The original typed result within the caller-owned transaction.</returns>
        public static SqlQuery<int> SaveCollectionRevision(byte[] revision)
        {
            return Sql.From<CollectionMetadataRow>().Where(row => row.Id == 1)
                .Update(row => new CollectionMetadataRow { Revision = revision }).ExecuteNonQuery();
        }
    }
}
