using System.Diagnostics;

namespace Ironmon.Tracker.Connection.Transport;

/// <summary>
/// Identifies the lifecycle state of the shared active-run obtainability calculation.
/// </summary>
public enum TrackerObtainabilityProgressStatus
{
    /// <summary>
    /// No active-run calculation has started.
    /// </summary>
    Idle = 0,

    /// <summary>
    /// The shared calculation is active.
    /// </summary>
    Running = 1,

    /// <summary>
    /// The shared calculation completed successfully.
    /// </summary>
    Complete = 2,

    /// <summary>
    /// The shared calculation stopped with an error.
    /// </summary>
    Error = 3
}

/// <summary>
/// Identifies which run surface owns the displayed obtainability calculation.
/// </summary>
public enum TrackerObtainabilityProgressScope
{
    /// <summary>
    /// The calculation belongs to the currently active game run.
    /// </summary>
    ActiveRun = 0,

    /// <summary>
    /// The calculation belongs to a selected archived run.
    /// </summary>
    ArchivedRun = 1
}

/// <summary>
/// Represents one immutable view of active-run obtainability progress.
/// </summary>
/// <remarks>
/// Initializes a progress snapshot from one game response.
/// </remarks>
/// <param name="status">The calculation lifecycle state.</param>
/// <param name="scope">The run surface owning the calculation.</param>
/// <param name="phase">The current game-owned calculation phase.</param>
/// <param name="processedPairs">The number of completed fusion-material pairs.</param>
/// <param name="totalPairs">The total number of fusion-material pairs.</param>
/// <param name="obtainableCount">The current number of proven obtainable species.</param>
/// <param name="elapsed">The total calculation wall time.</param>
/// <param name="phaseElapsed">The wall time spent in the current phase.</param>
/// <param name="error">The terminal error when the calculation failed.</param>
public sealed class TrackerObtainabilityProgressSnapshot(TrackerObtainabilityProgressStatus status, TrackerObtainabilityProgressScope scope, string phase, int processedPairs, int totalPairs, int obtainableCount, TimeSpan elapsed, TimeSpan phaseElapsed, string? error)
{
    /// <summary>
    /// Gets the shared empty progress snapshot.
    /// </summary>
    public static TrackerObtainabilityProgressSnapshot Idle { get; } = new(TrackerObtainabilityProgressStatus.Idle, TrackerObtainabilityProgressScope.ActiveRun, string.Empty, 0, 0, 0, TimeSpan.Zero, TimeSpan.Zero, null);

    /// <summary>
    /// Gets the calculation lifecycle state.
    /// </summary>
    public TrackerObtainabilityProgressStatus Status { get; } = status;

    /// <summary>
    /// Gets the run surface owning the calculation.
    /// </summary>
    public TrackerObtainabilityProgressScope Scope { get; } = scope;

    /// <summary>
    /// Gets the current game-owned calculation phase.
    /// </summary>
    public string Phase { get; } = phase;

    /// <summary>
    /// Gets the number of completed fusion-material pairs.
    /// </summary>
    public int ProcessedPairs { get; } = processedPairs;

    /// <summary>
    /// Gets the total number of fusion-material pairs.
    /// </summary>
    public int TotalPairs { get; } = totalPairs;

    /// <summary>
    /// Gets the current number of proven obtainable species.
    /// </summary>
    public int ObtainableCount { get; } = obtainableCount;

    /// <summary>
    /// Gets the total calculation wall time.
    /// </summary>
    public TimeSpan Elapsed { get; } = elapsed;

    /// <summary>
    /// Gets the wall time spent in the current phase.
    /// </summary>
    public TimeSpan PhaseElapsed { get; } = phaseElapsed;

    /// <summary>
    /// Gets the terminal calculation error when one exists.
    /// </summary>
    public string? Error { get; } = error;
}

