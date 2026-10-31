namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies connection-scoped startup stages, retired identities and lifecycle resets.
/// </summary>
public sealed class TrackerStartupPreparationStateTests
{
    private const string _first = "startup-one";
    private const string _second = "startup-two";
    private const string _loading = "loading_save";
    private const string _world = "world_data";
    private const string _sessionOne = "session-one";
    private const string _sessionTwo = "session-two";
    private const string _runOne = "run-one";
    private const string _runTwo = "run-two";
    private const string _gameVersion = "6.8.0";
    private const string _trackerVersion = "test";

    /// <summary>
    /// Verifies stage changes publish immutable snapshots and repeated stages remain quiet.
    /// </summary>
    [Fact]
    public void StageChangesPublishWithoutDuplicateNotifications()
    {
        TrackerStartupPreparationState state = new();
        int changes = 0;
        state.Changed += (_, _) => { _ = state.Snapshot; changes++; };
        Assert.Same(TrackerStartupPreparationSnapshot.Idle, state.Snapshot);
        Assert.True(state.Observe(Payload(_first, StartupPreparationProgressPayload.RunningStatus, _loading)));
        TrackerStartupPreparationSnapshot first = state.Snapshot;
        Assert.False(state.Observe(Payload(_first, StartupPreparationProgressPayload.RunningStatus, _loading)));
        Assert.True(state.Observe(Payload(_first, StartupPreparationProgressPayload.RunningStatus, _world)));
        Assert.Equal(_loading, first.Phase);
        Assert.Equal(_world, state.Snapshot.Phase);
        Assert.Equal(2, changes);
    }

    /// <summary>
    /// Verifies old loading attempts cannot replace a newer attempt or reopen completed progress.
    /// </summary>
    [Fact]
    public void RetiredAndTerminalAttemptsRejectLateUpdates()
    {
        TrackerStartupPreparationState state = new();
        Assert.False(state.Observe(Payload(_first, StartupPreparationProgressPayload.CompleteStatus, _world)));
        state.Observe(Payload(_first, StartupPreparationProgressPayload.RunningStatus, _loading));
        state.Observe(Payload(_second, StartupPreparationProgressPayload.RunningStatus, _loading));
        Assert.False(state.Observe(Payload(_first, StartupPreparationProgressPayload.CompleteStatus, _world)));
        Assert.False(state.Observe(Payload(_first, StartupPreparationProgressPayload.RunningStatus, _world)));
        Assert.Equal(_second, state.Snapshot.PreparationId);
        Assert.True(state.Observe(Payload(_second, StartupPreparationProgressPayload.CompleteStatus, _world)));
        TrackerStartupPreparationSnapshot complete = state.Snapshot;
        Assert.False(state.Observe(Payload(_second, StartupPreparationProgressPayload.RunningStatus, _loading)));
        Assert.False(state.Observe(Payload(_second, StartupPreparationProgressPayload.FailedStatus, _world)));
        Assert.Same(complete, state.Snapshot);
        state.Reset();
        Assert.Same(TrackerStartupPreparationSnapshot.Idle, state.Snapshot);
        Assert.True(state.Observe(Payload(_first, StartupPreparationProgressPayload.RunningStatus, _loading)));
    }

    /// <summary>
    /// Verifies failure details remain terminal and malformed lifecycle values are ignored.
    /// </summary>
    [Fact]
    public void FailureAndInvalidPayloadsPreserveValidState()
    {
        TrackerStartupPreparationState state = new();
        Assert.False(state.Observe(new()));
        Assert.False(state.Observe(Payload(_first, _world, _loading)));
        state.Observe(Payload(_first, StartupPreparationProgressPayload.RunningStatus, _loading));
        const string error = "The save could not be prepared.";
        Assert.True(state.Observe(new() { PreparationId = _first, Status = StartupPreparationProgressPayload.FailedStatus, Phase = _world, Error = error }));
        Assert.Equal(TrackerStartupPreparationStatus.Failed, state.Snapshot.Status);
        Assert.Equal(error, state.Snapshot.Error);
        Assert.False(state.Observe(Payload(_first, StartupPreparationProgressPayload.CompleteStatus, _world)));
    }

