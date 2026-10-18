using Ironmon.Sqlite.Querying;
using Ironmon.Tracker.Collection;
using Microsoft.Data.Sqlite;
using System.Reflection;
using System.Text.Json;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Exercises generated native maintenance against independent schema fixtures and hostile metadata identifiers.
/// </summary>
public sealed class GeneratedSqliteMaintenanceTests
{
    private const string _memory = ":memory:";
    private const string _resourcePrefix = "Ironmon.Tracker.Tests.Collection.Fixtures.GeneratedMaintenance.";
    private const string _seed = "Seed.sql";
    private const string _rebuild = "Rebuild.sql";
    private const string _brokenForeignKey = "BrokenForeignKey.sql";
    private const string _sentinel = "sentinel";
    private const string _hostileTable = "quoted\"; DROP TABLE sentinel;--";
    private const string _hostileColumn = "block\"; DROP TABLE sentinel;--";
    private const string _hostileTrigger = "trigger\"; DROP TABLE sentinel;--";
    private const string _hostileIndex = "index\"; DROP TABLE sentinel;--";
    private const string _original = "maintenance_original";
    private const string _replacement = "maintenance_replacement";
    private const string _renamed = "renamed\"; DROP TABLE sentinel;--";
    private const string _child = "child_references";
    private const string _detail = "detail";
    private const string _computed = "computed";
    private const string _foreignKeyFailure = "The disposable migration lost a retained reference.";
    private const string _invalidIdentifier = "sentinel; DROP TABLE sentinel;--";
    private const string _nullIdentifier = "sentinel\0";

    /// <summary>
    /// Treats punctuation and statement-shaped metadata names as one identifier in reads and reference counts.
    /// </summary>
    [Fact]
    public void MetadataReadsQuoteWholeIdentifiersAndPreserveFullWidthValues()
    {
        using SqliteConnection connection = Open();
        Assert.Equal(1, CollectionSqliteMaintenance.CountTableRows(connection, null, _hostileTable));
        Assert.Equal(1, CollectionSqliteMaintenance.CountColumnReferences(connection, null, _hostileTable, _hostileColumn, long.MaxValue));
        object[] row = Assert.Single(CollectionSqliteMaintenance.ReadTableRows(connection, null, _hostileTable));
        Assert.Equal(long.MaxValue, Assert.IsType<long>(row[0]));
        Assert.Equal(long.MaxValue, Assert.IsType<long>(row[1]));
        Assert.Equal(new byte[] { 0, 255, 128 }, Assert.IsType<byte[]>(row[2]));
        Assert.True(CollectionSqliteMaintenance.HasColumn(connection, null, _hostileTable, _hostileColumn));
        Assert.False(CollectionSqliteMaintenance.HasColumn(connection, null, _hostileTable, _invalidIdentifier));
        SqliteForeignKeyReference reference = Assert.Single(CollectionSqliteMaintenance.ReadForeignKeyReferences(connection, null, _child));
        Assert.Equal(_hostileTable, reference.Table);
        CollectionSqliteMaintenance.AssertForeignKeyIntegrity(connection, null, _foreignKeyFailure);
        Assert.Equal(1, CollectionSqliteMaintenance.CountTableRows(connection, null, _sentinel));
    }

    /// <summary>
    /// Rejects invalid identifier encodings and cannot execute statement text supplied as a table name.
    /// </summary>
    [Fact]
    public void InvalidIdentifiersDoNotExecuteAdditionalStatements()
    {
        using SqliteConnection connection = Open();
        Assert.Throws<ArgumentException>(() => CollectionSqliteMaintenance.CountTableRows(connection, null, string.Empty));
        Assert.Throws<ArgumentException>(() => CollectionSqliteMaintenance.CountTableRows(connection, null, _nullIdentifier));
        Assert.Throws<SqliteException>(() => CollectionSqliteMaintenance.CountTableRows(connection, null, _invalidIdentifier));
        Assert.Throws<SqliteException>(() => CollectionSqliteMaintenance.DropTable(connection, null, _invalidIdentifier));
        Assert.Equal(1, CollectionSqliteMaintenance.CountTableRows(connection, null, _sentinel));
    }

    /// <summary>
    /// Rebuilds all stored columns while retaining generated ordinals, extreme integers, nulls and binary values.
    /// </summary>
    [Fact]
    public void RebuildPreservesExactRowsAndRestoresCapturedMetadata()
    {
        using SqliteConnection connection = Open();
        IReadOnlyList<object[]> original = CollectionSqliteMaintenance.ReadTableRows(connection, null, _original);
        SqliteSchemaObject[] objects = [.. CollectionSqliteMaintenance.ReadMigrationObjects(connection, null).Where(row => row.Name == _hostileTrigger || row.Name == _hostileIndex)];
        Assert.Equal(2, objects.Length);
        using (SqliteTransaction transaction = connection.BeginTransaction())
        {
            CollectionSqliteMaintenance.DropTrigger(connection, transaction, _hostileTrigger);
            CollectionSqliteMaintenance.CreateRebuiltTable(connection, transaction, Read(_rebuild), _original, _replacement);
            IReadOnlyList<SqliteColumnInfo> allColumns = CollectionSqliteMaintenance.ReadTableColumns(connection, transaction, _replacement);
            Assert.Equal(2, Assert.Single(allColumns, column => column.Name == _computed).Hidden);
            SqliteColumnInfo[] stored = [.. allColumns.Where(column => column.Hidden == 0)];
            Assert.Equal(new long[] { 0, 1, 3 }, stored.Select(column => column.Ordinal));
            foreach (object[] row in original)
                CollectionSqliteMaintenance.InsertRebuiltRow(connection, transaction, _replacement, stored, row);

            CollectionSqliteMaintenance.DropTable(connection, transaction, _original);
            CollectionSqliteMaintenance.RenameTable(connection, transaction, _replacement, _original);
            foreach (SqliteSchemaObject schema in objects)
                CollectionSqliteMaintenance.ExecuteSchemaObject(connection, transaction, schema);

            transaction.Commit();
        }

        IReadOnlyList<object[]> rebuilt = CollectionSqliteMaintenance.ReadTableRows(connection, null, _original);
        Assert.Equal(Rows(original), Rows(rebuilt));
        Assert.Contains(CollectionSqliteMaintenance.ReadMigrationObjects(connection, null), row => row.Name == _hostileTrigger && row.Sql == objects.Single(value => value.Name == _hostileTrigger).Sql);
        Assert.Equal(1, CollectionSqliteMaintenance.CountTableRows(connection, null, _sentinel));
    }

