using Ironmon.Sqlite.Querying;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Migrates existing collected facts to typed value references and shared scope archives atomically.
/// </summary>
internal sealed partial class CollectionDatabase
{
    private const string _legacyNativeKeyColumn = "native_key";
    private const string _compactDefinitionsColumn = "definition_block_id";
    private const string _migrationTables = "identity_values";
    private const string _atomsTable = "direct_dimension_atoms";
    private const string _atomMembersTable = "direct_atom_members";
    private const string _directMembersTable = "direct_exact_set_members";
    private const string _temporaryTableSuffix = "_storage_v2";
    private const string _backupDirectoryName = "collection-backups";
    private const string _backupPrefix = "before-storage-v3-";
    private const string _backupExtension = ".sqlite";
    private const string _compactIdentifierFormat = "N";
    private const string _createTablePattern = "CREATE TABLE \\\"?{0}\\\"? \\([\\s\\S]*?\\) STRICT(?:, WITHOUT ROWID)?;";
    private const string _oldMemberColumn = "member_key";
    private const string _newMemberColumn = "member_value_id";
    private const string _triggerKind = "trigger";
    private const string _indexKind = "index";
    private const string _oldOrderedCodec = "zlib-json-v1";
    private const string _orderedValueCodec = "zlib-value-v2";
    private const string _archiveFormatProperty = "format";

    /// <summary>
    /// Upgrades storage once after an independent consistent backup, preserving the profile and receipt identities.
    /// </summary>
    private void UpgradeStorage()
    {
        int version = 0;
        StorageVersionRecord? stored = CollectionQueries.ReadStorageVersion(_db, _transaction);
        if (stored is not null)
        {
            version = stored.Version;
            _revision = CollectionEncoding.ReadRevision(stored.Revision);
        }

        if (version > 3)
            throw new NotSupportedException("This collection storage requires a newer tracker reader.");

        bool legacy = CollectionSqliteMaintenance.HasColumn(_db, _transaction, _migrationTables, _legacyNativeKeyColumn);
        if (!legacy)
        {
            if (version == 2 || version == 0 && !CollectionSqliteMaintenance.HasColumn(_db, _transaction, _directGroupsTable, _compactDefinitionsColumn))
            {
                UpgradeVersionTwoStorage();
                return;
            }

            if (version is not (0 or 3))
                throw new InvalidDataException("Collection storage version does not match its physical tables.");

            if (!CollectionSqliteMaintenance.HasColumn(_db, _transaction, _directGroupsTable, _compactDefinitionsColumn))
                throw new InvalidDataException("The compact collection definitions are missing.");

            _values.EnableCompactStrings();
            if (CollectionSqliteMaintenance.ReadPageSize(_db, _transaction) != 1024)
                CompactDatabasePages();

            return;
        }

        if (version is not (0 or 1))
            throw new InvalidDataException("The legacy collection schema has an inconsistent version.");

        CreateStorageBackup();
        IReadOnlyList<SqliteSchemaObject> objects = CollectionSqliteMaintenance.ReadMigrationObjects(_db, _transaction);

        CollectionSqliteMaintenance.BeginMigrationPragmas(_db, _transaction);
        using SqliteTransaction migration = _db.BeginTransaction();
        _transaction = migration;
        try
        {
            CollectionSqliteMaintenance.CreateValueSchema(_db, _transaction);
            foreach (var item in objects.Where(item => item.Type == _triggerKind))
                CollectionSqliteMaintenance.DropTrigger(_db, _transaction, item.Name);

            CollectionSqliteMaintenance.LegacyDropIdentityView(_db, _transaction);
            RebuildStorageTable(_migrationTables, row => { row[2] = InternLegacy(row[2]); return row; });
            RebuildStorageTable(_atomsTable, row =>
            {
                row[8] = InternLegacy(row[8]);
                if ((string)row[1] == _nativeKind && row[5] is byte[] native)
                    row[5] = EncodeLegacy(native);

                return row;
            });
            RebuildStorageTable(_atomMembersTable, row => { row[3] = InternLegacy(row[3]); return row; });
            RebuildStorageTable(_directMembersTable, row => { row[6] = InternLegacy(row[6]); return row; });
            CollectionSqliteMaintenance.LegacyRestoreIdentityView(_db, _transaction);
            foreach (var item in objects.Where(item => item.Type == _indexKind && IsRebuiltIndex(item.Sql)))
                CollectionSqliteMaintenance.ExecuteSchemaObject(_db, _transaction, new SqliteSchemaObject(item.Type, item.Name, item.Sql.Replace(_oldMemberColumn, _newMemberColumn, StringComparison.Ordinal)));

            MigrateValueKeys();
            MigrateDetailBlocks();
            MigrateOrderedRows();
            List<long> scopes = [.. CollectionQueries.FindMigrationScopes(_db, _transaction).Select(row => row.Id)];

            foreach (long scope in scopes)
                PublishScope(scope, []);

            RecloseContinuations();
            UpgradeCompactRepresentations();
            foreach (var item in objects.Where(item => item.Type == _indexKind && _compactStorageTables.Any(table => item.Sql.Contains(table, StringComparison.Ordinal))))
                CollectionSqliteMaintenance.ExecuteSchemaObject(_db, _transaction, new SqliteSchemaObject(item.Type, item.Name, item.Sql.Replace(_oldMemberColumn, _newMemberColumn, StringComparison.Ordinal)));

            foreach (var item in objects.Where(item => item.Type == _triggerKind))
                CollectionSqliteMaintenance.ExecuteSchemaObject(_db, _transaction, new SqliteSchemaObject(item.Type, item.Name, item.Sql.Replace(_oldMemberColumn, _newMemberColumn, StringComparison.Ordinal)));

            CollectionQueries.CompleteStorageMigration(_db, _transaction);
            CollectionSqliteMaintenance.AssertForeignKeyIntegrity(_db, _transaction, "The collection migration did not preserve a foreign-key relationship.");

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
            _archiveReadCache.Clear();
            _replacedColdBlocks.Clear();
            CollectionSqliteMaintenance.RestoreMigrationPragmas(_db, _transaction);
        }

        CompactDatabasePages();
    }

