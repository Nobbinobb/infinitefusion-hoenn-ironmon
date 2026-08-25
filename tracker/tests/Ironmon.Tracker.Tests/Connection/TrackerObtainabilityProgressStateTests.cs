namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies the shared active-run obtainability progress lifecycle.
/// </summary>
public sealed class TrackerObtainabilityProgressStateTests
{
    private const string _completePhase = "complete";
    private const string _firstRunId = "progress-run-one";
    private const string _playerFusionsPhase = "player_fusions";
    private const string _secondRunId = "progress-run-two";

    /// <summary>
    /// Verifies that progress remains complete for one run and resets for the next run.
    /// </summary>
    [Fact]
    public void ProgressTracksOneRunThroughCompletion()
    {
        TrackerObtainabilityProgressState state = new();

        state.Begin(_firstRunId, TrackerObtainabilityProgressScope.ActiveRun);
        state.Report(_firstRunId, TrackerObtainabilityProgressScope.ActiveRun, new PokemonObtainabilityResponsePayload
        {
            Phase = _playerFusionsPhase,
            Complete = true,
            ProcessedPairs = 0,
            TotalPairs = 137_550,
            ObtainableCount = 1_286
        });

        TrackerObtainabilityProgressSnapshot running = state.Snapshot;
        Assert.Equal(TrackerObtainabilityProgressStatus.Running, running.Status);
        Assert.Equal(TrackerObtainabilityProgressScope.ActiveRun, running.Scope);
        Assert.Equal(_playerFusionsPhase, running.Phase);
        Assert.Equal(137_550, running.TotalPairs);
        Assert.Equal(1_286, running.ObtainableCount);
        Assert.True(running.Elapsed >= TimeSpan.Zero);

        state.Report(_firstRunId, TrackerObtainabilityProgressScope.ActiveRun, new PokemonObtainabilityResponsePayload
        {
            Phase = _completePhase,
            Complete = true,
            ProcessedPairs = 137_550,
            TotalPairs = 137_550,
            ObtainableCount = 104_378
        });

        TrackerObtainabilityProgressSnapshot complete = state.Snapshot;
        Assert.Equal(TrackerObtainabilityProgressStatus.Complete, complete.Status);
        Assert.Equal(137_550, complete.ProcessedPairs);
        Assert.Equal(104_378, complete.ObtainableCount);

        state.Begin(_firstRunId, TrackerObtainabilityProgressScope.ActiveRun);
        Assert.Same(complete, state.Snapshot);

        state.Begin(_secondRunId, TrackerObtainabilityProgressScope.ArchivedRun);
        Assert.Equal(TrackerObtainabilityProgressStatus.Running, state.Snapshot.Status);
        Assert.Equal(TrackerObtainabilityProgressScope.ArchivedRun, state.Snapshot.Scope);
        Assert.Equal(TimeSpan.Zero, state.Snapshot.Elapsed);

        state.Report(_firstRunId, TrackerObtainabilityProgressScope.ActiveRun, new PokemonObtainabilityResponsePayload { Phase = _completePhase, Complete = true });
        Assert.Equal(TrackerObtainabilityProgressStatus.Running, state.Snapshot.Status);

        state.Cancel(_firstRunId, TrackerObtainabilityProgressScope.ArchivedRun);
        Assert.Equal(TrackerObtainabilityProgressStatus.Running, state.Snapshot.Status);

        state.Cancel(_secondRunId, TrackerObtainabilityProgressScope.ArchivedRun);
        Assert.Same(TrackerObtainabilityProgressSnapshot.Idle, state.Snapshot);

        state.Reset();
        Assert.Same(TrackerObtainabilityProgressSnapshot.Idle, state.Snapshot);
    }
}
