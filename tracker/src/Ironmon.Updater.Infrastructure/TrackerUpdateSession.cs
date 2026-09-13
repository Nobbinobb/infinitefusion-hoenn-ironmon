using Ironmon.Updater.Core;
namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Supplies tracker-owned installation observations and the acknowledged application shutdown boundary.
/// </summary>
public interface ITrackerUpdateHost
{
    /// <summary>
    /// Observes the current installation and conservatively reports unknown run state as active.
    /// </summary>
    /// <returns>The running tracker's package and game selection.</returns>
    IronmonUpdateRequest Observe();

    /// <summary>
    /// Starts a verified independent window before stopping tracker services and closing the application.
    /// </summary>
    /// <param name="prepared">The independently verifiable prepared transaction.</param>
    /// <param name="recover">Whether to recover an interrupted update.</param>
    /// <param name="cancellationToken">The pre-shutdown cancellation token.</param>
    /// <returns>The acknowledged handoff task.</returns>
    Task HandoffAsync(PreparedIronmonUpdate prepared, bool recover, CancellationToken cancellationToken);
}

/// <summary>
/// Coordinates the two-action tracker flow without requiring existing tracker views to own update state.
/// </summary>
/// <remarks>
/// Constructs a session using real discovery, preparation and application lifecycle boundaries.
/// </remarks>
/// <param name="discovery">The bounded signed release discovery service.</param>
/// <param name="verifier">The independent release verifier.</param>
/// <param name="preparation">The signed metadata and transaction workflow.</param>
/// <param name="downloads">The verified cache whose byte progress is displayed.</param>
/// <param name="host">The tracker-owned lifecycle adapter.</param>
public sealed class TrackerUpdateSession(ReleaseDiscovery discovery, ReleaseVerifier verifier, TrackerUpdatePreparation preparation, ReleaseDownloadStore downloads, ITrackerUpdateHost host) : IDisposable
{
    private readonly ReleaseDiscovery _discovery = discovery;
    private readonly ReleaseVerifier _verifier = verifier;
    private readonly TrackerUpdatePreparation _preparation = preparation;
    private readonly ReleaseDownloadStore _downloads = downloads;
    private readonly ITrackerUpdateHost _host = host;
    private readonly HashSet<string> _approvals = new(StringComparer.Ordinal);
    private ReleaseEvidence? _release;
    private CancellationTokenSource? _cancellation;
    private int _busy;
    private bool _started;
    private bool _manualCheckPending;
    private bool _disposed;
    private long _lastProgressTick;

    /// <summary>
    /// Notifies the new updater interface of observable state changes.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Gets whether the dedicated panel is open over the preserved tracker view.
    /// </summary>
    public bool IsOpen { get; private set; }

    /// <summary>
    /// Gets whether the one-per-launch update or recovery invitation is visible.
    /// </summary>
    public bool ShowStartupPrompt { get; private set; }

    /// <summary>
    /// Gets whether the panel was opened from Settings.
    /// </summary>
    public bool OpenedFromSettings { get; private set; }

    /// <summary>
    /// Gets the time of the last successful authenticated check, or null when the latest attempt was unsuccessful.
    /// </summary>
    public DateTimeOffset? CheckedAt { get; private set; }

    /// <summary>
    /// Gets the installed game version when its revision matches authenticated release metadata.
    /// </summary>
    public string? CurrentGameVersion { get; private set; }

    /// <summary>
    /// Gets whether a startup check, review, preparation or handoff is running.
    /// </summary>
    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    /// <summary>
    /// Gets the phase whose localized explanation belongs in the new panel.
    /// </summary>
    public TrackerUpdatePhase Phase { get; private set; }

    /// <summary>
    /// Gets the latest authenticated newer version, if one was found.
    /// </summary>
    public string? AvailableVersion { get; private set; }

    /// <summary>
    /// Gets the running tracker version shown beside the target release.
    /// </summary>
    public string? CurrentVersion { get; private set; }

    /// <summary>
    /// Gets whether interrupted recovery must be resolved before another update.
    /// </summary>
    public bool NeedsRecovery { get; private set; }

    /// <summary>
    /// Gets the independently verified pending recovery handoff.
    /// </summary>
    public PreparedIronmonUpdate? Recovery { get; private set; }

    /// <summary>
    /// Gets the authenticated review and exact local conflicts.
    /// </summary>
    public TrackerUpdateReview? Review { get; private set; }

    /// <summary>
    /// Gets the actionable error without altering the normal tracker's connection state.
    /// </summary>
    public string? Error { get; private set; }

