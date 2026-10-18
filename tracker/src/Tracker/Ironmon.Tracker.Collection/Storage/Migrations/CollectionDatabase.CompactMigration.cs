using Ironmon.Sqlite.Querying;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Upgrades real collected facts to typed archives and compact relational metadata atomically.
/// </summary>
internal sealed partial class CollectionDatabase
{
    private const string _directGroupsTable = "direct_groups";
    private const string _contractsTable = "direct_relationship_contracts";
    private const string _dimensionReferencesTable = "direct_series_dimension_refs";
    private const string _tupleGroupsTable = "tuple_member_groups";
    private const string _tupleViewPattern = "CREATE VIEW tuple_members AS[\\s\\S]*?;";
    private const string _tupleIndexesPattern = "CREATE INDEX ix_tuple_group_identity_[0-3][\\s\\S]*?;";
    private const string _aggregateSeriesTable = "aggregate_series";
    private const string _aggregateCellsTable = "aggregate_cells";
    private const string _setMembersTable = "set_members";
    private const string _setCoverageTable = "aggregate_set_coverage";
    private const string _directScalarsTable = "direct_scalar_values";
    private const string _directExactMembersTable = "direct_exact_set_members";
    private const string _directRatiosTable = "direct_rational_extrema";
    private static readonly string[] _compactStorageTables = [_directGroupsTable, _contractsTable, _dimensionReferencesTable, _aggregateSeriesTable, _aggregateCellsTable, _setMembersTable, _setCoverageTable, _directScalarsTable, _directExactMembersTable, _directRatiosTable, _atomMembersTable];

    /// <summary>
    /// Migrates a version-2 database after an independent consistent backup and before accepting new input.
    /// </summary>
    private void UpgradeVersionTwoStorage()
    {
        CreateStorageBackup();
        IReadOnlyList<SqliteSchemaObject> objects = CollectionSqliteMaintenance.ReadMigrationObjects(_db, _transaction);

        CollectionSqliteMaintenance.BeginMigrationPragmas(_db, _transaction);
        using SqliteTransaction migration = _db.BeginTransaction();
        _transaction = migration;
        try
        {
            foreach (var item in objects.Where(item => item.Type == _triggerKind))
                CollectionSqliteMaintenance.DropTrigger(_db, _transaction, item.Name);

            UpgradeCompactRepresentations();
            foreach (var item in objects.Where(item => item.Type == _indexKind && _compactStorageTables.Any(table => item.Sql.Contains(table, StringComparison.Ordinal))))
                CollectionSqliteMaintenance.ExecuteSchemaObject(_db, _transaction, item);

            foreach (var item in objects.Where(item => item.Type == _triggerKind))
                CollectionSqliteMaintenance.ExecuteSchemaObject(_db, _transaction, item);

            CollectionQueries.CompleteStorageMigration(_db, _transaction);
            CollectionSqliteMaintenance.AssertForeignKeyIntegrity(_db, _transaction, "The compact migration did not preserve a foreign-key relationship.");

            migration.Commit();
        }
        catch
        {
            migration.Rollback();
            throw;
        }
        finally
        {
            _transaction = null;
            ClearCaches();
            CollectionSqliteMaintenance.RestoreMigrationPragmas(_db, _transaction);
        }

        CompactDatabasePages();
    }

    /// <summary>
    /// Changes only physical representations while retaining every directly indexed fact and full owned detail.
    /// </summary>
    private void UpgradeCompactRepresentations()
    {
        long catalogueBlock = MigrateCatalogueBlock();
        foreach (string table in _compactStorageTables)
        {
            RebuildStorageTable(table, row =>
            {
                if (table == _directGroupsTable || table == _contractsTable)
                {
                    int column = table == _directGroupsTable ? 6 : 4;
                    row[column] = catalogueBlock;
                }

                if (table == _dimensionReferencesTable)
                    return [.. row, DimensionKindCode((string)row[4])];

                return row;
            });
        }

        MigrateTupleGroups();
        RemoveUnusedDefinitionBlocks();
        CollectionSqliteMaintenance.CompactDimensionMembersView(_db, _transaction);
        List<long> archives = [.. CollectionQueries.FindCompactArchiveBlocks(_db, _transaction).Select(row => row.Id)];

        foreach (long block in archives)
        {
            byte[] raw = CollectionArchiveEncoding.Encode(ReadStoredBlock(block));
            byte[] compressed = CollectionEncoding.Compress(raw);
            bool smaller = compressed.Length < raw.Length;
            long kind = CollectionQueries.InternBlockKind(_db, _transaction, _coldBlockKind);
            CollectionQueries.SaveCompactArchiveBlock(_db, _transaction, block, kind, smaller ? _compressedBlockCodec : _rawBlockCodec, raw.LongLength, SHA256.HashData(raw), smaller ? compressed : raw);
        }

        _archiveReadCache.Clear();
        _values.CompactStoredStrings();
        ReclaimDictionaryValues();
    }

    /// <summary>
    /// Groups up to four ordered members per relational row while preserving sparse ordinals and reverse indexes.
    /// </summary>
    private void MigrateTupleGroups()
    {
        CollectionSqliteMaintenance.ExecuteSchemaDefinition(_db, _transaction, StorageTableDefinition(_tupleGroupsTable));
        CollectionSqliteMaintenance.CompactTupleMembers(_db, _transaction);
        using Stream source = typeof(CollectionDatabase).Assembly.GetManifestResourceStream(_schemaResource)!;
        using StreamReader text = new(source);
        string schema = text.ReadToEnd();
        CollectionSqliteMaintenance.ExecuteSchemaDefinition(_db, _transaction, System.Text.RegularExpressions.Regex.Match(schema, _tupleViewPattern).Value);
        foreach (System.Text.RegularExpressions.Match index in System.Text.RegularExpressions.Regex.Matches(schema, _tupleIndexesPattern))
            CollectionSqliteMaintenance.ExecuteSchemaDefinition(_db, _transaction, index.Value);
    }

    /// <summary>
    /// Releases old representation pages and chooses compact pages outside WAL mode before restoring durability.
    /// </summary>
    private void CompactDatabasePages()
    {
        CollectionSqliteMaintenance.CheckpointTruncate(_db, _transaction);
        try
        {
            CollectionSqliteMaintenance.UseRollbackJournal(_db, _transaction);
            CollectionSqliteMaintenance.SetCompactPageSize(_db, _transaction);
            CollectionSqliteMaintenance.Vacuum(_db, _transaction);
        }
        finally
        {
            CollectionSqliteMaintenance.ConfigureDurability(_db, _transaction);
        }

        if (CollectionSqliteMaintenance.ReadPageSize(_db, _transaction) != 1024)
            throw new InvalidOperationException("The compact SQLite page size was not applied.");

        CollectionSqliteMaintenance.CheckpointTruncate(_db, _transaction);
    }
}
