using Ironmon.Tracker.Collection;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Exercises the actual SQLite store, receipts, exact values and finalized partition recovery.
/// </summary>
public sealed partial class CollectionStoreTests
{
    private const string _profile = "11111111111111111111111111111111";
    private const string _epoch = "22222222222222222222222222222222";
    private const string _stream = "33333333333333333333333333333333";
    private const string _successor = "44444444444444444444444444444444";
    private const string _attempt = "55555555555555555555555555555555";
    private const string _branch = "66666666666666666666666666666666";
    private const string _damageEvent = "hp.loss_applied";
    private const string _ownedEvent = "pokemon.owned_configuration_committed";
    private const string _attemptContextEvent = "collection.attempt_context_initialized";
    private const string _attemptsTable = "attempts";
    private const string _seriesTable = "aggregate_series";
    private const string _captureKindProperty = "capture_kind";
    private const string _configurationChange = "configuration_change";
    private const string _experienceProperty = "experience";
    private const string _trainingProperty = "training_traits";
    private const string _friendshipProperty = "friendship";
    private const string _movesProperty = "moves";
    private const string _moveIdProperty = "move_id";
    private const string _quickAttack = "QUICKATTACK";
    private const string _ownedEntityProperty = "owned_entity_id";
    private const string _runnerOne = "runner-one";
    private const string _runnerTwo = "runner-two";
    private const string _candidateOne = "candidate-one";
    private const string _lineageProperty = "lineage";
    private const string _parentEntitiesProperty = "parent_entity_ids";
    private const string _knownParentEdgesSql = "SELECT COUNT(*) FROM owned_lineage_edges WHERE role='other' AND source_configuration_id IS NOT NULL";
    private const string _contextParentEdgesSql = "SELECT COUNT(*) FROM owned_lineage_edges WHERE role='other' AND source_context_id IS NOT NULL";
    private const string _attemptEndEvent = "attempt.completed";
    private const string _closedEvent = "collection.stream_closed";
    private const string _battle = "battle-one";
    private const string _databaseName = "collection.sqlite";
    private const string _prefixProperty = "durable_prefix";
    private const string _storageProperty = "storage";
    private const string _tablesProperty = "tables";
    private const string _versionProperty = "sqlite_version";
    private const string _pinnedNativeVersion = "3.53.3";
    private const string _firstSequence = "1";
    private const string _secondSequence = "2";
    private const string _familiesTable = "aggregate_families";
    private const string _groupsTable = "direct_groups";
    private const string _ownedTable = "owned_individuals";
    private const string _configurationTable = "configuration_revisions";
    private const string _movesTable = "moveset_slots";
    private const string _partialsTable = "owned_partial_configurations";
    private const string _coldTable = "cold_partitions";
    private const string _gapsTable = "coverage_gaps";
    private const string _gapFamiliesTable = "coverage_gap_families";
    private const string _checkpointEvent = "collection.context_checkpoint";
    private const string _interrupted = "recovered_interrupted";
    private const string _wideDamage = "1267650600228229401496703205376";
    private const string _wideScalarSql = "SELECT v.value_decimal FROM direct_scalar_values v JOIN direct_groups g ON g.group_id=v.group_id WHERE g.projection_key='actual_hp_lost+substitute_hp_damage'";
    private const string _inventorySnapshotEvent = "inventory.snapshot_reconciled";
    private const string _potion = "POTION";
    private const string _antidote = "ANTIDOTE";
    private const string _playerBag = "player_bag";
    private const string _inventorySnapshotKind = "reconciliation";
    private const string _inventoryMembersSql = "SELECT i.native_value_id FROM set_members m JOIN identity_values i ON i.identity_id=m.member_identity_id JOIN aggregate_families f ON f.family_id=m.family_id WHERE f.family_key='inventory_distinct_items_current'";
    private const string _inventoryMembersCountSql = "SELECT COUNT(*) FROM set_members m JOIN aggregate_families f ON f.family_id=m.family_id WHERE f.family_key='inventory_distinct_items_current'";
    private const string _inventorySeriesCountSql = "SELECT COUNT(*) FROM aggregate_series s JOIN aggregate_families f ON f.family_id=s.family_id WHERE f.family_key='inventory_item_quantity_current'";
    private const string _inventoryQuantitySql = "SELECT o.state_blob FROM ordered_state o JOIN aggregate_families f ON f.family_id=o.family_id WHERE f.family_key='inventory_item_quantity_current'";
    private const string _inventoryQuantityKey = "item_quantities.quantity";
    private const string _valueProperty = "value";
    private const string _coverageProperty = "coverage";
    private const string _knownCoverage = "known";
    private const string _damageScalarSql = "SELECT c.value_blob FROM aggregate_cells c JOIN aggregate_series s ON s.series_id=c.series_id JOIN hot_partitions p ON p.partition_id=s.partition_id JOIN scopes x ON x.scope_id=p.scope_id JOIN aggregate_families f ON f.family_id=s.family_id WHERE f.family_key='actual_hp_lost' AND x.scope_kind='profile_lifetime'";
    private const string _foreignSql = "PRAGMA foreign_key_check";
    private const string _integritySql = "PRAGMA integrity_check";
    private const string _integrityOkay = "ok";
    private const string _ivSql = "SELECT v.hp,v.attack,v.defense,v.special_attack,v.special_defense,v.speed FROM configuration_revisions c JOIN stat_vectors v ON v.stat_vector_id=c.raw_iv_id";
    private const string _emptyPayload = "{}";
    private const string _terminalPayload = "{\"result\":\"lost\"}";
    private const string _abandonedPayload = "{\"result\":\"abandoned\"}";
    private const string _resetStatus = "reset";
    private const string _attemptStatusSql = "SELECT status FROM attempts";
    private const string _battleStartedEvent = "battle.started";
    private const string _battleEndedEvent = "battle.ended";
    private const string _trainerKind = "trainer";
    private const string _winOutcome = "win";
    private const string _trainerBattlePrefix = "trainer-battle-";
    private const string _trainerAttemptWinsSql = "SELECT b.payload FROM cold_partitions p JOIN scopes x ON x.scope_id=p.scope_id JOIN aggregate_families f ON f.family_id=p.family_id JOIN block_values b ON b.block_id=p.base_block_id WHERE f.family_key='battle_outcomes' AND x.scope_kind='attempt'";
    private const string _trainerAttemptPeakSql = "SELECT v.value_i64 FROM direct_scalar_values v JOIN direct_groups g ON g.group_id=v.group_id WHERE g.projection_key='scope_statistic_peaks' AND g.variant_key='4cfe8464116a0ffe'";
    private const string _completedAttemptStateCountSql = "SELECT COUNT(*) FROM completed_attempt_reducer_states";
    private const string _activeReducerStateSql = "SELECT payload FROM causal_work WHERE work_kind='generic_reducer'";
    private const string _attemptStateProperty = "attempts";
    private const string _archivedSeriesProperty = "series";
    private const string _archivedCellsProperty = "cells";
    private const string _archiveFamiliesProperty = "families";
    private const string _battleOutcomesFamilySql = "SELECT family_id FROM aggregate_families WHERE family_key='battle_outcomes'";
    private const string _tempPrefix = "Ironmon-Collection-tests-";
    private const string _ownedPayload = """
        {"owned_entity_id":"runner-one","configuration_revision":1,"capture_kind":"acquisition","change_reasons":["starter"],"ownership_role":"runner","team_commit_context":{"outer_transaction_id":"select-one","retained_after_commit":true},"species_identity":{"canonical_species_id":25},"level":5,"experience":125,"generated_base_stats":{"hp":35,"attack":55,"defense":40,"special_attack":50,"special_defense":50,"speed":90},"computed_stats":{"maximum_hp":20,"attack":12,"defense":10,"special_attack":11,"special_defense":11,"speed":15},"training_traits":{"iv":{"hp":1,"attack":2,"defense":3,"special_attack":4,"special_defense":5,"speed":6},"effective_iv":{"hp":1,"attack":2,"defense":3,"special_attack":4,"special_defense":5,"speed":6},"ev":{"hp":0,"attack":0,"defense":0,"special_attack":0,"special_defense":0,"speed":0},"nature_id":"HARDY"},"moves":[{"slot":0,"move_id":"TACKLE","max_pp":35},{"slot":1,"move_id":"GROWL","max_pp":40}],"persistent_ability":{"ability_id":"STATIC"},"held_item":{"item_id":null,"origin_known":false}}
        """;
    private const string _player = "player";
    private const string _opponent = "opponent";
    private const string _directKind = "direct";
    private const string _tackle = "TACKLE";