/// <summary>
/// Publishes thread-safe active-run obtainability progress to tracker interface consumers.
/// </summary>
public sealed class TrackerObtainabilityProgressState
{
    private const string _completePhase = "complete";
    private const int _maximumPreparedRuns = 4;
    private readonly Lock _sync = new();
    private readonly Dictionary<string, TrackerObtainabilityProgressSnapshot> _preparedActiveRuns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TrackerObtainabilityProgressSnapshot> _preparedArchivedRuns = new(StringComparer.Ordinal);
    private readonly HashSet<string> _startedActiveRuns = new(StringComparer.Ordinal);
    private string? _activeRunId;
    private TrackerObtainabilityProgressScope _scope;
    private string _phase = string.Empty;
    private long _phaseStartedTimestamp;
    private TrackerObtainabilityProgressSnapshot _snapshot = TrackerObtainabilityProgressSnapshot.Idle;
    private long _startedTimestamp;

    /// <summary>
    /// Initializes an empty obtainability progress state.
    /// </summary>
    public TrackerObtainabilityProgressState()
    {
    }

    /// <summary>
    /// Occurs after the progress snapshot changes.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the latest immutable progress snapshot.
    /// </summary>
    public TrackerObtainabilityProgressSnapshot Snapshot
    {
        get
        {
            lock (_sync)
                return _snapshot;
        }
    }

