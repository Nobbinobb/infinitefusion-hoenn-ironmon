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
        TrackerConnectionOptions options = new(0, "0.1.0", false, TimeSpan.FromSeconds(2));
        await using TrackerConnectionService service = new(options, state);
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
        TrackerConnectionOptions options = new(0, "0.1.0", false, TimeSpan.FromSeconds(2));
        await using TrackerConnectionService service = new(options, state);
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
        TrackerConnectionOptions options = new(0, "0.1.0", false, TimeSpan.FromMilliseconds(50));
        await using TrackerConnectionService service = new(options, state);
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
}
