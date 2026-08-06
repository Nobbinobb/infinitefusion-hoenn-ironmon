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
        TrackerConnectionOptions options = new(0, "0.1.0", false, TimeSpan.FromSeconds(2));
        await using TrackerConnectionService service = new(options, state, runState);
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

        BattleSnapshot battle = new() { BattleId = "battle-1" };
        TrackerMessage battleStarted = TrackerMessageFactory.CreateEvent("battle_started", 2, battle, "run-2", "battle-1");
        await writer.WriteAsync(battleStarted);
        await WaitForRunSnapshotAsync(runState, snapshot => snapshot.Battle?.BattleId == "battle-1");

        PlayerPokemonSnapshot player = CreatePlayerSnapshot(24, 24, 2);
        TrackerMessage sentOut = TrackerMessageFactory.CreateEvent("player_sent_out", 3, player, "run-2", "battle-1");
        await writer.WriteAsync(sentOut);
        TrackerRunStateSnapshot playerState = await WaitForRunSnapshotAsync(runState, snapshot => snapshot.Player is not null);
        Assert.Equal("Espeon", playerState.Player?.SpeciesName);
        Assert.Equal(2, playerState.Player?.Healing.ItemCount);

        PlayerPokemonSnapshot damaged = CreatePlayerSnapshot(12, 24, 1);
        TrackerMessage changed = TrackerMessageFactory.CreateEvent("player_state_changed", 4, damaged, "run-2", "battle-1");
        await writer.WriteAsync(changed);
        TrackerRunStateSnapshot damagedState = await WaitForRunSnapshotAsync(runState, snapshot => snapshot.Player?.CurrentHp == 12);
        Assert.Equal(50, damagedState.Player?.Healing.Percentage);

        TrackerMessage battleEnded = TrackerMessageFactory.CreateEvent("battle_ended", 5, battle, "run-2", "battle-1");
        await writer.WriteAsync(battleEnded);
        TrackerRunStateSnapshot endedState = await WaitForRunSnapshotAsync(runState, snapshot => snapshot.Battle is null);
        Assert.NotNull(endedState.Player);

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
        TrackerConnectionOptions options = new(0, "0.1.0", false, TimeSpan.FromSeconds(2));
        await using TrackerConnectionService service = new(options, state, runState);
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
        TrackerConnectionOptions options = new(0, "0.1.0", false, TimeSpan.FromMilliseconds(50));
        await using TrackerConnectionService service = new(options, state, runState);
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
    private static async Task<TrackerConnectionSnapshot> WaitForSnapshotAsync(
        TrackerConnectionState state,
        Func<TrackerConnectionSnapshot, bool> condition)
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
    private static async Task<TrackerRunStateSnapshot> WaitForRunSnapshotAsync(
        TrackerRunState state,
        Func<TrackerRunStateSnapshot, bool> condition)
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
}
