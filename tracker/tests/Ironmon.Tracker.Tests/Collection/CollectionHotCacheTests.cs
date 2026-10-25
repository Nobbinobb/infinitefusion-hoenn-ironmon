using Ironmon.Tracker.Collection;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Checks lookup caches against rollback, retired stream fences and completed-attempt continuations.
/// </summary>
public sealed partial class CollectionStoreTests
{
    private const string _hotAlternateAttempt = "99999999999999999999999999999999";
    private const string _hotAttemptProperty = "attempt_id";
    private const string _hotResourcePrefix = "Ironmon.Tracker.Tests.Collection.Fixtures.HotCaches.";
    private const string _hotAttemptDamage = "AttemptDamage.sql";
    private const string _hotAttemptPeak = "AttemptPeak.sql";
    private const string _hotSeparateTotals = "7,11";
    private const string _hotOperationsProperty = "operations";
    private const string _hotReceivedBytesProperty = "received_bytes";
    private const string _hotWorkerProperty = "worker";
    private const string _hotContextEmpty = "\"context\":{}";
    private const string _hotUnicodeContext = "\"context\":{\"label\":\"雪の道 ä\"}";
    private const string _hotUnicode = "雪の道 ä";
    private const string _hotCompletedAttemptTable = "completed_attempt_reducer_states";

    /// <summary>
    /// Rejects rolled-back cached identities even when SQLite immediately reuses their original numeric IDs.
    /// </summary>
    /// <returns>Completes after independent attempts retain their own exact totals.</returns>
    [Fact]
    public async Task LookupCachesDoNotReuseRolledBackScopeOrContextIdentities()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, Damage(999), 1), Occurrence(_damageEvent, Damage(1), 3)])));
        await store.AcceptBatchAsync(Batch(_stream, [HotOccurrence(_damageEvent, Damage(7), 1, _hotAlternateAttempt)]));
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, Damage(11), 2)]));
        Assert.Equal(18L, ReadScalar(directory.Path, _damageScalarSql));
        Assert.Equal(_hotSeparateTotals, ReadScalar(directory.Path, HotControl(_hotAttemptDamage)));
        Assert.Equal(2L, GameplayCollectionInspector.Read(directory.Path).GetProperty(_tablesProperty).GetProperty(_attemptsTable).GetInt64());
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Preserves retirement fences while accepting acknowledged duplicates and rolling back a close followed by a new tail.
    /// </summary>
    /// <returns>Completes after no rejected post-retirement damage reaches the database.</returns>
    [Fact]
    public async Task CachedOpenStreamRejectsNewTailButAcceptsRetiredDuplicateRetries()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        JsonElement[] closed = [Occurrence(_damageEvent, Damage(40), 1), Occurrence(_closedEvent, JsonSerializer.Deserialize<JsonElement>(_emptyPayload), 2)];
        await Assert.ThrowsAsync<InvalidDataException>(() => store.AcceptBatchAsync(Batch(_stream, [.. closed, Occurrence(_damageEvent, Damage(999), 3)])));
        await store.AcceptBatchAsync(Batch(_stream, closed));
        JsonElement duplicate = await store.AcceptBatchAsync(Batch(_stream, closed));
        Assert.Equal(_secondSequence, duplicate.GetProperty(_prefixProperty).GetString());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, Damage(999), 3)])));
        Assert.Equal(40L, ReadScalar(directory.Path, _damageScalarSql));
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Reopens closed scopes after reset and disconnect without treating previous hot-cache misses as permanent evidence.
    /// </summary>
    /// <returns>Completes after both continued and independent attempts preserve sufficient totals.</returns>
    [Fact]
    public async Task HotPartitionMemoDoesNotSuppressReopeningAfterCompletionOrRestart()
    {
        using TestDirectory directory = new();
        await using (GameplayCollectionStore initial = new(directory.Path))
        {
            await initial.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, Damage(7), 1), Occurrence(_attemptEndEvent, JsonSerializer.Deserialize<JsonElement>(_abandonedPayload), 2)]));
            await initial.AcceptBatchAsync(Batch(_stream, [HotOccurrence(_damageEvent, Damage(11), 3, _hotAlternateAttempt), HotOccurrence(_attemptEndEvent, JsonSerializer.Deserialize<JsonElement>(_terminalPayload), 4, _hotAlternateAttempt)]));
            await initial.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, Damage(13), 5), Occurrence(_damageEvent, Damage(17), 6)]));
        }

        await using GameplayCollectionStore reopened = new(directory.Path);
        await reopened.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, Damage(19), 7), Occurrence(_attemptEndEvent, JsonSerializer.Deserialize<JsonElement>(_terminalPayload), 8)]));
        Assert.Equal(67L, ReadScalar(directory.Path, _damageScalarSql));
        Assert.Equal(56L, ReadScalar(directory.Path, HotControl(_hotAttemptPeak)));
        Assert.Equal(2L, GameplayCollectionInspector.Read(directory.Path).GetProperty(_tablesProperty).GetProperty(_hotCompletedAttemptTable).GetInt64());
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Retains exact raw UTF-8 accounting while reusing one validated byte count for every batch outcome.
    /// </summary>
    /// <returns>Completes after worker and persisted byte totals match original non-ASCII input including retries.</returns>
    [Fact]
    public async Task ReusedBatchByteCountPreservesRawUnicodeAndDuplicateAccounting()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        string raw = Batch(_stream, [Occurrence(_damageEvent, Damage(7), 1)]).GetRawText().Replace(_hotContextEmpty, _hotUnicodeContext, StringComparison.Ordinal);
        Assert.Contains(_hotUnicode, raw, StringComparison.Ordinal);
        using JsonDocument input = JsonDocument.Parse(raw);
        await store.AcceptBatchAsync(input.RootElement);
        await store.AcceptBatchAsync(input.RootElement);
        long expected = 2L * Encoding.UTF8.GetByteCount(raw);
        JsonElement measurements = await store.GetMeasurementsAsync();
        Assert.Equal(expected, measurements.GetProperty(_hotWorkerProperty).GetProperty(_hotReceivedBytesProperty).GetInt64());
        Assert.Equal(expected, GameplayCollectionInspector.Read(directory.Path).GetProperty(_hotOperationsProperty).GetProperty(_hotReceivedBytesProperty).GetInt64());
        Assert.Equal(7L, ReadScalar(directory.Path, _damageScalarSql));
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Creates one ordinary occurrence belonging to a different actual attempt.
    /// </summary>
    /// <param name="type">The actual event type.</param>
    /// <param name="payload">The authoritative event payload.</param>
    /// <param name="sequence">The contiguous producer sequence.</param>
    /// <param name="attempt">The distinct authoritative attempt UUID.</param>
    /// <returns>The ordinary event envelope with only its actual attempt identity changed.</returns>
    private static JsonElement HotOccurrence(string type, JsonElement payload, int sequence, string attempt)
    {
        JsonObject occurrence = JsonNode.Parse(Occurrence(type, payload, sequence).GetRawText())!.AsObject();
        occurrence[_hotAttemptProperty] = attempt;
        return JsonSerializer.SerializeToElement(occurrence);
    }

    /// <summary>
    /// Reads independent control queries from embedded test fixtures.
    /// </summary>
    /// <param name="name">The control fixture filename.</param>
    /// <returns>The exact independent SQL control text.</returns>
    private static string HotControl(string name)
    {
        using Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(_hotResourcePrefix + name)!;
        using StreamReader reader = new(source);
        return reader.ReadToEnd();
    }
}