    /// <summary>
    /// Saves the complete pre-migration database outside the actively measured collection directory.
    /// </summary>
    private void CreateStorageBackup()
    {
        string directory = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_databasePath))!, _backupDirectoryName);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, _backupPrefix + Guid.NewGuid().ToString(_compactIdentifierFormat) + _backupExtension);
        using SqliteConnection backup = new(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        backup.Open();
        _db.BackupDatabase(backup);
    }

    /// <summary>
    /// Rebuilds one affected physical table while preserving row IDs and all unrelated columns.
    /// </summary>
    /// <param name="table">The fixed production table name.</param>
    /// <param name="convert">Converts only the old representation columns in each complete row.</param>
    private void RebuildStorageTable(string table, Func<object[], object[]> convert)
    {
        IReadOnlyList<object[]> rows = CollectionSqliteMaintenance.ReadTableRows(_db, _transaction, table);
        string temporary = table + _temporaryTableSuffix;
        CollectionSqliteMaintenance.CreateRebuiltTable(_db, _transaction, StorageTableDefinition(table), table, temporary);
        SqliteColumnInfo[] storedColumns = [.. CollectionSqliteMaintenance.ReadTableColumns(_db, _transaction, temporary).Where(column => column.Hidden == 0)];
        foreach (object[] row in rows)
            CollectionSqliteMaintenance.InsertRebuiltRow(_db, _transaction, temporary, storedColumns, convert(row));

        CollectionSqliteMaintenance.DropTable(_db, _transaction, table);
        CollectionSqliteMaintenance.RenameTable(_db, _transaction, temporary, table);
    }

    /// <summary>
    /// Finds the canonical physical table definition used for fresh and migrated storage.
    /// </summary>
    /// <param name="table">The fixed canonical table name.</param>
    /// <returns>The complete strict SQLite table declaration.</returns>
    private static string StorageTableDefinition(string table)
    {
        using Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(_schemaResource)!;
        using StreamReader text = new(source);
        string pattern = string.Format(CultureInfo.InvariantCulture, _createTablePattern, Regex.Escape(table));
        Match definition = Regex.Match(text.ReadToEnd(), pattern, RegexOptions.CultureInvariant);
        if (!definition.Success)
            throw new InvalidDataException("An upgraded collection table definition is missing.");

        return definition.Value;
    }

    /// <summary>
    /// Identifies explicit indexes belonging to the tables rebuilt by this storage migration.
    /// </summary>
    /// <param name="sql">The original fixed index definition.</param>
    /// <returns>True when the index must be recreated after its table replacement.</returns>
    private static bool IsRebuiltIndex(string sql)
        => new[] { _migrationTables, _atomsTable, _atomMembersTable, _directMembersTable }.Any(table => sql.Contains(table, StringComparison.Ordinal));

    /// <summary>
    /// Interns an exact old canonical JSON key into immutable typed value records.
    /// </summary>
    /// <param name="bytes">The old JSON byte array.</param>
    /// <returns>The immutable value ID.</returns>
    private long InternLegacy(object bytes)
    {
        using JsonDocument document = JsonDocument.Parse((byte[])bytes);
        return _values.Intern(document.RootElement);
    }

    /// <summary>
    /// Encodes an exact old JSON value as a compact typed record body.
    /// </summary>
    /// <param name="bytes">The old canonical JSON bytes.</param>
    /// <returns>The version-2 binary body.</returns>
    private byte[] EncodeLegacy(byte[] bytes)
    {
        using JsonDocument document = JsonDocument.Parse(bytes);
        return _values.Encode(document.RootElement);
    }

    /// <summary>
    /// Replaces growing scope, context and owned-entity JSON keys with compact typed references.
    /// </summary>
    private void MigrateValueKeys()
    {
        (IEnumerable<KeyMigrationRecord> Rows, Action<long, byte[]> Save)[] migrations = [(CollectionQueries.FindScopeMigrationKeys(_db, _transaction), (id, key) => CollectionQueries.SaveScopeMigrationKey(_db, _transaction, id, key)), (CollectionQueries.FindContextMigrationKeys(_db, _transaction), (id, key) => CollectionQueries.SaveContextMigrationKey(_db, _transaction, id, key, SHA256.HashData(key))), (CollectionQueries.FindOwnedMigrationKeys(_db, _transaction), (id, key) => CollectionQueries.SaveOwnedMigrationKey(_db, _transaction, id, key))];
        foreach (var (stored, save) in migrations)
        {
            List<(long Id, byte[] Key)> rows = [.. stored.Select(row => (row.Id, row.Key))];
            foreach (var (id, raw) in rows)
                save(id, EncodeLegacy(raw));
        }
    }

    /// <summary>
    /// Re-encodes growing detail blocks while keeping their stable foreign-key IDs.
    /// </summary>
    private void MigrateDetailBlocks()
    {
        List<long> blocks = [.. CollectionQueries.FindDetailMigrationBlocks(_db, _transaction).Select(row => row.Id)];

        foreach (long block in blocks)
        {
            byte[] raw = _values.Encode(ReadStoredBlock(block));
            CollectionQueries.SaveMigratedDetailBlock(_db, _transaction, block, raw.LongLength, SHA256.HashData(raw), CollectionEncoding.Compress(raw));
        }

        _archiveReadCache.Clear();
    }

    /// <summary>
    /// Replaces existing hot ordered JSON state with the compact detail encoding.
    /// </summary>
    private void MigrateOrderedRows()
    {
        List<OrderedMigrationRecord> rows = [.. CollectionQueries.FindOrderedMigrationRows(_db, _transaction)];

        foreach (var row in rows)
            CollectionQueries.SaveMigratedOrderedState(_db, _transaction, row.Family, row.Scope, row.Tuple, CollectionEncoding.Compress(EncodeLegacy(CollectionEncoding.Inflate(row.Payload, _maximumBatchBytes))));
    }

    /// <summary>
    /// Upgrades ordered states embedded in an old closed-family frame without altering its facts.
    /// </summary>
    /// <param name="frame">The complete old or current family frame.</param>
    /// <returns>The frame with versioned compact ordered payloads.</returns>
    private JsonElement UpgradeArchiveOrdered(JsonElement frame)
    {
        JsonObject value = JsonNode.Parse(frame.GetRawText())!.AsObject();
        foreach (JsonArray row in value[_archiveOrderedProperty]!.AsArray().OfType<JsonArray>())
        {
            if (row[3]![1]!.GetValue<string>() != _oldOrderedCodec)
                continue;

            byte[] old = Convert.FromHexString(row[4]![1]!.GetValue<string>());
            byte[] encoded = CollectionEncoding.Compress(EncodeLegacy(CollectionEncoding.Inflate(old, _maximumBatchBytes)));
            row[2] = JsonSerializer.SerializeToNode(new object[] { _tagInteger, 2 });
            row[3] = JsonSerializer.SerializeToNode(new[] { _tagText, _orderedValueCodec });
            row[4] = JsonSerializer.SerializeToNode(new[] { _tagBlob, Convert.ToHexString(encoded) });
        }

        CompactPackedIntegers(value);
        value[_archiveFormatProperty] = 2;
        return JsonSerializer.SerializeToElement(value);
    }

    /// <summary>
    /// Replaces tagged decimal integer strings in old SQL frames with exact native numeric tokens.
    /// </summary>
    /// <param name="node">The bounded generated SQL frame, excluding opaque source payloads.</param>
    private static void CompactPackedIntegers(JsonNode? node)
    {
        if (node is JsonArray { Count: 2 } pair && pair[0] is JsonValue tag && tag.TryGetValue(out string? name) && name == _tagInteger && pair[1] is JsonValue number && number.TryGetValue(out string? digits))
        {
            pair[1] = long.Parse(digits, CultureInfo.InvariantCulture);
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array)
                CompactPackedIntegers(child);
        }
        else if (node is JsonObject value)
        {
            foreach (var child in value)
                CompactPackedIntegers(child.Value);
        }
    }
}
