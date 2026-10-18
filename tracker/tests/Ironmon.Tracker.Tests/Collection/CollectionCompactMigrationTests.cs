using Ironmon.Tracker.Collection;
using Microsoft.Data.Sqlite;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Exercises successful and failed version-2 upgrades against independent historical table definitions.
/// </summary>
public sealed partial class CollectionStoreTests
{
    private const string _versionTwoSchema = "Ironmon.Tracker.Tests.Collection.Fixtures.StorageV2.sql";
    private const string _versionTwoMeta = "INSERT INTO collection_meta VALUES(1,$profile,$epoch,$stream,2,1234,$revision,2,'fresh_at_release')";
    private const string _versionTwoValue = "0123456789abcdef0123456789abcdef:pokemon:123";
    private const string _versionTwoInsert = "INSERT INTO identity_namespaces VALUES(1,'1','move'); INSERT INTO collection_values VALUES(1,5,$value,$digest); INSERT INTO identity_values VALUES(1,1,1,NULL)";
    private const string _versionTwoDigest = "$digest";
    private const string _pageSizeTestSql = "PRAGMA page_size";
    private const string _definitionTypeSql = "SELECT typeof(definition_block_id) FROM direct_groups LIMIT 1";
    private const string _integerStorageType = "integer";
    private const string _memberViewSql = "SELECT count(*) FROM direct_dimension_members";
    private const string _oldDefinitionSql = "SELECT count(*) FROM pragma_table_info('direct_groups') WHERE name='definition_json'";
    private const string _sparseOldMembersSql = "INSERT INTO tuples VALUES(1,1,X'01',10); INSERT INTO tuple_members VALUES(1,0,1),(1,3,1),(1,4,1),(1,9,1)";
    private const string _memberOrdinalsSql = "SELECT group_concat(ordinal,',') FROM (SELECT ordinal FROM tuple_members WHERE tuple_id=1 ORDER BY ordinal)";
    private const string _sparseOrdinals = "0,3,4,9";
    private const string _memberGroupCountSql = "SELECT count(*) FROM tuple_member_groups WHERE tuple_id=1";
    private const string _invalidMemberSql = "UPDATE tuple_member_groups SET identity_3=999999 WHERE tuple_id=1 AND group_no=0";
    private const string _configurationCountSql = "SELECT count(*) FROM configuration_revisions";

