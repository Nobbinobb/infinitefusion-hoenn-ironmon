using Ironmon.Tracker.Collection.Reduction;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Implements the serialized relational collection store and its atomic receipt boundary.
/// </summary>
internal sealed partial class CollectionDatabase : IDisposable
{
    private const string _databaseName = "collection.sqlite";
    private const string _lockName = "collection.lock";
    private const string _schemaResource = "Ironmon.Tracker.Collection.Schema.sql";
    private const string _walMode = "wal";
    private const string _schemaVersion = "schema_version";
    private const string _profileProperty = "profile_id";
    private const string _epochProperty = "epoch_id";
    private const string _streamProperty = "stream_id";
    private const string _eventsProperty = "events";
    private const string _sequenceProperty = "sequence";
    private const string _typeProperty = "type";
    private const string _attemptProperty = "attempt_id";
    private const string _payloadProperty = "payload";
    private const string _originChannel = "origin_channel";
    private const string _contextProperty = "context";
    private const string _ordinaryGameplay = "ordinary_gameplay";
    private const string _gameplay = "gameplay";
    private const string _streamClosed = "collection.stream_closed";
    private const string _effectClosed = "collection.effect_group_closed";
    private const string _effectOpened = "collection.effect_group_opened";
    private const string _contextCheckpoint = "collection.context_checkpoint";
    private const string _attemptContextInitialized = "collection.attempt_context_initialized";
    private const string _attemptCompleted = "attempt.completed";
    private const int _maximumBatchBytes = 16 * 1024 * 1024;
    private static readonly Version _minimumSqliteVersion = new(3, 51, 3);
    private readonly SqliteConnection _db;
    private readonly FileStream _ownership;
    private readonly string _databasePath;
    private readonly CollectionCatalogue _catalogue;
    private readonly CollectionEventReducer _reducer;
    private readonly Dictionary<string, CollectionFamilyDefinition> _families;
    private readonly HashSet<string> _eventTypes;
    private readonly string _sqliteVersion;
    private readonly CollectionValueStore _values;
    private SqliteTransaction? _transaction;
    private byte[]? _profile;
    private byte[]? _epoch;
    private byte[]? _incarnation;
    private ulong _revision;
    private long _lifetimeScope;
    private long _acceptedEvents;
    private long _acceptedBatches;
    private long _duplicateEvents;
    private long _receivedBytes;
    private double _lastApplyMilliseconds;
    private double _totalApplyMilliseconds;
    private double _maximumApplyMilliseconds;
    private JsonElement _currentBatch;
    private Guid? _verifiedOpenStream;

