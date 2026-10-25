using Ironmon.Sqlite.Definitions;
using Ironmon.Tracker.Collection.Queries.Results;
using Ironmon.Tracker.Collection.Queries.Tables;

namespace Ironmon.Tracker.Collection.Queries.Definitions;

/// <summary>
/// Defines generated migrations storage operations.
/// </summary>
internal static partial class CollectionQueries
{
    private const string _maintenanceOldFamily = "closed_family_v1";
    private const string _maintenanceItemMetadata = "item_identity_metadata";
    private const string _maintenanceScopeV2 = "closed_scope_v2";
    private const string _maintenanceScopeV3 = "closed_scope_v3";
    private const string _maintenanceCompressed = "zlib";
    private const string _maintenanceOrderedCodec = "zlib-value-v2";

    /// <summary>
    /// Declares typed migrations queries for compile-time generation.
    /// </summary>
    private static partial class Definitions
    {
        /// <summary>
        /// Advances the retained schema and reader versions after a successful migration.
        /// </summary>
        /// <returns>The number of metadata rows updated.</returns>
        public static SqlQuery<int> CompleteStorageMigration()
        {
            return Sql.From<CollectionMetadataRow>().Where(row => row.Id == 1).Update(row => new CollectionMetadataRow { Version = 3, MinimumReader = 3 }).ExecuteNonQuery();
        }

        /// <summary>
        /// Replaces one original scope key with its lossless compact representation.
        /// </summary>
        /// <param name="id">The complete scope identity.</param>
        /// <param name="key">The compact key bytes.</param>
        /// <returns>The number of rows updated.</returns>
        public static SqlQuery<int> SaveScopeMigrationKey(long id, byte[] key)
        {
            return Sql.From<CollectionScopeRow>().Where(row => row.Id == id).Update(row => new CollectionScopeRow { Key = key }).ExecuteNonQuery();
        }

        /// <summary>
        /// Replaces one original context key and its full lookup digest.
        /// </summary>
        /// <param name="id">The complete context identity.</param>
        /// <param name="key">The lossless compact key bytes.</param>
        /// <param name="digest">The complete SHA-256 digest.</param>
        /// <returns>The number of rows updated.</returns>
        public static SqlQuery<int> SaveContextMigrationKey(long id, byte[] key, byte[] digest)
        {
            return Sql.From<CollectionContextRow>().Where(row => row.Id == id).Update(row => new CollectionContextRow { Key = key, Digest = digest }).ExecuteNonQuery();
        }

        /// <summary>
        /// Replaces one retained individual's exact logical key without changing its identity.
        /// </summary>
        /// <param name="id">The complete owned-individual identity.</param>
        /// <param name="key">The lossless compact key bytes.</param>
        /// <returns>The number of rows updated.</returns>
        public static SqlQuery<int> SaveOwnedMigrationKey(long id, byte[] key)
        {
            return Sql.From<OwnedIndividualRow>().Where(row => row.Id == id).Update(row => new OwnedIndividualRow { Key = key }).ExecuteNonQuery();
        }

        /// <summary>
        /// Selects historical detail bodies while retaining the three independently handled representations.
        /// </summary>
        /// <returns>The complete selected block identities.</returns>
        public static SqlQuery<IEnumerable<MaintenanceIdentityRecord>> FindDetailMigrationBlocks()
        {
            return Sql.From<MaintenanceBlockViewRow>().Where(row => row.Kind != _maintenanceOldFamily && row.Kind != _maintenanceDefinition && row.Kind != _maintenanceItemMetadata).Select(row => new MaintenanceIdentityRecord { Id = row.Id }).AsEnumerable();
        }

        /// <summary>
        /// Replaces one historical detail body while preserving its stable references.
        /// </summary>
        /// <param name="id">The retained block identity.</param>
        /// <param name="length">The complete uncompressed body length in bytes.</param>
        /// <param name="digest">The SHA-256 digest of the complete uncompressed body.</param>
        /// <param name="payload">The compressed lossless replacement body.</param>
        /// <returns>The number of rows updated.</returns>
        public static SqlQuery<int> SaveMigratedDetailBlock(long id, long length, byte[] digest, byte[] payload)
        {
            return Sql.From<ImmutableBlockRow>().Where(row => row.Id == id).Update(row => new ImmutableBlockRow { Format = 2, Codec = _maintenanceCompressed, Length = length, Digest = digest, Payload = payload }).ExecuteNonQuery();
        }

        /// <summary>
        /// Selects each historical closed scope once in unchanged numeric order.
        /// </summary>
        /// <returns>Distinct complete scope identities in ascending order.</returns>
        public static SqlQuery<IEnumerable<MaintenanceIdentityRecord>> FindMigrationScopes()
        {
            return Sql.From<ColdPartitionRow>().Distinct().OrderBy(row => row.Scope).Select(row => new MaintenanceIdentityRecord { Id = row.Scope }).AsEnumerable();
        }