    /// <summary>
    /// Continues collecting, retries acknowledged input and republishes a completed attempt after a historical upgrade.
    /// </summary>
    /// <returns>Completes after verifying cumulative damage, complete owned details and database integrity.</returns>
    [Fact]
    public async Task VersionTwoUpgradeAcceptsNewFactsAndContinuesClosedAttemptAfterRestart()
    {
        using TestDirectory directory = new();
        string profile = CreateVersionTwoStorage(directory.Path, false);
        JsonElement first = Batch(_stream, [Occurrence(_damageEvent, Damage(40), 1), Occurrence(_ownedEvent, JsonSerializer.Deserialize<JsonElement>(_ownedPayload), 2), Occurrence(_attemptEndEvent, JsonSerializer.Deserialize<JsonElement>(_terminalPayload), 3)]);
        await using (GameplayCollectionStore store = new(profile))
            await store.AcceptBatchAsync(first);

        await using (GameplayCollectionStore reopened = new(profile))
        {
            await reopened.AcceptBatchAsync(first);
            await reopened.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, Damage(60), 4), Occurrence(_attemptEndEvent, JsonSerializer.Deserialize<JsonElement>(_terminalPayload), 5)]));
        }

        Assert.Equal(100L, ReadScalar(profile, _damageScalarSql));
        Assert.Equal(1L, ReadScalar(profile, _completedAttemptStateCountSql));
        Assert.Equal(1L, ReadScalar(profile, _configurationCountSql));
        AssertIntegrity(profile);
    }

    /// <summary>
    /// Preserves repeated identities and sparse ordered membership across four-member group boundaries.
    /// </summary>
    /// <returns>Completes after checking migration results and enforced identity foreign keys.</returns>
    [Fact]
    public async Task VersionTwoSparseTupleMembersRetainEveryOrdinalAndRealForeignKey()
    {
        using TestDirectory directory = new();
        string profile = CreateVersionTwoStorage(directory.Path, false);
        using (SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = System.IO.Path.Combine(profile, _databaseName), Pooling = false }.ToString()))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = _sparseOldMembersSql;
            command.ExecuteNonQuery();
        }

        await using (GameplayCollectionStore store = new(profile))
            await store.GetMeasurementsAsync();

        Assert.Equal(_sparseOrdinals, ReadScalar(profile, _memberOrdinalsSql));
        Assert.Equal(3L, ReadScalar(profile, _memberGroupCountSql));
        using SqliteConnection check = new(new SqliteConnectionStringBuilder { DataSource = System.IO.Path.Combine(profile, _databaseName), ForeignKeys = true, Pooling = false }.ToString());
        check.Open();
        using SqliteCommand invalid = check.CreateCommand();
        invalid.CommandText = _invalidMemberSql;
        Assert.Throws<SqliteException>(() => invalid.ExecuteNonQuery());
        AssertIntegrity(profile);
    }

    /// <summary>
    /// Preserves identity IDs and revisions while installing compact pages, readable views and compressed definitions.
    /// </summary>
    /// <returns>Completes after the migration and an idempotent reopen.</returns>
    [Fact]
    public async Task VersionTwoUpgradePreservesFactsAndKeepsOneOriginalBackup()
    {
        using TestDirectory directory = new();
        string profile = CreateVersionTwoStorage(directory.Path, false);
        await using (GameplayCollectionStore store = new(profile))
            await store.GetMeasurementsAsync();

        Assert.Equal(3L, ReadScalar(profile, _schemaVersionSql));
        Assert.Equal(1024L, ReadScalar(profile, _pageSizeTestSql));
        Assert.Equal(_versionTwoValue, ReadStoredValue(profile, 1).GetString());
        Assert.Equal(CollectionEncoding.Revision(42), Assert.IsType<byte[]>(ReadScalar(profile, _storedRevisionSql)));
        Assert.Equal(_integerStorageType, ReadScalar(profile, _definitionTypeSql));
        Assert.Equal(0L, ReadScalar(profile, _memberViewSql));
        AssertIntegrity(profile);
        await using (GameplayCollectionStore reopened = new(profile))
            await reopened.GetMeasurementsAsync();

        Assert.Single(Directory.GetFiles(System.IO.Path.Combine(directory.Path, _migrationBackupName), _migrationBackupPattern));
    }

    /// <summary>
    /// Leaves the old schema and native bytes unchanged if dictionary corruption prevents a safe upgrade.
    /// </summary>
    /// <returns>Completes after validating rollback and the preserved old database.</returns>
    [Fact]
    public async Task CorruptVersionTwoValueRollsBackCompactSchemaAndArchiveChanges()
    {
        using TestDirectory directory = new();
        string profile = CreateVersionTwoStorage(directory.Path, true);
        await using (GameplayCollectionStore store = new(profile))
            await Assert.ThrowsAnyAsync<Exception>(() => store.GetMeasurementsAsync());

        Assert.Equal(2L, ReadScalar(profile, _schemaVersionSql));
        Assert.Equal(1L, ReadScalar(profile, _oldDefinitionSql));
        AssertIntegrity(profile);
    }

    /// <summary>
    /// Creates version-2 facts directly from the historical schema, including an optional checksum failure.
    /// </summary>
    /// <param name="root">The owned test directory.</param>
    /// <param name="corrupt">Whether the old value checksum is deliberately invalid.</param>
    /// <returns>The disposable profile directory.</returns>
    private static string CreateVersionTwoStorage(string root, bool corrupt)
    {
        string profile = System.IO.Path.Combine(root, _migrationProfileName);
        Directory.CreateDirectory(profile);
        using SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = System.IO.Path.Combine(profile, _databaseName), Pooling = false }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        using Stream schema = Assembly.GetExecutingAssembly().GetManifestResourceStream(_versionTwoSchema)!;
        using StreamReader text = new(schema);
        command.CommandText = text.ReadToEnd();
        command.ExecuteNonQuery();
        command.CommandText = _versionTwoMeta;
        command.Parameters.AddWithValue(_legacyProfileParameter, CollectionEncoding.Identifier(_profile));
        command.Parameters.AddWithValue(_legacyEpochParameter, CollectionEncoding.Identifier(_epoch));
        command.Parameters.AddWithValue(_legacyStreamParameter, CollectionEncoding.Identifier(_stream));
        command.Parameters.AddWithValue(_legacyRevisionParameter, CollectionEncoding.Revision(42));
        command.ExecuteNonQuery();
        command.Parameters.Clear();
        command.CommandText = _versionTwoInsert;
        byte[] value = Encoding.UTF8.GetBytes(_versionTwoValue);
        command.Parameters.AddWithValue(_legacyValueParameter, value);
        command.Parameters.AddWithValue(_versionTwoDigest, corrupt ? new byte[8] : SHA256.HashData(value)[..8]);
        command.ExecuteNonQuery();
        return profile;
    }
}