    /// <summary>
    /// Gets the earliest server-authorized retry time.
    /// </summary>
    public DateTimeOffset? RetryAt { get; private set; }

    /// <summary>
    /// Gets measured progress for the current artifact, or null during non-download work.
    /// </summary>
    public ReleaseDownloadProgress? Download { get; private set; }

    /// <summary>
    /// Gets the measured stage of the current update operation.
    /// </summary>
    public InstallationProgress? Progress { get; private set; }

    /// <summary>
    /// Gets whether all exact replaceable conflicts are approved and the Update action is available.
    /// </summary>
    public bool CanUpdate => !IsBusy && !NeedsRecovery && Error is null && Review is { } review && review.Plan.Entries.Where(entry => entry.Action == Core.FilePlanAction.Conflict).All(entry => entry.CanReplaceAfterBackup && _approvals.Contains(entry.Path));

    /// <summary>
    /// Starts one non-disruptive check while leaving the tracker on its normal view.
    /// </summary>
    /// <returns>The startup check.</returns>
    public async Task StartAsync()
    {
        if (_started || _disposed)
            return;

        _started = true;
        _downloads.Progress += HandleDownload;
        await RunAsync(TrackerUpdatePhase.Checking, token => CheckCoreAsync(false, token)).ConfigureAwait(false);
        ShowStartupPrompt = !IsOpen && (NeedsRecovery || AvailableVersion is not null && Error is null);
        Changed?.Invoke();
    }