        /// <summary>
        /// Replaces a hot ordered body without changing its full historical natural key.
        /// </summary>
        /// <param name="family">The complete family identity.</param>
        /// <param name="scope">The complete scope identity.</param>
        /// <param name="tuple">The complete dimension tuple identity.</param>
        /// <param name="payload">The compressed compact state body.</param>
        /// <returns>The number of rows updated.</returns>
        public static SqlQuery<int> SaveMigratedOrderedState(long family, long scope, long tuple, byte[] payload)
        {
            return Sql.From<MaintenanceOrderedRow>().Where(row => row.Family == family && row.Scope == scope && row.Tuple == tuple).Update(row => new MaintenanceOrderedRow { Format = 2, Codec = _maintenanceOrderedCodec, Payload = payload }).ExecuteNonQuery();
        }

        /// <summary>
        /// Selects version-two closed scope bodies through their compatibility view.
        /// </summary>
        /// <returns>The complete identities of bodies requiring archive encoding.</returns>
        public static SqlQuery<IEnumerable<MaintenanceIdentityRecord>> FindCompactArchiveBlocks()
        {
            return Sql.From<MaintenanceBlockViewRow>().Where(row => (row.Kind == _maintenanceScopeV2 || row.Kind == _maintenanceScopeV3) && row.Format == 2).Select(row => new MaintenanceIdentityRecord { Id = row.Id }).AsEnumerable();
        }

        /// <summary>
        /// Replaces an archive body while preserving every original block reference.
        /// </summary>
        /// <param name="id">The retained immutable block identity.</param>
        /// <param name="kind">The version-three archive kind identity.</param>
        /// <param name="codec">The measured raw or compressed representation.</param>
        /// <param name="length">The complete uncompressed body length in bytes.</param>
        /// <param name="digest">The complete SHA-256 body digest.</param>
        /// <param name="payload">The complete retained archive body.</param>
        /// <returns>The number of rows updated.</returns>
        public static SqlQuery<int> SaveCompactArchiveBlock(long id, long kind, string codec, long length, byte[] digest, byte[] payload)
        {
            return Sql.From<ImmutableBlockRow>().Where(row => row.Id == id).Update(row => new ImmutableBlockRow { BlockKind = kind, Format = 3, Codec = codec, Length = length, Digest = digest, Payload = payload }).ExecuteNonQuery();
        }

        /// <summary>
        /// Streams complete context identities and original canonical keys before in-place migration.
        /// </summary>
        /// <returns>Matching original rows; enumeration owns and disposes the command and reader.</returns>
        public static SqlQuery<IEnumerable<KeyMigrationRecord>> FindContextMigrationKeys()
        {
            return Sql.From<CollectionContextRow>()
                .Select(row => new KeyMigrationRecord { Id = row.Id, Key = row.Key })
                .AsEnumerable();
        }

        /// <summary>
        /// Streams complete owned individual identities and original entity keys before migration.
        /// </summary>
        /// <returns>Matching original rows; enumeration owns and disposes the command and reader.</returns>
        public static SqlQuery<IEnumerable<KeyMigrationRecord>> FindOwnedMigrationKeys()
        {
            return Sql.From<OwnedIndividualRow>()
                .Select(row => new KeyMigrationRecord { Id = row.Id, Key = row.Key })
                .AsEnumerable();
        }

        /// <summary>
        /// Streams only the original five scope kinds whose JSON keys require value migration.
        /// </summary>
        /// <returns>Matching original rows; enumeration owns and disposes the command and reader.</returns>
        public static SqlQuery<IEnumerable<KeyMigrationRecord>> FindScopeMigrationKeys()
        {
            return Sql.From<CollectionScopeRow>()
                .Where(row => row.Kind == _keyBattleScope || row.Kind == _keyIndividualScope || row.Kind == _keySessionScope || row.Kind == _keySaveLineageScope || row.Kind == _keyWardrobeSessionScope)
                .Select(row => new KeyMigrationRecord { Id = row.Id, Key = row.Key })
                .AsEnumerable();
        }

        /// <summary>
        /// Streams complete original ordered migration rows without narrowing their stored identities.
        /// </summary>
        /// <returns>Matching original rows; enumeration owns and disposes the command and reader.</returns>
        public static SqlQuery<IEnumerable<OrderedMigrationRecord>> FindOrderedMigrationRows()
        {
            return Sql.From<OrderedMigrationRow>()
                .Select(row => new OrderedMigrationRecord { Family = row.Family, Scope = row.Scope, Tuple = row.Tuple, Payload = row.Payload })
                .AsEnumerable();
        }

        /// <summary>
        /// Streams complete legacy summary JSON in original identity order before schema compaction.
        /// </summary>
        /// <returns>Matching original rows; enumeration owns and disposes the command and reader.</returns>
        public static SqlQuery<IEnumerable<HistoricalDefinitionRecord>> FindLegacyGroupDefinitions()
        {
            return Sql.From<LegacySummaryDefinitionRow>()
                .OrderBy(row => row.Id)
                .Select(row => new HistoricalDefinitionRecord { Id = row.Id, Value = row.Definition })
                .AsEnumerable();
        }

        /// <summary>
        /// Streams complete legacy relationship JSON in original identity order before schema compaction.
        /// </summary>
        /// <returns>Matching original rows; enumeration owns and disposes the command and reader.</returns>
        public static SqlQuery<IEnumerable<HistoricalDefinitionRecord>> FindLegacyContractDefinitions()
        {
            return Sql.From<LegacyContractDefinitionRow>()
                .OrderBy(row => row.Id)
                .Select(row => new HistoricalDefinitionRecord { Id = row.Id, Value = row.Definition })
                .AsEnumerable();
        }
    }
}