    /// <summary>
    /// Gets active-run preparation progress without borrowing an archive's status or losing completed preparation when another surface owns progress.
    /// </summary>
    /// <param name="runId">The currently displayed active run identifier.</param>
    /// <returns>The matching active progress, retained completion, or idle state.</returns>
    public TrackerObtainabilityProgressSnapshot GetActiveRunSnapshot(string? runId)
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(runId))
                return TrackerObtainabilityProgressSnapshot.Idle;

            if (_preparedActiveRuns.TryGetValue(runId, out TrackerObtainabilityProgressSnapshot? prepared))
                return prepared;

            return _scope == TrackerObtainabilityProgressScope.ActiveRun && string.Equals(_activeRunId, runId, StringComparison.Ordinal)
                ? _snapshot
                : TrackerObtainabilityProgressSnapshot.Idle;
        }
    }

    /// <summary>
    /// Gets whether this connection has confirmed full preparation for one active or archived run.
    /// </summary>
    /// <param name="runId">The run whose completed preparation should be checked.</param>
    /// <returns>Whether full preparation is retained for the matching run.</returns>
    public bool HasCompletedPreparation(string? runId)
    {
        lock (_sync)
            return runId is not null && (_preparedActiveRuns.ContainsKey(runId) || _preparedArchivedRuns.ContainsKey(runId));
    }

    /// <summary>
    /// Marks one run's shared calculation as active.
    /// </summary>
    /// <param name="runId">The active run identifier.</param>
    /// <param name="scope">The run surface owning the calculation.</param>
    internal void Begin(string? runId, TrackerObtainabilityProgressScope scope)
    {
        bool changed;
        lock (_sync)
        {
            if (scope == TrackerObtainabilityProgressScope.ActiveRun && !string.IsNullOrWhiteSpace(runId))
                _startedActiveRuns.Add(runId);

            if (string.Equals(_activeRunId, runId, StringComparison.Ordinal) && _scope == scope
                && _snapshot.Status is TrackerObtainabilityProgressStatus.Running or TrackerObtainabilityProgressStatus.Complete)
            {
                return;
            }

            long now = Stopwatch.GetTimestamp();
            _activeRunId = runId;
            _scope = scope;
            _phase = string.Empty;
            _startedTimestamp = now;
            _phaseStartedTimestamp = now;
            _snapshot = new TrackerObtainabilityProgressSnapshot(TrackerObtainabilityProgressStatus.Running, scope, string.Empty, 0, 0, 0, TimeSpan.Zero, TimeSpan.Zero, null);
            changed = true;
        }

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Publishes one intermediate or terminal game response.
    /// </summary>
    /// <param name="runId">The reporting run identifier.</param>
    /// <param name="scope">The reporting run surface.</param>
    /// <param name="response">The current shared calculation response.</param>
    internal void Report(string? runId, TrackerObtainabilityProgressScope scope, PokemonObtainabilityResponsePayload response)
    {
        ArgumentNullException.ThrowIfNull(response);
        lock (_sync)
        {
            bool ownsProgress = string.Equals(_activeRunId, runId, StringComparison.Ordinal) && _scope == scope;
            bool prepared = response.BackgroundComplete || string.Equals(response.Phase, _completePhase, StringComparison.Ordinal);
            if (!ownsProgress && !(prepared && scope == TrackerObtainabilityProgressScope.ActiveRun && runId is not null && _startedActiveRuns.Contains(runId)))
                return;

            long now = Stopwatch.GetTimestamp();
            if (ownsProgress && !string.Equals(_phase, response.Phase, StringComparison.Ordinal))
            {
                _phase = response.Phase;
                _phaseStartedTimestamp = now;
            }

            TrackerObtainabilityProgressStatus status = prepared
                ? TrackerObtainabilityProgressStatus.Complete
                : TrackerObtainabilityProgressStatus.Running;

            TrackerObtainabilityProgressSnapshot progress = new(status, scope, response.Phase, response.ProcessedPairs, response.TotalPairs, response.ObtainableCount, ownsProgress ? Stopwatch.GetElapsedTime(_startedTimestamp, now) : TimeSpan.Zero, ownsProgress ? Stopwatch.GetElapsedTime(_phaseStartedTimestamp, now) : TimeSpan.Zero, null);
            if (ownsProgress)
                _snapshot = progress;

            if (prepared && !string.IsNullOrWhiteSpace(runId))
            {
                Dictionary<string, TrackerObtainabilityProgressSnapshot> preparedRuns = scope == TrackerObtainabilityProgressScope.ActiveRun ? _preparedActiveRuns : _preparedArchivedRuns;
                preparedRuns[runId] = progress;
                if (preparedRuns.Count > _maximumPreparedRuns)
                    preparedRuns.Remove(preparedRuns.Keys.First());
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Marks the active calculation as failed.
    /// </summary>
    /// <param name="runId">The reporting run identifier.</param>
    /// <param name="scope">The reporting run surface.</param>
    /// <param name="error">The reported terminal error.</param>
    internal void Fail(string? runId, TrackerObtainabilityProgressScope scope, string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        lock (_sync)
        {
            if (!string.Equals(_activeRunId, runId, StringComparison.Ordinal) || _scope != scope)
                return;

            long now = Stopwatch.GetTimestamp();
            _snapshot = new TrackerObtainabilityProgressSnapshot(TrackerObtainabilityProgressStatus.Error, _scope, _phase, _snapshot.ProcessedPairs, _snapshot.TotalPairs, _snapshot.ObtainableCount, Stopwatch.GetElapsedTime(_startedTimestamp, now), Stopwatch.GetElapsedTime(_phaseStartedTimestamp, now), error);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Clears one still-running calculation when its owner cancels it.
    /// </summary>
    /// <param name="runId">The canceled run identifier.</param>
    /// <param name="scope">The canceled run surface.</param>
    internal void Cancel(string runId, TrackerObtainabilityProgressScope scope)
    {
        bool changed = false;
        lock (_sync)
        {
            if (string.Equals(_activeRunId, runId, StringComparison.Ordinal) && _scope == scope && _snapshot.Status == TrackerObtainabilityProgressStatus.Running)
            {
                _activeRunId = null;
                _snapshot = TrackerObtainabilityProgressSnapshot.Idle;
                changed = true;
            }
        }

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Clears progress for a disconnected game.
    /// </summary>
    internal void Reset()
    {
        lock (_sync)
        {
            _activeRunId = null;
            _scope = TrackerObtainabilityProgressScope.ActiveRun;
            _preparedActiveRuns.Clear();
            _preparedArchivedRuns.Clear();
            _startedActiveRuns.Clear();
            _phase = string.Empty;
            _startedTimestamp = 0;
            _phaseStartedTimestamp = 0;
            _snapshot = TrackerObtainabilityProgressSnapshot.Idle;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
