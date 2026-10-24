using Ironmon.Tracker.Collection;
using Microsoft.Data.Sqlite;
using System.Reflection;
using Ironmon.Tracker.Collection.Storage.Encoding;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Tests migration against the immutable historical schema rather than deriving old tables from the new writer.
/// </summary>
public sealed partial class CollectionStoreTests
{
    private const string _legacySchemaResource = "Ironmon.Tracker.Tests.Collection.Fixtures.StorageV1.sql";
    private const string _migrationProfileName = "profile";
    private const string _legacyMetaInsert = "INSERT INTO collection_meta VALUES(1,$profile,$epoch,$stream,1,1234,$revision,1,'fresh_at_release')";
    private const string _legacyIdentityInsert = "INSERT INTO identity_namespaces VALUES(1,'1','move'); INSERT INTO identity_values VALUES(1,1,$value,NULL); INSERT INTO identity_labels VALUES(1,'en','Tackle',NULL)";
    private const string _legacyAtomInsert = "INSERT INTO direct_dimension_atoms VALUES(1,'integer','known',0,NULL,NULL,NULL,NULL,$value)";
    private const string _legacySetInsert = "INSERT INTO direct_dimension_atoms VALUES(2,'set','known',NULL,NULL,NULL,NULL,NULL,$value); INSERT INTO direct_atom_members VALUES(2,'set','known',$member)";
    private const string _legacyValueParameter = "$value";
    private const string _legacyMemberParameter = "$member";
    private const string _legacyProfileParameter = "$profile";
    private const string _legacyEpochParameter = "$epoch";
    private const string _legacyStreamParameter = "$stream";
    private const string _legacyRevisionParameter = "$revision";
    private const string _legacyKnownZero = "{\"coverage\":\"known\",\"kind\":\"integer\",\"value\":0}";
    private const string _legacyKnownSet = "{\"coverage\":\"known\",\"kind\":\"set\",\"value\":[\"225441434B4C4522\"]}";
    private const string _legacyTackleJson = "\"TACKLE\"";
    private const string _malformedLegacyJson = "{";
    private const string _nativeValueSql = "SELECT native_value_id FROM identity_values WHERE identity_id=1";
    private const string _atomValueSql = "SELECT value_id FROM direct_dimension_atoms WHERE atom_id=1";
    private const string _atomMemberValueSql = "SELECT member_value_id FROM direct_atom_members WHERE atom_id=2";
    private const string _schemaVersionSql = "SELECT schema_version FROM collection_meta";
    private const string _legacyKeyCountSql = "SELECT count(*) FROM pragma_table_info('identity_values') WHERE name='native_key'";
    private const string _labelsTableCountSql = "SELECT count(*) FROM sqlite_schema WHERE type='table' AND name='identity_labels'";
    private const string _storedRevisionSql = "SELECT collection_revision FROM collection_meta";
    private const string _migrationBackupName = "collection-backups";
    private const string _migrationBackupPattern = "*.sqlite";

    /// <summary>
    /// Preserves exact old keys, typed zero, set membership and revision while retaining an independent original backup.
    /// </summary>
    /// <returns>Completes after migration, close and idempotent reopen checks.</returns>
    [Fact]
    public async Task StorageMigrationPreservesLegacyValuesAndMakesAConsistentBackup()
    {
        using TestDirectory directory = new();
        string profile = CreateLegacyStorage(directory.Path, _legacyTackleJson);
        await using (GameplayCollectionStore store = new(profile))
            await store.GetMeasurementsAsync();

        Assert.Equal(3L, ReadScalar(profile, _schemaVersionSql));
        Assert.Equal(_tackle, ReadStoredValue(profile, Assert.IsType<long>(ReadScalar(profile, _nativeValueSql))).GetString());
        Assert.Equal(0, ReadStoredValue(profile, Assert.IsType<long>(ReadScalar(profile, _atomValueSql))).GetProperty(_valueProperty).GetInt32());
        Assert.Equal(_tackle, ReadStoredValue(profile, Assert.IsType<long>(ReadScalar(profile, _atomMemberValueSql))).GetString());
        Assert.Equal(CollectionEncoding.Revision(42), Assert.IsType<byte[]>(ReadScalar(profile, _storedRevisionSql)));
        Assert.Equal(0L, ReadScalar(profile, _labelsTableCountSql));
        string[] backups = Directory.GetFiles(System.IO.Path.Combine(directory.Path, _migrationBackupName), _migrationBackupPattern);
        Assert.Single(backups);
        AssertIntegrity(profile);
        await using (GameplayCollectionStore reopened = new(profile))
            await reopened.GetMeasurementsAsync();

        Assert.Single(Directory.GetFiles(System.IO.Path.Combine(directory.Path, _migrationBackupName), _migrationBackupPattern));
    }

