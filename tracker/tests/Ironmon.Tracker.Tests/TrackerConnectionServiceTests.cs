using System.Net;
using System.Net.Sockets;
using Ironmon.Tracker.Connection;
using Ironmon.Tracker.Protocol;

namespace Ironmon.Tracker.Tests;

/// <summary>
/// Verifies persistent loopback connection, handshake, recovery, and error behavior.
/// </summary>
public sealed class TrackerConnectionServiceTests
{
    /// <summary>
    /// Initializes the tracker connection service tests.
    /// </summary>
    public TrackerConnectionServiceTests()
    {
    }

    /// <summary>
    /// Verifies the duplex handshake and current-state recovery request.
    /// </summary>
    [Fact]
    public async Task ServiceCompletesHandshakeAndRecoversCurrentState()
    {
        TrackerConnectionState state = new();
        TrackerRunState runState = new();
        TrackerKnowledgeStore knowledge = CreateKnowledgeStore();
        TrackerConnectionOptions options = new(0, "0.1.0", false, TimeSpan.FromSeconds(2));
        await using TrackerConnectionService service = new(options, state, runState, knowledge);
        service.Start();

        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, service.BoundPort);
        NetworkStream stream = client.GetStream();
        using TrackerMessageReader reader = new(stream, leaveOpen: true);
        await using TrackerMessageWriter writer = new(stream, leaveOpen: true);

        GameHandshakePayload game = new("6.8.0", "0.3.3", true, false, @"C:\Game", "run-1", null);
        TrackerMessage gameHandshake = TrackerMessageFactory.CreateEvent("game_connected", 0, game, "run-1");
        await writer.WriteAsync(gameHandshake);

        TrackerMessage? trackerHandshake = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        TrackerMessage? currentStateRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("tracker_connected", trackerHandshake?.Event);
        Assert.Equal("current_state", currentStateRequest?.Command);
        string requestId = Assert.IsType<string>(currentStateRequest?.RequestId);

        GameCurrentStatePayload currentState = new(true, "run-1", null, 7);
        TrackerMessage response = TrackerMessageFactory.CreateResponse(requestId, currentState, "run-1");
        await writer.WriteAsync(response);

        TrackerConnectionSnapshot connected = await WaitForSnapshotAsync(state, snapshot => snapshot.CurrentState is not null);
        Assert.Equal(TrackerConnectionStatus.Connected, connected.Status);
        Assert.Equal("6.8.0", connected.Game?.GameVersion);
        Assert.Equal(7, connected.CurrentState?.Sequence);

        GameCurrentStatePayload startedState = new(true, "run-2", null, 1);
        TrackerMessage runStarted = TrackerMessageFactory.CreateEvent("run_started", 1, startedState, "run-2");
        await writer.WriteAsync(runStarted);
        TrackerConnectionSnapshot newRun = await WaitForSnapshotAsync(state, snapshot => snapshot.CurrentState?.RunId == "run-2");
        Assert.Equal(1, newRun.CurrentState?.Sequence);
        knowledge.SelectRun(null);

        BattleSnapshot battle = new() { BattleId = "battle-1" };
        TrackerMessage battleStarted = TrackerMessageFactory.CreateEvent("battle_started", 2, battle, "run-2", "battle-1");
        await writer.WriteAsync(battleStarted);
        await WaitForRunSnapshotAsync(runState, snapshot => snapshot.Battle?.BattleId == "battle-1");
        Assert.Equal("run-2", knowledge.RunId);

        EnemyPokemonSnapshot enemy = new()
        {
            EnemyId = "enemy-1",
            Position = 1,
            SpeciesId = "BELLOSSOM:0",
            SpeciesName = "Bellossom",
            Level = 5,
            Types = ["GRASS"],
            BaseStatTotal = 490,
            LastAbility = new AbilitySnapshot
            {
                Id = "CHLOROPHYLL",
                Name = "Chlorophyll",
                Description = "Boosts Speed in sunshine."
            },
            LastMove = new ObservedMoveSnapshot
            {
                Id = "STUNSPORE",
                Name = "Stun Spore",
                LearnedLevel = 0,
                LearnOrder = 0,
                Source = "unknown",
                Origin = "enemy_use",
                Type = "GRASS",
                Power = 0,
                Accuracy = 75,
                TotalPp = 30,
                PpAfterUse = 29
            }
        };

