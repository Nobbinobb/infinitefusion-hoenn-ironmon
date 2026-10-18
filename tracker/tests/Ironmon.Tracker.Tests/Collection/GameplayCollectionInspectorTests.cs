using Ironmon.Tracker.Collection;
using System.Text;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Verifies physical storage inspection includes disconnected producer input without opening raw payloads.
/// </summary>
public sealed class GameplayCollectionInspectorTests
{
    private const string _producerDirectory = "producer";
    private const string _streamsDirectory = "streams";
    private const string _streamOne = "11111111111111111111111111111111";
    private const string _streamTwo = "22222222222222222222222222222222";
    private const string _firstSegment = "segment-00000001.journal";
    private const string _secondSegment = "segment-00000002.journal";
    private const string _bindingA = "binding-a.json";
    private const string _bindingB = "binding-b.json";
    private const string _temporaryBinding = "binding-writing.tmp";
    private const string _unrelatedFile = "unrelated.txt";
    private const string _privatePayload = "private-gameplay-payload";
    private const string _existsProperty = "exists";
    private const string _databaseBytesProperty = "database_bytes";
    private const string _walBytesProperty = "wal_bytes";
    private const string _shmBytesProperty = "shm_bytes";
    private const string _journalBytesProperty = "producer_journal_bytes";
    private const string _journalSegmentsProperty = "producer_journal_segments";
    private const string _bindingBytesProperty = "producer_binding_bytes";
    private const string _completeProperty = "producer_measurement_complete";
    private const string _totalProperty = "total_measured_bytes";
    private const string _approximateProperty = "storage_snapshot_approximate";
    private const string _databaseName = "collection.sqlite";
    private const string _walSuffix = "-wal";
    private const string _shmSuffix = "-shm";
    private const string _temporaryPrefix = "Ironmon-Collection-inspector-tests-";
    private const string _guidFormat = "N";

    /// <summary>
    /// Measures offline input even before the first tracker database exists, while excluding unrelated and temporary files.
    /// </summary>
    [Fact]
    public void MeasuresOnlyOwnedJournalAndBindingFilesWithoutOpeningPayloads()
    {
        using TestDirectory directory = new();
        string producer = Path.Combine(directory.Path, _producerDirectory);
        string streamOne = Path.Combine(producer, _streamsDirectory, _streamOne);
        string streamTwo = Path.Combine(producer, _streamsDirectory, _streamTwo);
        Directory.CreateDirectory(streamOne);
        Directory.CreateDirectory(streamTwo);
        byte[] privateBytes = Encoding.UTF8.GetBytes(_privatePayload);
        string lockedJournal = Path.Combine(streamOne, _firstSegment);
        File.WriteAllBytes(lockedJournal, privateBytes);
        File.WriteAllBytes(Path.Combine(streamOne, _secondSegment), new byte[13]);
        File.WriteAllBytes(Path.Combine(streamTwo, _firstSegment), new byte[21]);
        File.WriteAllBytes(Path.Combine(producer, _bindingA), new byte[5]);
        File.WriteAllBytes(Path.Combine(producer, _bindingB), new byte[8]);
        File.WriteAllBytes(Path.Combine(producer, _temporaryBinding), new byte[34]);
        File.WriteAllBytes(Path.Combine(producer, _firstSegment), new byte[55]);
        File.WriteAllBytes(Path.Combine(streamTwo, _unrelatedFile), new byte[89]);
        using FileStream exclusive = new(lockedJournal, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        JsonElement snapshot = GameplayCollectionInspector.Read(directory.Path);

        Assert.False(snapshot.GetProperty(_existsProperty).GetBoolean());
        Assert.Equal(privateBytes.Length + 34L, snapshot.GetProperty(_journalBytesProperty).GetInt64());
        Assert.Equal(3L, snapshot.GetProperty(_journalSegmentsProperty).GetInt64());
        Assert.Equal(13L, snapshot.GetProperty(_bindingBytesProperty).GetInt64());
        Assert.Equal(privateBytes.Length + 47L, snapshot.GetProperty(_totalProperty).GetInt64());
        Assert.Equal(0L, snapshot.GetProperty(_databaseBytesProperty).GetInt64());
        Assert.True(snapshot.GetProperty(_completeProperty).GetBoolean());
        Assert.True(snapshot.GetProperty(_approximateProperty).GetBoolean());
        Assert.DoesNotContain(_privatePayload, snapshot.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(privateBytes.Length, exclusive.Length);
        Assert.False(File.Exists(Path.Combine(directory.Path, _databaseName)));
    }

    /// <summary>
    /// Includes live SQLite/WAL/SHM and producer bytes in the same reported physical total without changing the outbox.
    /// </summary>
    /// <returns>A task that completes after the asynchronous fixture operations and all described assertions finish.</returns>
    [Fact]
    public async Task AddsProducerStorageToLiveDatabaseMeasurements()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        await store.GetMeasurementsAsync();
        string producer = Path.Combine(directory.Path, _producerDirectory);
        string stream = Path.Combine(producer, _streamsDirectory, _streamOne);
        Directory.CreateDirectory(stream);
        string journal = Path.Combine(stream, _firstSegment);
        File.WriteAllBytes(journal, new byte[17]);
        File.WriteAllBytes(Path.Combine(producer, _bindingA), new byte[11]);

        JsonElement snapshot = GameplayCollectionInspector.Read(directory.Path);

        string database = Path.Combine(directory.Path, _databaseName);
        long databaseBytes = new FileInfo(database).Length;
        long walBytes = new FileInfo(database + _walSuffix).Length;
        long shmBytes = new FileInfo(database + _shmSuffix).Length;
        Assert.True(snapshot.GetProperty(_existsProperty).GetBoolean());
        Assert.Equal(databaseBytes, snapshot.GetProperty(_databaseBytesProperty).GetInt64());
        Assert.Equal(walBytes, snapshot.GetProperty(_walBytesProperty).GetInt64());
        Assert.Equal(shmBytes, snapshot.GetProperty(_shmBytesProperty).GetInt64());
        Assert.Equal(17L, snapshot.GetProperty(_journalBytesProperty).GetInt64());
        Assert.Equal(1L, snapshot.GetProperty(_journalSegmentsProperty).GetInt64());
        Assert.Equal(11L, snapshot.GetProperty(_bindingBytesProperty).GetInt64());
        Assert.Equal(databaseBytes + walBytes + shmBytes + 28L, snapshot.GetProperty(_totalProperty).GetInt64());
        Assert.Equal(17, File.ReadAllBytes(journal).Length);
        Assert.True(snapshot.GetProperty(_completeProperty).GetBoolean());
    }

    /// <summary>
    /// Owns a unique disposable fixture directory.
    /// </summary>
    private sealed class TestDirectory : IDisposable
    {
        /// <summary>
        /// Creates a new directory exclusively for this test.
        /// </summary>
        public TestDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), _temporaryPrefix + Guid.NewGuid().ToString(_guidFormat));
            Directory.CreateDirectory(Path);
        }

        /// <summary>
        /// Gets the exact test-owned directory.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Deletes only the verified test-owned directory.
        /// </summary>
        public void Dispose()
        {
            string root = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
            string owned = System.IO.Path.GetFullPath(Path);
            if (owned.StartsWith(root, StringComparison.OrdinalIgnoreCase) && System.IO.Path.GetFileName(owned).StartsWith(_temporaryPrefix, StringComparison.Ordinal))
                Directory.Delete(owned, true);
        }
    }
}