    /// <summary>
    /// Leaves the complete old schema and data usable when an invalid legacy value prevents conversion.
    /// </summary>
    /// <returns>Completes after the failed migration and independent rollback checks.</returns>
    [Fact]
    public async Task FailedStorageMigrationRollsBackEveryRepresentationChange()
    {
        using TestDirectory directory = new();
        string profile = CreateLegacyStorage(directory.Path, _malformedLegacyJson);
        await using (GameplayCollectionStore store = new(profile))
            await Assert.ThrowsAnyAsync<Exception>(() => store.GetMeasurementsAsync());

        Assert.Equal(1L, ReadScalar(profile, _schemaVersionSql));
        Assert.Equal(1L, ReadScalar(profile, _legacyKeyCountSql));
        Assert.Equal(1L, ReadScalar(profile, _labelsTableCountSql));
        AssertIntegrity(profile);
    }

    /// <summary>
    /// Creates a small independent legacy database using the actual version-1 table definitions.
    /// </summary>
    /// <param name="root">The owned test directory containing both profile and migration backups.</param>
    /// <param name="nativeJson">The exact old native key, optionally malformed to exercise rollback.</param>
    /// <returns>The disposable legacy profile directory.</returns>
    private static string CreateLegacyStorage(string root, string nativeJson)
    {
        string profile = System.IO.Path.Combine(root, _migrationProfileName);
        Directory.CreateDirectory(profile);
        using SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = System.IO.Path.Combine(profile, _databaseName), Pooling = false }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        using Stream schema = Assembly.GetExecutingAssembly().GetManifestResourceStream(_legacySchemaResource)!;
        using StreamReader text = new(schema);
        command.CommandText = text.ReadToEnd();
        command.ExecuteNonQuery();
        command.CommandText = _legacyMetaInsert;
        command.Parameters.AddWithValue(_legacyProfileParameter, CollectionEncoding.Identifier(_profile));
        command.Parameters.AddWithValue(_legacyEpochParameter, CollectionEncoding.Identifier(_epoch));
        command.Parameters.AddWithValue(_legacyStreamParameter, CollectionEncoding.Identifier(_stream));
        command.Parameters.AddWithValue(_legacyRevisionParameter, CollectionEncoding.Revision(42));
        command.ExecuteNonQuery();
        command.Parameters.Clear();
        command.CommandText = _legacyIdentityInsert;
        command.Parameters.AddWithValue(_legacyValueParameter, System.Text.Encoding.UTF8.GetBytes(nativeJson));
        command.ExecuteNonQuery();
        command.Parameters.Clear();
        command.CommandText = _legacyAtomInsert;
        command.Parameters.AddWithValue(_legacyValueParameter, System.Text.Encoding.UTF8.GetBytes(_legacyKnownZero));
        command.ExecuteNonQuery();
        command.Parameters.Clear();
        command.CommandText = _legacySetInsert;
        command.Parameters.AddWithValue(_legacyValueParameter, System.Text.Encoding.UTF8.GetBytes(_legacyKnownSet));
        command.Parameters.AddWithValue(_legacyMemberParameter, System.Text.Encoding.UTF8.GetBytes(_legacyTackleJson));
        command.ExecuteNonQuery();
        return profile;
    }
}