    /// <summary>
    /// Opens one externally locked, version-checked SQLite database.
    /// </summary>
    /// <param name="profileDirectory">The collection profile directory whose database and exclusive ownership lock are opened.</param>
    public CollectionDatabase(string profileDirectory)
    {
        Directory.CreateDirectory(profileDirectory);
        _databasePath = Path.Combine(profileDirectory, _databaseName);
        _ownership = new FileStream(Path.Combine(profileDirectory, _lockName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        SqliteConnectionStringBuilder connectionString = new() { DataSource = _databasePath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false };
        _db = new SqliteConnection(connectionString.ToString());
        try
        {
            _db.Open();
            CollectionSqliteMaintenance.SetCompactPageSize(_db, _transaction);
            _sqliteVersion = CollectionSqliteMaintenance.SqliteVersion(_db, _transaction) ?? throw new InvalidDataException("SQLite did not report a runtime version.");
            if (!Version.TryParse(_sqliteVersion, out Version? runtimeVersion) || runtimeVersion < _minimumSqliteVersion)
                throw new NotSupportedException("Gameplay collection requires SQLite 3.51.3 or newer.");

            CollectionSqliteMaintenance.ConfigureDurability(_db, _transaction);
            if (!string.Equals(CollectionSqliteMaintenance.ReadJournalMode(_db, _transaction), _walMode, StringComparison.OrdinalIgnoreCase) || CollectionSqliteMaintenance.ReadSynchronous(_db, _transaction) != 2 || CollectionSqliteMaintenance.ReadForeignKeys(_db, _transaction) != 1)
                throw new InvalidOperationException("SQLite collection durability settings were not applied.");

            if (CollectionQueries.HasCollectionSchema(_db, _transaction) == 0)
            {
                using SqliteTransaction schemaTransaction = _db.BeginTransaction();
                _transaction = schemaTransaction;
                CollectionSqliteMaintenance.CreateCoreSchema(_db, _transaction);
                CollectionSqliteMaintenance.CreateSupplementalSchema(_db, _transaction);
                schemaTransaction.Commit();
                _transaction = null;
            }
            else
            {
                CollectionSqliteMaintenance.CreateSupplementalSchema(_db, _transaction);
            }

            CollectionSqliteMaintenance.CreateResourceSchema(_db, _transaction);
            _values = new CollectionValueStore(_db, () => _transaction, false);
            UpgradeStorage();

            _catalogue = CollectionCatalogue.LoadDefault();
            _families = _catalogue.Families.ToDictionary(family => family.Key, StringComparer.Ordinal);
            _eventTypes = _catalogue.Events.Select(definition => RequiredString(definition, _keyProperty)).ToHashSet(StringComparer.Ordinal);
            _reducer = new CollectionEventReducer(_catalogue);
            InitializeCatalogue();
            CollectionMetadataRecord? meta = CollectionQueries.ReadCollectionMetadata(_db, _transaction);
            if (meta is not null)
            {
                _profile = meta.Profile;
                _epoch = meta.Epoch;
                _incarnation = meta.Incarnation;
                _revision = CollectionEncoding.ReadRevision(meta.Revision);
            }

            if (_profile is not null)
            {
                _lifetimeScope = Scope(_scopeLifetime, _epoch!, null);
                RestoreReducer();
            }
        }
        catch
        {
            _db.Dispose();
            _ownership.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Applies one contiguous source range atomically before building an acknowledgement.
    /// </summary>
    /// <param name="batch">The immutable, contiguous collection batch whose new occurrences are applied atomically.</param>
    /// <returns>The acknowledgement binding and contiguous prefix after the transaction commits successfully.</returns>
    public JsonElement Accept(JsonElement batch)
    {
        int receivedBytes = ValidateBatch(batch);
        _currentBatch = batch;
        byte[] profile = CollectionEncoding.Identifier(RequiredString(batch, _profileProperty));
        byte[] epoch = CollectionEncoding.Identifier(RequiredString(batch, _epochProperty));
        byte[] stream = CollectionEncoding.Identifier(RequiredString(batch, _streamProperty));
        byte[] stateBefore = CollectionEncoding.Canonical(_reducer.ExportState());
        ulong revisionBefore = _revision;
        bool initializedBefore = _profile is not null;
        _thawedPartitions.Clear();
        _replacedColdBlocks.Clear();
        _checkedHotPartitions.Clear();
        _verifiedOpenStream = null;
        _achievementChangedGroups.Clear();
        Stopwatch elapsed = Stopwatch.StartNew();
        long accepted = 0;
        long duplicates = 0;
        using SqliteTransaction transaction = _db.BeginTransaction();
        _transaction = transaction;
        try
        {
            Bind(profile, epoch);
            RegisterStream(batch, stream);
            ulong prefix = Receipt(stream);
            ulong previous = 0;
            bool first = true;
            HashSet<byte[]> closedAttempts = new(ByteArrayComparer.Instance);
            foreach (JsonElement occurrence in batch.GetProperty(_eventsProperty).EnumerateArray())
            {
                ulong sequence = CollectionEncoding.Sequence(occurrence.GetProperty(_sequenceProperty));
                if ((!first && sequence != checked(previous + 1)) || sequence == 0)
                    throw new InvalidDataException("Collection batch sequences are not contiguous.");

                first = false;
                previous = sequence;
                if (sequence <= prefix)
                {
                    duplicates++;
                    continue;
                }

                if (sequence != checked(prefix + 1))
                    throw new InvalidDataException("Collection stream has a missing required sequence; retry its predecessor range first.");

                EnsureStreamOpen(stream);
                _revision = checked(_revision + 1);
                ApplyOccurrence(occurrence, stream, sequence, closedAttempts);
                prefix = sequence;
                accepted++;
            }

            SaveReducer(stream, prefix);
            foreach (byte[] attempt in closedAttempts)
                CompactAttempt(attempt);

            RecloseContinuations();
            EvaluateAchievements(_achievementEvaluationRevision.HasValue);
            SaveReceipt(stream, prefix);
            CollectionQueries.SaveCollectionRevision(_db, _transaction, CollectionEncoding.Revision(_revision));
            CollectionQueries.RecordCollectionOperation(_db, _transaction, accepted, duplicates, receivedBytes, _lastApplyMilliseconds, Now());
            transaction.Commit();
            _transaction = null;
            _acceptedBatches++;
            _acceptedEvents += accepted;
            _duplicateEvents += duplicates;
            _receivedBytes += receivedBytes;
            _lastApplyMilliseconds = elapsed.Elapsed.TotalMilliseconds;
            _totalApplyMilliseconds += _lastApplyMilliseconds;
            _maximumApplyMilliseconds = Math.Max(_maximumApplyMilliseconds, _lastApplyMilliseconds);
            return Acknowledgement(stream, prefix);
        }
        catch
        {
            transaction.Rollback();
            _achievementPack = null;
            _achievementEvaluationRevision = null;
            _transaction = null;
            using JsonDocument priorState = JsonDocument.Parse(stateBefore);
            _reducer.ImportState(priorState.RootElement);
            _revision = revisionBefore;
            ClearCaches();
            if (!initializedBefore)
            {
                _profile = null;
                _epoch = null;
                _incarnation = null;
                _lifetimeScope = 0;
            }

            throw;
        }
    }

    /// <summary>
    /// Applies one source event without using a saved snapshot as earned history.
    /// </summary>
    /// <param name="occurrence">The authoritative event envelope, including its attempt, battle and delivery context.</param>
    /// <param name="stream">The producer stream UUID in its 16-byte database representation.</param>
    /// <param name="sequence">The contiguous source sequence being committed with this evidence.</param>
    /// <param name="closedAttempts">Collects attempts whose partitions must be compacted before the transaction commits.</param>
    private void ApplyOccurrence(JsonElement occurrence, byte[] stream, ulong sequence, HashSet<byte[]> closedAttempts)
    {
        string type = RequiredString(occurrence, _typeProperty);
        if (!_eventTypes.Contains(type) && type is not (_streamClosed or _effectClosed or _effectOpened or _contextCheckpoint or _attemptContextInitialized))
            throw new InvalidDataException("Unregistered collection event semantics cannot be acknowledged.");

        byte[]? attempt = OptionalIdentifier(occurrence, _attemptProperty);
        if (attempt is not null)
            EnsureAttempt(attempt, occurrence);

        if (type == _attemptContextInitialized)
            return;

        if (type == _streamClosed)
        {
            CloseStream(stream, sequence);
            return;
        }

        if (!Eligible(occurrence))
            return;

        if (attempt is not null)
            RestoreContinuedAttempt(attempt);

        ReconcileCurrentInventory(occurrence, stream, sequence);
        RetainResourceIdentity(occurrence, stream, sequence);
        if (type == _contextCheckpoint)
            RecordRecoveryGap(occurrence);

        CollectionReduction reduction = _reducer.ReduceAll(occurrence);
        foreach (CollectionProjection projection in reduction.Families)
            ApplyProjection(projection, occurrence);

        foreach (CollectionDirectProjection projection in reduction.Direct)
            ApplyDirectProjection(projection, occurrence);

        if (type == _ownedConfiguration)
            ApplyOwnedConfiguration(occurrence, stream, sequence);

        if (type == _attemptCompleted && attempt is not null)
        {
            ArchiveCompletedAttempt(attempt, stream, sequence);
            CloseAttempt(attempt, occurrence);
            closedAttempts.Add(attempt);
        }
    }

    /// <summary>
    /// Applies the fresh-release eligibility boundary before any sample or counter.
    /// </summary>
    /// <param name="occurrence">The authoritative event envelope, including its attempt, battle and delivery context.</param>
    /// <returns>True for ordinary gameplay input, including events without an explicit origin channel; otherwise false.</returns>
    private static bool Eligible(JsonElement occurrence)
    {
        if (!occurrence.TryGetProperty(_contextProperty, out JsonElement context) || !context.TryGetProperty(_originChannel, out JsonElement origin))
            return true;

        string? channel = origin.GetString();
        return channel is _ordinaryGameplay or _gameplay;
    }

    /// <summary>
    /// Validates bounded schema and identities before queuing durable work.
    /// </summary>
    /// <param name="batch">The incoming collection JSON object whose version, event array and encoded size must be supported.</param>
    /// <returns>The exact encoded UTF-8 batch length in bytes, reused for operational accounting.</returns>
    private static int ValidateBatch(JsonElement batch)
    {
        if (batch.ValueKind != JsonValueKind.Object || batch.GetProperty(_schemaVersion).GetInt32() != 1)
            throw new InvalidDataException("Unsupported gameplay collection schema.");

        int receivedBytes = Encoding.UTF8.GetByteCount(batch.GetRawText());
        if (receivedBytes > _maximumBatchBytes)
            throw new InvalidDataException("Gameplay collection batch exceeds the encoded byte budget.");

        if (batch.GetProperty(_eventsProperty).ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Gameplay collection batch has no event array.");

        return receivedBytes;
    }

    /// <summary>
    /// Binds a fresh database once and rejects cross-profile or cross-epoch mixing.
    /// </summary>
    /// <param name="profile">The source collection profile UUID in its 16-byte representation.</param>
    /// <param name="epoch">The fresh collection epoch UUID in its 16-byte representation.</param>
    private void Bind(byte[] profile, byte[] epoch)
    {
        if (_profile is not null)
        {
            if (!_profile.AsSpan().SequenceEqual(profile) || !_epoch!.AsSpan().SequenceEqual(epoch))
                throw new InvalidDataException("Collection profile or epoch does not match this database; pending input remains isolated.");

            return;
        }

        _profile = profile;
        _epoch = epoch;
        _incarnation = Guid.NewGuid().ToByteArray();
        CollectionQueries.InsertCollectionMetadata(_db, _transaction, profile, epoch, _incarnation, Now(), CollectionEncoding.Revision(0));
        _lifetimeScope = Scope(_scopeLifetime, epoch, null);
    }

    /// <summary>
    /// Builds the post-commit immutable acknowledgement.
    /// </summary>
    /// <param name="stream">The producer stream UUID in its 16-byte database representation.</param>
    /// <param name="prefix">The durable contiguous source prefix to acknowledge.</param>
    /// <returns>The immutable store/profile/epoch/stream binding and its committed contiguous prefix.</returns>
    private JsonElement Acknowledgement(byte[] stream, ulong prefix)
        => JsonSerializer.SerializeToElement(new { schema_version = 1, profile_id = CollectionEncoding.Identifier(_profile!), epoch_id = CollectionEncoding.Identifier(_epoch!), store_incarnation_id = CollectionEncoding.Identifier(_incarnation!), stream_id = CollectionEncoding.Identifier(stream), durable_prefix = CollectionEncoding.Decimal(prefix) });

    /// <summary>
    /// Returns one required string property.
    /// </summary>
    /// <param name="parent">The object containing the required string property.</param>
    /// <param name="key">The exact name of the required property.</param>
    /// <returns>The required string value; missing or null properties are rejected.</returns>
    private static string RequiredString(JsonElement parent, string key)
        => parent.GetProperty(key).GetString() ?? throw new InvalidDataException("A required collection string was null.");

    /// <summary>
    /// Reads an optional UUID binding.
    /// </summary>
    /// <param name="parent">The source object containing an optional UUID property.</param>
    /// <param name="key">The exact name of the optional UUID property.</param>
    /// <returns>The parsed 16-byte UUID, or null when the optional property is absent or JSON null.</returns>
    private static byte[]? OptionalIdentifier(JsonElement parent, string key)
        => parent.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String ? CollectionEncoding.Identifier(value.GetString()!) : null;

    /// <summary>
    /// Returns a diagnostic UTC clock unrelated to gameplay ordering or duration.
    /// </summary>
    /// <returns>The current UTC Unix time in milliseconds, used only for operational diagnostics.</returns>
    private static long Now()
        => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// Flushes WAL state and releases exact profile ownership.
    /// </summary>
    public void Dispose()
    {
        try
        {
            CollectionSqliteMaintenance.CheckpointTruncate(_db, _transaction);
        }
        finally
        {
            _db.Dispose();
            _ownership.Dispose();
        }
    }

    /// <summary>
    /// Compares exact byte identifiers without changing source identity semantics.
    /// </summary>
    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        /// <summary>
        /// Gets the reusable comparer.
        /// </summary>
        public static ByteArrayComparer Instance { get; } = new();

        /// <summary>
        /// Compares exact bytes.
        /// </summary>
        /// <param name="left">The first byte sequence, or null.</param>
        /// <param name="right">The second byte sequence, or null.</param>
        /// <returns>True when both references are null or their byte contents are equal; otherwise false.</returns>
        public bool Equals(byte[]? left, byte[]? right)
            => ReferenceEquals(left, right) || left is not null && right is not null && left.AsSpan().SequenceEqual(right);

        /// <summary>
        /// Hashes exact bytes for one bounded in-memory set.
        /// </summary>
        /// <param name="value">The exact bytes whose content determines the in-memory hash.</param>
        /// <returns>A content-based hash suitable for the bounded in-memory byte-array set.</returns>
        public int GetHashCode(byte[] value)
        {
            HashCode hash = new();
            hash.AddBytes(value);
            return hash.ToHashCode();
        }
    }
}