    /// <summary>
    /// Verifies actual native runtime and the entire generic registry in a fresh database.
    /// </summary>
    /// <returns>A task that completes after the asynchronous fixture operations and all described assertions finish.</returns>
    [Fact]
    public async Task InitializesActualSupportedRuntimeAndAllGenericDefinitions()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        JsonElement ack = await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, Damage(40), 1)]));
        Assert.Equal(_firstSequence, ack.GetProperty(_prefixProperty).GetString());
        JsonElement status = await store.GetMeasurementsAsync();
        JsonElement storage = status.GetProperty(_storageProperty);
        Assert.True(Version.Parse(storage.GetProperty(_versionProperty).GetString()!) >= new Version(3, 51, 3));
        Assert.Equal(_pinnedNativeVersion, storage.GetProperty(_versionProperty).GetString());
        Assert.Equal(258, storage.GetProperty(_tablesProperty).GetProperty(_familiesTable).GetInt64());
        Assert.Equal(84, storage.GetProperty(_tablesProperty).GetProperty(_groupsTable).GetInt64());
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Verifies commit-before-ACK retry idempotence after a real database close and reopen.
    /// </summary>
    /// <returns>A task that completes after the asynchronous fixture operations and all described assertions finish.</returns>
    [Fact]
    public async Task DuplicateBatchAfterReopenDoesNotAddDamageAgain()
    {
        using TestDirectory directory = new();
        JsonElement batch = Batch(_stream, [Occurrence(_damageEvent, Damage(40), 1)]);
        await using (GameplayCollectionStore first = new(directory.Path))
            await first.AcceptBatchAsync(batch);

        await using GameplayCollectionStore reopened = new(directory.Path);
        await reopened.AcceptBatchAsync(batch);
        await reopened.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, Damage(60), 2)]));
        Assert.Equal(100L, ReadScalar(directory.Path, _damageScalarSql));
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Verifies an out-of-order batch rolls back its earlier effects and receipt together.
    /// </summary>
    /// <returns>A task that completes after the asynchronous fixture operations and all described assertions finish.</returns>
    [Fact]
    public async Task MissingSequenceRollsBackTheEntireBatch()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, Damage(40), 1), Occurrence(_damageEvent, Damage(60), 3)])));
        JsonElement ack = await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, Damage(40), 1)]));
        Assert.Equal(_firstSequence, ack.GetProperty(_prefixProperty).GetString());
        Assert.Equal(40L, ReadScalar(directory.Path, _damageScalarSql));
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Verifies real retained IVs, stat vectors and simultaneous moves reach relational records.
    /// </summary>
    /// <returns>A task that completes after the asynchronous fixture operations and all described assertions finish.</returns>
    [Fact]
    public async Task PreservesCompleteOwnedConfigurationsWithoutInventingIndividualStats()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_ownedEvent, JsonSerializer.Deserialize<JsonElement>(_ownedPayload), 1)]));
        JsonElement tables = GameplayCollectionInspector.Read(directory.Path).GetProperty(_tablesProperty);
        Assert.Equal(1, tables.GetProperty(_ownedTable).GetInt64());
        Assert.Equal(1, tables.GetProperty(_configurationTable).GetInt64());
        Assert.Equal(2, tables.GetProperty(_movesTable).GetInt64());
        Assert.Equal(0, tables.GetProperty(_partialsTable).GetInt64());
        using SqliteConnection connection = OpenRead(directory.Path);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = _ivSql;
        using SqliteDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(Enumerable.Range(1, 6).Select(value => (long)value), Enumerable.Range(0, 6).Select(reader.GetInt64));
    }

    /// <summary>
    /// Verifies attaching a save to the fresh collection epoch does not manufacture a run start.
    /// </summary>
    /// <returns>A task that completes after the asynchronous fixture operations and all described assertions finish.</returns>
    [Fact]
    public async Task TechnicalAttemptAnchorHasNoGameplayProgress()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        JsonElement ack = await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_attemptContextEvent, JsonSerializer.Deserialize<JsonElement>(_emptyPayload), 1)]));
        JsonElement tables = GameplayCollectionInspector.Read(directory.Path).GetProperty(_tablesProperty);
        Assert.Equal(_firstSequence, ack.GetProperty(_prefixProperty).GetString());
        Assert.Equal(1, tables.GetProperty(_attemptsTable).GetInt64());
        Assert.Equal(0, tables.GetProperty(_seriesTable).GetInt64());
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Verifies frequent EXP and friendship changes do not create configuration history while learning does.
    /// </summary>
    /// <returns>A task that completes after the asynchronous fixture operations and all described assertions finish.</returns>
    [Fact]
    public async Task ResourceOnlyChangesDoNotCreateOwnedConfigurationRevisions()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_ownedEvent, JsonSerializer.Deserialize<JsonElement>(_ownedPayload), 1)]));
        JsonNode changed = JsonNode.Parse(_ownedPayload)!;
        changed[_captureKindProperty] = _configurationChange;
        changed[_experienceProperty] = 140;
        changed[_trainingProperty]![_friendshipProperty] = 71;
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_ownedEvent, JsonSerializer.SerializeToElement(changed), 2)]));
        Assert.Equal(1, GameplayCollectionInspector.Read(directory.Path).GetProperty(_tablesProperty).GetProperty(_configurationTable).GetInt64());

        changed[_movesProperty]![0]![_moveIdProperty] = _quickAttack;
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_ownedEvent, JsonSerializer.SerializeToElement(changed), 3)]));
        Assert.Equal(2, GameplayCollectionInspector.Read(directory.Path).GetProperty(_tablesProperty).GetProperty(_configurationTable).GetInt64());
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Verifies lineage references known configurations and preserves unowned source identity as context.
    /// </summary>
    /// <returns>A task that completes after the asynchronous fixture operations and all described assertions finish.</returns>
    [Fact]
    public async Task RetainedTransformationKeepsActualParentRelationships()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_ownedEvent, JsonSerializer.Deserialize<JsonElement>(_ownedPayload), 1)]));
        JsonNode transformed = JsonNode.Parse(_ownedPayload)!;
        transformed[_ownedEntityProperty] = _runnerTwo;
        transformed[_lineageProperty] = new JsonObject { [_parentEntitiesProperty] = new JsonArray(_runnerOne, _candidateOne) };
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_ownedEvent, JsonSerializer.SerializeToElement(transformed), 2)]));
        Assert.Equal(1L, ReadScalar(directory.Path, _knownParentEdgesSql));
        Assert.Equal(1L, ReadScalar(directory.Path, _contextParentEdgesSql));
        Assert.Equal(2, GameplayCollectionInspector.Read(directory.Path).GetProperty(_tablesProperty).GetProperty(_ownedTable).GetInt64());
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Verifies closed partitions retain damage and can accept newly replayed gameplay.
    /// </summary>
    /// <returns>A task that completes after the asynchronous fixture operations and all described assertions finish.</returns>
    [Fact]
    public async Task ClosedAttemptCanAccumulateNewPostLoadGameplayWithoutReplacingLifetimeProgress()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, Damage(40), 1), Occurrence(_attemptEndEvent, JsonSerializer.Deserialize<JsonElement>(_terminalPayload), 2)]));
        Assert.True(GameplayCollectionInspector.Read(directory.Path).GetProperty(_tablesProperty).GetProperty(_coldTable).GetInt64() > 0);
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, Damage(60), 3), Occurrence(_attemptEndEvent, JsonSerializer.Deserialize<JsonElement>(_terminalPayload), 4)]));
        Assert.Equal(100L, ReadScalar(directory.Path, _damageScalarSql));
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Checks closed-attempt replay restores sufficient counters across restart without retaining old attempts in active working state.
    /// </summary>
    /// <returns>A task that completes after the asynchronous fixture operations and all described assertions finish.</returns>
    [Fact]
    public async Task ClosedAttemptReplayContinuesItsTrainerWinPeakAcrossStoreRestart()
    {
        using TestDirectory directory = new();
        List<JsonElement> first = TrainerWins(7, 1);
        first.Add(Occurrence(_attemptEndEvent, JsonSerializer.Deserialize<JsonElement>(_terminalPayload), 15));
        await using (GameplayCollectionStore initial = new(directory.Path))
            await initial.AcceptBatchAsync(Batch(_stream, [.. first]));

        Assert.Equal(7L, ReadScalar(directory.Path, _trainerAttemptPeakSql));
        Assert.Equal(1L, ReadScalar(directory.Path, _completedAttemptStateCountSql));
        byte[] checkpoint = Assert.IsType<byte[]>(ReadScalar(directory.Path, _activeReducerStateSql));
        using (MemoryStream input = new(checkpoint, false))
        using (System.IO.Compression.ZLibStream inflater = new(input, System.IO.Compression.CompressionMode.Decompress))
        using (JsonDocument state = JsonDocument.Parse(inflater))
            Assert.Empty(state.RootElement.GetProperty(_attemptStateProperty).EnumerateObject());

        await using GameplayCollectionStore reopened = new(directory.Path);
        List<JsonElement> continued = TrainerWins(5, 16);
        await reopened.AcceptBatchAsync(Batch(_stream, [.. continued]));
        byte[] outcomes = Assert.IsType<byte[]>(ReadScalar(directory.Path, _trainerAttemptWinsSql));
        using (MemoryStream input = new(outcomes, false))
        using (System.IO.Compression.ZLibStream inflater = new(input, System.IO.Compression.CompressionMode.Decompress))
        {
            using MemoryStream bytes = new();
            inflater.CopyTo(bytes);
            JsonElement scope = DecodeStoredValue(directory.Path, bytes.ToArray());
            long family = Assert.IsType<long>(ReadScalar(directory.Path, _battleOutcomesFamilySql));
            JsonElement frame = scope.GetProperty(_archiveFamiliesProperty).EnumerateArray().Single(entry => entry[0].GetInt64() == family)[1];
            Assert.Equal(12L, frame.GetProperty(_archivedSeriesProperty).EnumerateArray().Sum(series => series.GetProperty(_archivedCellsProperty).EnumerateArray().Where(cell => cell[0][1].GetInt64() == 1).Sum(cell => cell[1][1].GetInt64())));
        }
        Assert.Equal(12L, ReadScalar(directory.Path, _trainerAttemptPeakSql));
        await reopened.AcceptBatchAsync(Batch(_stream, [Occurrence(_attemptEndEvent, JsonSerializer.Deserialize<JsonElement>(_terminalPayload), 26)]));
        Assert.Equal(1L, ReadScalar(directory.Path, _completedAttemptStateCountSql));
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Preserves the game's known abandoned outcome as a reset rather than an unknown retirement.
    /// </summary>
    /// <returns>A task that completes after the asynchronous fixture operations and all described assertions finish.</returns>
    [Fact]
    public async Task NativeAbandonedAttemptHasKnownResetStatus()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_attemptEndEvent, JsonSerializer.Deserialize<JsonElement>(_abandonedPayload), 1)]));
        Assert.Equal(_resetStatus, ReadScalar(directory.Path, _attemptStatusSql));
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Verifies successor streams cannot skip a missing predecessor receipt.
    /// </summary>
    /// <returns>A task that completes after the asynchronous fixture operations and all described assertions finish.</returns>
    [Fact]
    public async Task SuccessorWaitsForItsRequiredPredecessorFrontier()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.AcceptBatchAsync(Batch(_successor, [Occurrence(_damageEvent, Damage(5), 1)], _stream, _secondSequence)));
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, Damage(40), 1), Occurrence(_closedEvent, JsonSerializer.Deserialize<JsonElement>(_emptyPayload), 2)]));
        JsonElement ack = await store.AcceptBatchAsync(Batch(_successor, [Occurrence(_damageEvent, Damage(60), 1)], _stream, _secondSequence));
        Assert.Equal(_firstSequence, ack.GetProperty(_prefixProperty).GetString());
        Assert.Equal(100L, ReadScalar(directory.Path, _damageScalarSql));
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Checks exact quantities above native integer range survive both storage representations.
    /// </summary>
    /// <returns>A task that completes after the asynchronous fixture operations and all described assertions finish.</returns>
    [Fact]
    public async Task WideQuantitiesRemainExactInCoreAndDirectSummary()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        JsonElement payload = JsonSerializer.SerializeToElement(new { actual_hp_lost = _wideDamage, target = new { owner_role = _opponent }, cause = new { kind = _directKind, mechanic_id = _tackle, origin_instigator = new { owner_role = _player } } });
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, payload, 1)]));
        Assert.IsType<byte[]>(ReadScalar(directory.Path, _damageScalarSql));
        Assert.Equal(_wideDamage, ReadScalar(directory.Path, _wideScalarSql));
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Checks a recovered crash boundary remains explicitly incomplete across the fresh stream.
    /// </summary>
    /// <returns>A task that completes after the asynchronous fixture operations and all described assertions finish.</returns>
    [Fact]
    public async Task CrashRecoveryRetainsAnExplicitUnknownTailGap()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_damageEvent, Damage(40), 1)]));
        JsonElement recovery = JsonSerializer.SerializeToElement(new { retirement_kind = _interrupted, predecessor_stream_id = _stream, predecessor_final_sequence = _firstSequence });
        await store.AcceptBatchAsync(Batch(_successor, [Occurrence(_checkpointEvent, recovery, 1)], _stream, _firstSequence));
        JsonElement tables = GameplayCollectionInspector.Read(directory.Path).GetProperty(_tablesProperty);
        Assert.Equal(1, tables.GetProperty(_gapsTable).GetInt64());
        Assert.Equal(258, tables.GetProperty(_gapFamiliesTable).GetInt64());
        Assert.Equal(40L, ReadScalar(directory.Path, _damageScalarSql));
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Checks complete current inventory snapshots replace both exact membership and per-item quantities.
    /// </summary>
    /// <returns>A task that completes after the asynchronous fixture operations and all described assertions finish.</returns>
    [Fact]
    public async Task CurrentInventorySnapshotsReplaceRemovedItemsAndPreserveKnownEmpty()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_inventorySnapshotEvent, Inventory(_potion, 3), 1)]));
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_inventorySnapshotEvent, Inventory(_antidote, 1), 2)]));
        long member = Assert.IsType<long>(ReadScalar(directory.Path, _inventoryMembersSql));
        Assert.Equal(_antidote, ReadStoredValue(directory.Path, member).GetString());
        Assert.Equal(1L, ReadScalar(directory.Path, _inventorySeriesCountSql));
        byte[] quantity = Assert.IsType<byte[]>(ReadScalar(directory.Path, _inventoryQuantitySql));
        using (MemoryStream input = new(quantity, false))
        using (System.IO.Compression.ZLibStream inflater = new(input, System.IO.Compression.CompressionMode.Decompress))
        {
            using MemoryStream bytes = new();
            inflater.CopyTo(bytes);
            JsonElement exact = DecodeStoredValue(directory.Path, bytes.ToArray()).GetProperty(_inventoryQuantityKey);
            Assert.Equal(_knownCoverage, exact.GetProperty(_coverageProperty).GetString());
            Assert.Equal(1, exact.GetProperty(_valueProperty).GetInt32());
        }

        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_inventorySnapshotEvent, Inventory(null, 0), 3)]));
        Assert.Equal(0L, ReadScalar(directory.Path, _inventoryMembersCountSql));
        Assert.Equal(0L, ReadScalar(directory.Path, _inventorySeriesCountSql));
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Creates an exact source damage payload.
    /// </summary>
    /// <param name="amount">The actual HP loss credited to the player, in HP.</param>
    /// <returns>The native-shaped HP loss payload with player-instigator and opponent-recipient evidence.</returns>
    private static JsonElement Damage(long amount)
        => JsonSerializer.SerializeToElement(new { actual_hp_lost = amount, target = new { owner_role = _opponent }, cause = new { kind = _directKind, mechanic_id = _tackle, origin_instigator = new { owner_role = _player } } });

    /// <summary>
    /// Creates a complete current inventory population, including explicit empty.
    /// </summary>
    /// <param name="item">The sole current item identity, or null for a known-empty inventory.</param>
    /// <param name="quantity">The actual current quantity of that item.</param>
    /// <returns>The complete current-inventory snapshot, retaining an explicitly empty population.</returns>
    private static JsonElement Inventory(string? item, int quantity)
        => JsonSerializer.SerializeToElement(new { snapshot_kind = _inventorySnapshotKind, container_ref = _playerBag, item_quantities = item is null ? Array.Empty<object>() : new object[] { new { item_id = item, quantity } }, held_assignments = Array.Empty<object>(), lineage_complete = false });

    /// <summary>
    /// Creates completed real trainer-battle facts with unique battle identities and contiguous source sequences.
    /// </summary>
    /// <param name="count">The number of distinct completed trainer battles to construct.</param>
    /// <param name="firstSequence">The first contiguous delivery sequence to assign to the generated facts.</param>
    /// <returns>Ordered battle-start and battle-end occurrences for the requested distinct trainer victories.</returns>
    private static List<JsonElement> TrainerWins(int count, int firstSequence)
    {
        List<JsonElement> events = [];
        for (int index = 0; index < count; index++)
        {
            int sequence = firstSequence + index * 2;
            string battle = _trainerBattlePrefix + sequence.ToString(CultureInfo.InvariantCulture);
            events.Add(Occurrence(_battleStartedEvent, JsonSerializer.SerializeToElement(new { battle_kind = _trainerKind }), sequence, battle));
            events.Add(Occurrence(_battleEndedEvent, JsonSerializer.SerializeToElement(new { outcome = _winOutcome }), sequence + 1, battle));
        }

        return events;
    }

    /// <summary>
    /// Creates one immutable wire occurrence.
    /// </summary>
    /// <param name="type">The gameplay event type carried by this fixture.</param>
    /// <param name="payload">The native-shaped authoritative payload.</param>
    /// <param name="sequence">The unique contiguous source sequence.</param>
    /// <param name="battle">The actual battle identity, or null for an event outside battle.</param>
    /// <returns>The immutable JSON wire envelope identifying this source occurrence.</returns>
    private static JsonElement Occurrence(string type, JsonElement payload, int sequence, string battle = _battle)
        => JsonSerializer.SerializeToElement(new { sequence = sequence.ToString(CultureInfo.InvariantCulture), type, attempt_id = _attempt, branch_id = _branch, battle_id = battle, occurred_at_us = sequence.ToString(CultureInfo.InvariantCulture), context = new { }, payload });

    /// <summary>
    /// Creates a versioned batch with immutable predecessor binding.
    /// </summary>
    /// <param name="stream">The immutable fixture producer stream UUID.</param>
    /// <param name="occurrences">The contiguous ordered source occurrences in this batch.</param>
    /// <param name="predecessor">The required predecessor stream UUID, or null for the first stream.</param>
    /// <param name="predecessorSequence">The predecessor's required final sequence as a lossless decimal string, or null when absent.</param>
    /// <returns>The versioned batch containing the source range and its immutable predecessor requirements.</returns>
    private static JsonElement Batch(string stream, JsonElement[] occurrences, string? predecessor = null, string? predecessorSequence = null)
        => JsonSerializer.SerializeToElement(new { schema_version = 1, profile_id = _profile, epoch_id = _epoch, stream_id = stream, predecessor_stream_id = predecessor, predecessor_final_sequence = predecessorSequence, events = occurrences });

    /// <summary>
    /// Reads one test scalar through a separate read-only SQLite connection.
    /// </summary>
    /// <param name="directory">The disposable collection profile directory containing the fixture database.</param>
    /// <param name="sql">The SQL statement to execute within the current snapshot or transaction.</param>
    /// <returns>The first result column from the disposable database, or null when the query returns no row.</returns>
    private static object? ReadScalar(string directory, string sql)
    {
        using SqliteConnection connection = OpenRead(directory);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    /// <summary>
    /// Opens a shared read-only snapshot while the real writer still owns its profile.
    /// </summary>
    /// <param name="directory">The disposable collection profile directory containing the fixture database.</param>
    /// <returns>An open read-only connection to the fixture database; the caller disposes it.</returns>
    private static SqliteConnection OpenRead(string directory)
    {
        SqliteConnectionStringBuilder builder = new() { DataSource = System.IO.Path.Combine(directory, _databaseName), Mode = SqliteOpenMode.ReadOnly, Pooling = false };
        SqliteConnection connection = new(builder.ToString());
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Verifies actual SQLite page and foreign-key integrity.
    /// </summary>
    /// <param name="directory">The disposable collection profile directory containing the fixture database.</param>
    private static void AssertIntegrity(string directory)
    {
        Assert.Equal(_integrityOkay, ReadScalar(directory, _integritySql));
        Assert.Null(ReadScalar(directory, _foreignSql));
    }

    /// <summary>
    /// Owns one disposable exact temporary test directory.
    /// </summary>
    private sealed class TestDirectory : IDisposable
    {
        /// <summary>
        /// Allocates a unique directory below the platform temporary root.
        /// </summary>
        public TestDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), _tempPrefix + Guid.NewGuid().ToString(_guidFormat));
            Directory.CreateDirectory(Path);
        }

        private const string _guidFormat = "N";

        /// <summary>
        /// Gets the exact owned temporary path.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Removes only the verified directory created by this fixture.
        /// </summary>
        public void Dispose()
        {
            string root = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
            string owned = System.IO.Path.GetFullPath(Path);
            if (owned.StartsWith(root, StringComparison.OrdinalIgnoreCase) && System.IO.Path.GetFileName(owned).StartsWith(_tempPrefix, StringComparison.Ordinal))
                Directory.Delete(owned, true);
        }
    }
}