        TrackerMessage enemySentOut = TrackerMessageFactory.CreateEvent("enemy_sent_out", 3, enemy, "run-2", "battle-1");
        await writer.WriteAsync(enemySentOut);
        TrackerRunStateSnapshot enemyState = await WaitForRunSnapshotAsync(runState, snapshot => snapshot.Enemies.Count == 1);
        Assert.Equal("Bellossom", enemyState.Enemies[0].SpeciesName);
        Assert.Equal(5, knowledge.GetHighestLevel("BELLOSSOM:0"));
        Assert.Equal("CHLOROPHYLL", Assert.Single(knowledge.GetAbilities("BELLOSSOM:0")).Id);
        await WaitForKnowledgeAsync(knowledge, "BELLOSSOM:0", 5);
        Assert.Equal("STUNSPORE", Assert.Single(knowledge.GetDisplayedMoves("BELLOSSOM:0", 5)).Id);

        EnemyMoveUsedPayload moveUsed = new()
        {
            EnemyId = "enemy-1",
            SpeciesId = "BELLOSSOM:0",
            EnemyLevel = 5,
            Move = new ObservedMoveSnapshot
            {
                Id = "ABSORB",
                Name = "Absorb",
                LearnedLevel = 0,
                LearnOrder = 0,
                Source = "unknown",
                Origin = "enemy_use",
                Type = "GRASS",
                Power = 20,
                Accuracy = 100,
                TotalPp = 25,
                PpAfterUse = 24
            }
        };
        TrackerMessage enemyMoveUsed = TrackerMessageFactory.CreateEvent("enemy_move_used", 4, moveUsed, "run-2", "battle-1");
        await writer.WriteAsync(enemyMoveUsed);
        await WaitForKnowledgeMoveAsync(knowledge, "BELLOSSOM:0", 5, "ABSORB");
        Assert.Contains(knowledge.GetDisplayedMoves("BELLOSSOM:0", 5), move => move.Id == "ABSORB");

        PlayerPokemonSnapshot player = CreatePlayerSnapshot(24, 24, 2);
        TrackerMessage sentOut = TrackerMessageFactory.CreateEvent("player_sent_out", 5, player, "run-2", "battle-1");
        await writer.WriteAsync(sentOut);
        TrackerRunStateSnapshot playerState = await WaitForRunSnapshotAsync(runState, snapshot => snapshot.Player is not null);
        Assert.Equal("Espeon", playerState.Player?.SpeciesName);
        Assert.Equal(2, playerState.Player?.Healing.ItemCount);

        PlayerMoveMenuOpenedPayload moveMenu = new() { PokemonId = "1234" };
        TrackerMessage moveMenuOpened = TrackerMessageFactory.CreateEvent("player_move_menu_opened", 6, moveMenu, "run-2", "battle-1");
        await writer.WriteAsync(moveMenuOpened);
        TrackerRunStateSnapshot moveMenuState = await WaitForRunSnapshotAsync(runState, snapshot => snapshot.MoveMenuPokemonId == "1234");
        Assert.Equal("1234", moveMenuState.MoveMenuPokemonId);

        PlayerPokemonSnapshot damaged = CreatePlayerSnapshot(12, 24, 1);
        TrackerMessage changed = TrackerMessageFactory.CreateEvent("player_state_changed", 6, damaged, "run-2", "battle-1");
        await writer.WriteAsync(changed);
        TrackerRunStateSnapshot damagedState = await WaitForRunSnapshotAsync(runState, snapshot => snapshot.Player?.CurrentHp == 12);
        Assert.Equal(50, damagedState.Player?.Healing.Percentage);