    /// <summary>
    /// Opens details in one action and loads only authenticated metadata before the Update decision.
    /// </summary>
    /// <returns>The review task, leaving the origin mounted underneath.</returns>
    public async Task OpenAsync()
    {
        ShowStartupPrompt = false;
        OpenedFromSettings = false;
        IsOpen = true;
        Changed?.Invoke();
        if (!_started)
        {
            await StartAsync().ConfigureAwait(false);
        }
        else if (!IsBusy && Review is null && !NeedsRecovery && AvailableVersion is not null)
        {
            await RunAsync(TrackerUpdatePhase.Reviewing, ReviewCoreAsync).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Dismisses the startup invitation for this launch without hiding future update checks.
    /// </summary>
    public void DismissStartupPrompt()
    {
        ShowStartupPrompt = false;
        Changed?.Invoke();
    }

    /// <summary>
    /// Opens Settings updates with a fresh manual check, including when the startup check is still finishing.
    /// </summary>
    /// <returns>The manual check and optional release review.</returns>
    public Task OpenFromSettingsAsync()
    {
        ShowStartupPrompt = false;
        OpenedFromSettings = true;
        IsOpen = true;
        if (!_started)
        {
            _started = true;
            _downloads.Progress += HandleDownload;
        }

        _manualCheckPending = IsBusy && Phase == TrackerUpdatePhase.Checking;
        Changed?.Invoke();
        return CheckAsync();
    }

    /// <summary>
    /// Returns to the mounted origin without changing any existing view or selection.
    /// </summary>
    public void Close()
    {
        if (IsBusy)
            return;

        IsOpen = false;
        Changed?.Invoke();
    }

    /// <summary>
    /// Performs the explicit retry available only inside the new update interface.
    /// </summary>
    /// <returns>The bounded check and renewed review.</returns>
    public Task CheckAsync()
        => RunAsync(TrackerUpdatePhase.Checking, token => CheckCoreAsync(true, token));

    /// <summary>
    /// Records consent only for an exact displayed replaceable conflict.
    /// </summary>
    /// <param name="path">The selected installation-relative path.</param>
    /// <param name="approved">Whether its replacement after backup is approved.</param>
    public void Approve(string path, bool approved)
    {
        if (IsBusy || Review?.Plan.Entries.Any(entry => entry.Path == path && entry.Action == Core.FilePlanAction.Conflict && entry.CanReplaceAfterBackup) != true)
            return;

        if (approved)
        {
            _approvals.Add(path);
        }
        else
        {
            _approvals.Remove(path);
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Gets whether an exact displayed file has replacement consent.
    /// </summary>
    /// <param name="path">The installation-relative conflict path.</param>
    /// <returns>The current consent state.</returns>
    public bool IsApproved(string path)
        => _approvals.Contains(path);

    /// <summary>
    /// Downloads, verifies, backs up and hands off automatically after the player's Update action.
    /// </summary>
    /// <returns>The workflow up to independent window acknowledgement and tracker shutdown.</returns>
    public Task UpdateAsync()
    {
        if (!CanUpdate || Review is not { } review)
            return Task.CompletedTask;

        string[] approvals = [.. _approvals];
        return RunAsync(TrackerUpdatePhase.Preparing, async token =>
        {
            var observation = ObserveSameInstallation(review);
            PreparedIronmonUpdate? prepared = null;
            try
            {
                prepared = await _preparation.PrepareAsync(review, approvals, observation.HasActiveRun, token).ConfigureAwait(false);
                observation = ObserveSameInstallation(review);
                IronmonOnlyUpdate.ValidateSelection(review.Authorization.Request with { HasActiveRun = observation.HasActiveRun }, review.Manifest);
                if (review.Authorization.PreviousGameCommit is not null && observation.HasActiveRun)
                    throw new InvalidOperationException(UpdaterText.TrackerUpdateSessionARunStartedWhileTheUpdateWasBeingPrepared);

                token.ThrowIfCancellationRequested();
                Phase = TrackerUpdatePhase.Handoff;
                Progress = null;
                Download = null;
                Changed?.Invoke();
                await _host.HandoffAsync(prepared, false, token).ConfigureAwait(false);
            }
            catch
            {
                if (prepared is not null)
                    await _preparation.DiscardAsync(prepared, CancellationToken.None).ConfigureAwait(false);

                throw;
            }
        });
    }

    /// <summary>
    /// Opens independent recovery without attempting a second installation transaction.
    /// </summary>
    /// <returns>The acknowledged recovery handoff.</returns>
    public Task RecoverAsync()
    {
        if (!NeedsRecovery || Recovery is not { } recovery)
            return Task.CompletedTask;

        return RunAsync(TrackerUpdatePhase.Handoff, async token =>
        {
            var prepared = await _preparation.PrepareRecoveryAsync(recovery, token).ConfigureAwait(false);
            try
            {
                await _host.HandoffAsync(prepared, true, token).ConfigureAwait(false);
            }
            catch
            {
                if (prepared.Elevation is not null)
                    await ProtectedUpdateClient.EndSessionAsync(prepared).ConfigureAwait(false);

                throw;
            }
        });
    }

    /// <summary>
    /// Cancels pre-shutdown work and waits for owned preparation to be safely released.
    /// </summary>
    public void Cancel()
    {
        _cancellation?.Cancel();
        Changed?.Invoke();
    }

    /// <summary>
    /// Checks recovery first, then obtains only an authenticated forward release.
    /// </summary>
    /// <param name="manual">Whether the player explicitly requested a refresh.</param>
    /// <param name="cancellationToken">The session token.</param>
    /// <returns>The check and optional open-panel review.</returns>
    private async Task CheckCoreAsync(bool manual, CancellationToken cancellationToken)
    {
        Review = null;
        Recovery = null;
        AvailableVersion = null;
        CheckedAt = null;
        RetryAt = null;
        CurrentGameVersion = null;
        _approvals.Clear();
        var observation = _host.Observe();
        CurrentVersion = observation.CurrentVersion;
        NeedsRecovery = UpdateTransaction.ReadActiveId(observation.InstallationRoot) is not null;
        if (NeedsRecovery)
        {
            Recovery = await _preparation.ReadRecoveryAsync(observation.InstallationRoot, cancellationToken).ConfigureAwait(false);
            return;
        }

        var installed = _preparation.ReadCurrentRelease(observation.InstallationRoot, observation.CurrentVersion);
        if (installed is not null)
        {
            var current = _verifier.Verify(installed.Manifest, installed.Signatures);
            if (current.Game.PreferredCommit == observation.GameCommit)
                CurrentGameVersion = current.Game.VersionLabel;
        }

        var found = await _discovery.CheckAsync(manual, cancellationToken).ConfigureAwait(false);
        RetryAt = found.RetryAt;
        Error = found.Error;
        _release = found.Release;
        AvailableVersion = null;
        if (_release is not null)
        {
            var manifest = _verifier.Verify(_release.Manifest, _release.Signatures);
            CheckedAt = found.CheckedAt;
            if (manifest.Game.PreferredCommit == observation.GameCommit)
                CurrentGameVersion = manifest.Game.VersionLabel;
            if (ReleaseProtocol.ParseVersion(manifest.IronmonVersion) > ReleaseProtocol.ParseVersion(observation.CurrentVersion))
                AvailableVersion = manifest.IronmonVersion;
        }

        if (_manualCheckPending)
        {
            _manualCheckPending = false;
            await CheckCoreAsync(true, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (IsOpen && AvailableVersion is not null)
            await ReviewCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Captures current file ownership and release notes without downloading program packages.
    /// </summary>
    /// <param name="cancellationToken">The review token.</param>
    /// <returns>The metadata and conflict inspection.</returns>
    private async Task ReviewCoreAsync(CancellationToken cancellationToken)
    {
        if (_release is null || AvailableVersion is null)
            return;

        Phase = TrackerUpdatePhase.Reviewing;
        Changed?.Invoke();
        var observation = _host.Observe();
        var current = _preparation.ReadCurrentRelease(observation.InstallationRoot, observation.CurrentVersion);
        Review = await _preparation.ReviewAsync(observation, _release, current, cancellationToken).ConfigureAwait(false);
        _approvals.Clear();
    }

    /// <summary>
    /// Rejects changes to the selected installation while observing the latest run state.
    /// </summary>
    /// <param name="review">The captured review selection.</param>
    /// <returns>The current observation of the same installation.</returns>
    private IronmonUpdateRequest ObserveSameInstallation(TrackerUpdateReview review)
    {
        var observed = _host.Observe();
        var selected = review.Authorization.Request;
        if (observed.InstallationRoot != selected.InstallationRoot || observed.CurrentVersion != selected.CurrentVersion || observed.CurrentFlavor != selected.CurrentFlavor || observed.GameCommit != (review.Authorization.PreviousGameCommit ?? selected.GameCommit))
            throw new InvalidOperationException(UpdaterText.TrackerUpdateSessionTheInstallationChangedDuringReviewCheckTheUpdateAgain);

        return observed;
    }

    /// <summary>
    /// Serializes operations and contains nonfatal network, cancellation and validation failures in the update panel.
    /// </summary>
    /// <param name="phase">The initial displayed phase.</param>
    /// <param name="operation">The cancellable workflow.</param>
    /// <returns>The complete operation with an observable terminal state.</returns>
    private async Task RunAsync(TrackerUpdatePhase phase, Func<CancellationToken, Task> operation)
    {
        if (_disposed || Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return;

        Progress = null;
        using var progress = new InstallationProgressScope(value => { Progress = value; Changed?.Invoke(); });
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        Error = null;
        Download = null;
        Phase = phase;
        Changed?.Invoke();
        try
        {
            await Task.Run(() => operation(cancellation.Token), cancellation.Token).ConfigureAwait(false);
            Phase = TrackerUpdatePhase.Ready;
        }
        catch (OperationCanceledException)
        {
            Phase = TrackerUpdatePhase.Cancelled;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or TimeoutException or System.ComponentModel.Win32Exception or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException or System.Net.Http.HttpRequestException)
        {
            Error = error.Message;
            Phase = TrackerUpdatePhase.Ready;
        }
        finally
        {
            Download = null;
            _cancellation = null;
            Volatile.Write(ref _busy, 0);
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Throttles byte notifications while preserving start and verification boundaries.
    /// </summary>
    /// <param name="progress">The actual cache write observation.</param>
    private void HandleDownload(ReleaseDownloadProgress progress)
    {
        if (!IsBusy || Phase != TrackerUpdatePhase.Preparing)
            return;

        Download = progress.Verified ? null : progress;
        var now = Environment.TickCount64;
        if (progress.Received != 0 && !progress.Verified && now - _lastProgressTick < 100)
            return;

        _lastProgressTick = now;
        Changed?.Invoke();
    }

    /// <summary>
    /// Detaches progress and requests cancellation when the application releases this session.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _downloads.Progress -= HandleDownload;
        _cancellation?.Cancel();
    }
}

/// <summary>
/// Identifies tracker-owned phases; installation phases belong exclusively to the independent updater window.
/// </summary>
public enum TrackerUpdatePhase
{
    /// <summary>
    /// No cancellable work is running.
    /// </summary>
    Ready = 0,
    /// <summary>
    /// Signed release discovery or recovery inspection is running.
    /// </summary>
    Checking = 1,
    /// <summary>
    /// Signed metadata and exact conflicts are being inspected.
    /// </summary>
    Reviewing = 2,
    /// <summary>
    /// Packages, verification and recovery backups are being prepared.
    /// </summary>
    Preparing = 3,
    /// <summary>
    /// The independent window is acknowledging readiness before tracker exit.
    /// </summary>
    Handoff = 4,
    /// <summary>
    /// Pre-shutdown work was cancelled safely.
    /// </summary>
    Cancelled = 5
}