    /// <summary>
    /// Leaves command and transaction ownership with the caller through schema rollback and hostile renaming.
    /// </summary>
    [Fact]
    public void MaintenanceDoesNotCommitOrCloseTheCallerTransaction()
    {
        using SqliteConnection connection = Open();
        string[] original = Rows(CollectionSqliteMaintenance.ReadTableRows(connection, null, _original));
        using (SqliteTransaction transaction = connection.BeginTransaction())
        {
            CollectionSqliteMaintenance.RenameTable(connection, transaction, _original, _renamed);
            Assert.Equal(3, CollectionSqliteMaintenance.CountTableRows(connection, transaction, _renamed));
            CollectionSqliteMaintenance.DropTable(connection, transaction, _renamed);
            transaction.Rollback();
        }

        Assert.Equal(original, Rows(CollectionSqliteMaintenance.ReadTableRows(connection, null, _original)));
        Assert.Equal(1, CollectionSqliteMaintenance.CountTableRows(connection, null, _sentinel));
    }

    /// <summary>
    /// Detects a retained foreign-key violation and restores enforcement after migration pragmas.
    /// </summary>
    [Fact]
    public void ForeignKeyAuditDetectsInvalidReferencesWithoutCommitting()
    {
        using SqliteConnection connection = Open();
        CollectionSqliteMaintenance.BeginMigrationPragmas(connection, null);
        Assert.Equal(0, CollectionSqliteMaintenance.ReadForeignKeys(connection, null));
        using (SqliteTransaction transaction = connection.BeginTransaction())
        {
            CollectionSqliteMaintenance.ExecuteSchemaDefinition(connection, transaction, Read(_brokenForeignKey));
            InvalidDataException error = Assert.Throws<InvalidDataException>(() => CollectionSqliteMaintenance.AssertForeignKeyIntegrity(connection, transaction, _foreignKeyFailure));
            Assert.Equal(_foreignKeyFailure, error.Message);
            transaction.Rollback();
        }

        CollectionSqliteMaintenance.RestoreMigrationPragmas(connection, null);
        Assert.Equal(1, CollectionSqliteMaintenance.ReadForeignKeys(connection, null));
        CollectionSqliteMaintenance.AssertForeignKeyIntegrity(connection, null, _foreignKeyFailure);
        Assert.Equal(1, CollectionSqliteMaintenance.CountTableRows(connection, null, _child));
    }

    /// <summary>
    /// Rejects attempts to bind generated columns or missing source ordinals before writing any row.
    /// </summary>
    [Fact]
    public void ReplacementInsertRejectsGeneratedOrMissingColumns()
    {
        using SqliteConnection connection = Open();
        CollectionSqliteMaintenance.CreateRebuiltTable(connection, null, Read(_rebuild), _original, _replacement);
        IReadOnlyList<SqliteColumnInfo> columns = CollectionSqliteMaintenance.ReadTableColumns(connection, null, _replacement);
        object[] source = CollectionSqliteMaintenance.ReadTableRows(connection, null, _original)[0];
        Assert.Throws<ArgumentException>(() => CollectionSqliteMaintenance.InsertRebuiltRow(connection, null, _replacement, columns, source));
        Assert.Throws<ArgumentException>(() => CollectionSqliteMaintenance.InsertRebuiltRow(connection, null, _replacement, [new SqliteColumnInfo(99, _detail, 0)], source));
        Assert.Equal(0, CollectionSqliteMaintenance.CountTableRows(connection, null, _replacement));
        Assert.Equal(3, CollectionSqliteMaintenance.CountTableRows(connection, null, _original));
    }

    /// <summary>
    /// Creates an independent strict in-memory database from the retained control schema fixture.
    /// </summary>
    /// <returns>An open caller-owned connection with foreign-key enforcement enabled.</returns>
    private static SqliteConnection Open()
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = _memory, ForeignKeys = true, Pooling = false }.ToString());
        connection.Open();
        CollectionSqliteMaintenance.ExecuteSchemaDefinition(connection, null, Read(_seed));
        return connection;
    }

    /// <summary>
    /// Reads independent schema and migration input without constructing command strings in test code.
    /// </summary>
    /// <param name="name">The embedded control fixture filename.</param>
    /// <returns>The exact retained control fixture text.</returns>
    private static string Read(string name)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(_resourcePrefix + name)!;
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Canonicalizes complete physical rows while preserving integer, text, null and binary representations.
    /// </summary>
    /// <param name="rows">The complete rows to compare independently of native scan order.</param>
    /// <returns>Sorted complete row serializations.</returns>
    private static string[] Rows(IReadOnlyList<object[]> rows)
        => [.. rows.Select(row => JsonSerializer.Serialize(row)).Order(StringComparer.Ordinal)];
}