        TrackerMessage battleEnded = TrackerMessageFactory.CreateEvent("battle_ended", 7, battle, "run-2", "battle-1");
        await writer.WriteAsync(battleEnded);
        TrackerRunStateSnapshot endedState = await WaitForRunSnapshotAsync(runState, snapshot => snapshot.Battle is null);
        Assert.NotNull(endedState.Player);
        Assert.Empty(endedState.Enemies);

        client.Dispose();
        TrackerConnectionSnapshot waiting = await WaitForSnapshotAsync(state, snapshot => snapshot.Status == TrackerConnectionStatus.Waiting);
        Assert.Null(waiting.Game);
    }

    /// <summary>
    /// Verifies that a non-handshake first message is rejected without stopping the listener.
    /// </summary>
    [Fact]
    public async Task ServiceRejectsInvalidFirstMessageAndKeepsListening()
    {
        TrackerConnectionState state = new();
        TrackerRunState runState = new();
        TrackerKnowledgeStore knowledge = CreateKnowledgeStore();
        TrackerConnectionOptions options = new(0, "0.1.0", false, TimeSpan.FromSeconds(2));
        await using TrackerConnectionService service = new(options, state, runState, knowledge);
        service.Start();

        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, service.BoundPort);
        await using TrackerMessageWriter writer = new(client.GetStream());
        Dictionary<string, object?> payload = [];
        TrackerMessage invalid = TrackerMessageFactory.CreateEvent("run_started", 0, payload);
        await writer.WriteAsync(invalid);

        TrackerConnectionSnapshot error = await WaitForSnapshotAsync(state, snapshot => snapshot.Status == TrackerConnectionStatus.Error);
        Assert.Contains("game_connected", error.LastError, StringComparison.Ordinal);
        Assert.True(service.BoundPort > 0);
    }

    /// <summary>
    /// Verifies that a silent client times out without stopping the listener.
    /// </summary>
    [Fact]
    public async Task ServiceRejectsHandshakeTimeoutAndKeepsListening()
    {
        TrackerConnectionState state = new();
        TrackerRunState runState = new();
        TrackerKnowledgeStore knowledge = CreateKnowledgeStore();
        TrackerConnectionOptions options = new(0, "0.1.0", false, TimeSpan.FromMilliseconds(50));
        await using TrackerConnectionService service = new(options, state, runState, knowledge);
        service.Start();

        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, service.BoundPort);

        TrackerConnectionSnapshot error = await WaitForSnapshotAsync(state, snapshot => snapshot.Status == TrackerConnectionStatus.Error);
        Assert.Contains("timed out", error.LastError, StringComparison.Ordinal);
        Assert.True(service.BoundPort > 0);
    }

    /// <summary>
    /// Waits for the connection state to satisfy an integration-test condition.
    /// </summary>
    /// <param name="state">The connection state being observed.</param>
    /// <param name="condition">The condition that completes the wait.</param>
    /// <returns>The first matching connection snapshot.</returns>
    /// <exception cref="TimeoutException">Thrown when no matching snapshot arrives.</exception>
    private static async Task<TrackerConnectionSnapshot> WaitForSnapshotAsync(TrackerConnectionState state, Func<TrackerConnectionSnapshot, bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (DateTimeOffset.UtcNow < deadline)
        {
            TrackerConnectionSnapshot snapshot = state.Snapshot;
            if (condition(snapshot))
                return snapshot;
            await Task.Delay(10);
        }

        throw new TimeoutException("The expected tracker connection state was not published.");
    }

    /// <summary>
    /// Waits for live run state to satisfy an integration-test condition.
    /// </summary>
    /// <param name="state">The live run state being observed.</param>
    /// <param name="condition">The condition that completes the wait.</param>
    /// <returns>The first matching run-state snapshot.</returns>
    /// <exception cref="TimeoutException">Thrown when no matching snapshot arrives.</exception>
    private static async Task<TrackerRunStateSnapshot> WaitForRunSnapshotAsync(TrackerRunState state, Func<TrackerRunStateSnapshot, bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (DateTimeOffset.UtcNow < deadline)
        {
            TrackerRunStateSnapshot snapshot = state.Snapshot;
            if (condition(snapshot))
                return snapshot;

            await Task.Delay(10);
        }

        throw new TimeoutException("The expected tracker run state was not published.");
    }

    /// <summary>
    /// Waits for a received enemy move to enter tracker-owned knowledge.
    /// </summary>
    /// <param name="knowledge">The knowledge store being observed.</param>
    /// <param name="speciesId">The observed enemy species and form.</param>
    /// <param name="level">The visible enemy level.</param>
    /// <returns>A task representing the wait.</returns>
    /// <exception cref="TimeoutException">Thrown when no displayed move arrives.</exception>
    private static async Task WaitForKnowledgeAsync(TrackerKnowledgeStore knowledge, string speciesId, int level)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (knowledge.GetDisplayedMoves(speciesId, level).Count > 0)
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException("The expected enemy move was not remembered.");
    }

    /// <summary>
    /// Waits for a specific received enemy move to enter tracker-owned knowledge.
    /// </summary>
    /// <param name="knowledge">The knowledge store being observed.</param>
    /// <param name="speciesId">The observed enemy species and form.</param>
    /// <param name="level">The visible enemy level.</param>
    /// <param name="moveId">The expected move identifier.</param>
    /// <returns>A task representing the wait.</returns>
    /// <exception cref="TimeoutException">Thrown when the move does not arrive.</exception>
    private static async Task WaitForKnowledgeMoveAsync(TrackerKnowledgeStore knowledge, string speciesId, int level, string moveId)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (knowledge.GetDisplayedMoves(speciesId, level).Any(move => move.Id == moveId))
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException("The expected specific enemy move was not remembered.");
    }

    /// <summary>
    /// Creates a complete player snapshot for connection integration tests.
    /// </summary>
    /// <param name="currentHp">The current HP.</param>
    /// <param name="maximumHp">The maximum HP.</param>
    /// <param name="healingItems">The number of healing items.</param>
    /// <returns>The test player snapshot.</returns>
    private static PlayerPokemonSnapshot CreatePlayerSnapshot(int currentHp, int maximumHp, int healingItems)
    {
        PlayerMoveSnapshot move = new()
        {
            Id = "PSYCHIC",
            Name = "Psychic",
            Type = "PSYCHIC",
            CurrentPp = 10,
            TotalPp = 10,
            Power = 90,
            Accuracy = 100
        };

        HealingInventorySnapshot healing = new()
        {
            ItemCount = healingItems,
            PotentialHp = 12,
            Percentage = 50
        };

        return new PlayerPokemonSnapshot
        {
            PokemonId = "1234",
            SpeciesId = "ESPEON:0",
            Nickname = "Espeon",
            SpeciesName = "Espeon",
            Level = 5,
            CurrentHp = currentHp,
            MaximumHp = maximumHp,
            Status = "NONE",
            Types = ["PSYCHIC"],
            Ability = "Synchronize",
            Attack = 14,
            Defense = 16,
            SpecialAttack = 21,
            SpecialDefense = 18,
            Speed = 15,
            BaseStatTotal = 525,
            Nature = "Hardy",
            Moves = [move],
            Healing = healing
        };
    }

    /// <summary>
    /// Creates an isolated tracker knowledge store for a connection test.
    /// </summary>
    /// <returns>The isolated tracker knowledge store.</returns>
    private static TrackerKnowledgeStore CreateKnowledgeStore()
    {
        string path = Path.Combine(Path.GetTempPath(), "IronmonTrackerTests", Guid.NewGuid().ToString("N"));
        return new TrackerKnowledgeStore(new TrackerKnowledgeOptions(path));
    }
}