    /// <summary>
    /// Verifies failure dismissal keeps retired attempts closed and cannot cancel active loading.
    /// </summary>
    [Fact]
    public void DismissFailurePreservesRetiredIdentityAndDoesNotCancelRunningPreparation()
    {
        TrackerStartupPreparationState state = new();
        int changes = 0;
        state.Changed += (_, _) => changes++;
        Assert.False(state.DismissFailure());
        state.Observe(Payload(_first, StartupPreparationProgressPayload.RunningStatus, _loading));
        TrackerStartupPreparationSnapshot running = state.Snapshot;
        Assert.False(state.DismissFailure());
        Assert.Same(running, state.Snapshot);
        state.Observe(Payload(_first, StartupPreparationProgressPayload.FailedStatus, _world));
        Assert.True(state.DismissFailure());
        Assert.Same(TrackerStartupPreparationSnapshot.Idle, state.Snapshot);
        Assert.Equal(3, changes);
        Assert.False(state.Observe(Payload(_first, StartupPreparationProgressPayload.RunningStatus, _loading)));
        Assert.False(state.Observe(Payload(_first, StartupPreparationProgressPayload.CompleteStatus, _world)));
        Assert.False(state.Observe(Payload(_first, StartupPreparationProgressPayload.FailedStatus, _world)));
        Assert.Equal(3, changes);
        Assert.True(state.Observe(Payload(_second, StartupPreparationProgressPayload.RunningStatus, _loading)));
        Assert.Equal(TrackerStartupPreparationStatus.Running, state.Snapshot.Status);
    }

    /// <summary>
    /// Verifies same-session run recovery preserves loading while connection and session transitions clear it.
    /// </summary>
    [Fact]
    public void RunChangesPreserveLoadingButSessionAndConnectionChangesResetIt()
    {
        TrackerConnectionState state = new();
        GameHandshakePayload game = new(_gameVersion, _trackerVersion, true, false, TrackerTestPaths.GameRoot, _runOne, null) { SessionId = _sessionOne };
        state.Publish(TrackerConnectionStatus.Connected, game);
        state.StartupPreparation.Observe(Payload(_first, StartupPreparationProgressPayload.RunningStatus, _loading));
        TrackerStartupPreparationSnapshot running = state.StartupPreparation.Snapshot;
        state.Publish(TrackerConnectionStatus.Connected, game, new(true, _runTwo, null, 0));
        Assert.Same(running, state.StartupPreparation.Snapshot);
        GameHandshakePayload nextGame = new(_gameVersion, _trackerVersion, true, false, TrackerTestPaths.GameRoot, _runTwo, null) { SessionId = _sessionTwo };
        state.Publish(TrackerConnectionStatus.Connected, nextGame);
        Assert.Same(TrackerStartupPreparationSnapshot.Idle, state.StartupPreparation.Snapshot);
        TrackerConnectionStatus[] disconnected = [TrackerConnectionStatus.Waiting, TrackerConnectionStatus.Handshaking, TrackerConnectionStatus.Error, TrackerConnectionStatus.Stopped];
        foreach (TrackerConnectionStatus status in disconnected)
        {
            state.StartupPreparation.Observe(Payload(_first, StartupPreparationProgressPayload.RunningStatus, _loading));
            state.Publish(status);
            Assert.Same(TrackerStartupPreparationSnapshot.Idle, state.StartupPreparation.Snapshot);
        }
    }

    /// <summary>
    /// Creates one minimal stage notification.
    /// </summary>
    /// <param name="id">The loading attempt identity.</param>
    /// <param name="status">The wire lifecycle value.</param>
    /// <param name="phase">The current stage identifier.</param>
    /// <returns>A stage-only startup payload.</returns>
    private static StartupPreparationProgressPayload Payload(string id, string status, string phase)
        => new() { PreparationId = id, Status = status, Phase = phase };
}
