using Ironmon.SpriteLibrary;
using Ironmon.Tracker.Connection.Sprites;
using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Setup.Core;

/// <summary>
/// Supplies explicit Windows prerequisites, normal process closure and optional shell integration.
/// </summary>
public interface ISetupPlatform
{
    /// <summary>
    /// Gets whether the tracker browser runtime is installed.
    /// </summary>
    bool WebViewAvailable { get; }

    /// <summary>
    /// Rejects unsupported operating systems before installation.
    /// </summary>
    void EnsureSupported();

    /// <summary>
    /// Installs the publisher-verified browser runtime after explicit consent and detects it again.
    /// </summary>
    /// <param name="cancellationToken">The prerequisite token.</param>
    /// <returns>The verified prerequisite outcome.</returns>
    Task InstallWebViewAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Requests normal closure only for the selected game and tracker and waits for actual exit.
    /// </summary>
    /// <param name="root">The selected installation.</param>
    /// <param name="cancellationToken">The closure token.</param>
    /// <returns>The idle installation check.</returns>
    Task CloseInstallationAsync(string root, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the opted-in shortcut without replacing another shortcut.
    /// </summary>
    /// <param name="root">The committed installation.</param>
    void CreateShortcut(string root);

    /// <summary>
    /// Launches the installed tracker from the selected installation.
    /// </summary>
    /// <param name="root">The committed installation.</param>
    void OpenTracker(string root);
}

/// <summary>
/// Runs the disposable install UI through shared transactions and independently resumable sprite work.
/// </summary>
/// <remarks>
/// Constructs a session whose optional work cannot roll back a successful core installation.
/// </remarks>
/// <param name="discovery">The authenticated public release discovery.</param>
/// <param name="preparation">The shared signed installer preparation.</param>
/// <param name="transactions">The independently configured installation engine.</param>
/// <param name="sprites">The same persisted sprite service used by the tracker.</param>
/// <param name="platform">The narrow Windows integration boundary.</param>
/// <param name="downloads">The measured shared release cache.</param>
/// <param name="protectedUpdates">The optional Windows administrator boundary.</param>
public sealed class SetupSession(ReleaseDiscovery discovery, SetupPreparation preparation, Func<string, Action<TransactionProgress>, UpdateTransaction> transactions, CustomSpriteSheetInstaller sprites, ISetupPlatform platform, ReleaseDownloadStore downloads, ProtectedUpdateClient? protectedUpdates = null) : IDisposable
{
    private readonly ReleaseDiscovery _discovery = discovery;
    private readonly SetupPreparation _preparation = preparation;
    private readonly Func<string, Action<TransactionProgress>, UpdateTransaction> _transactions = transactions;
    private readonly CustomSpriteSheetInstaller _sprites = sprites;
    private readonly ISetupPlatform _platform = platform;
    private readonly ReleaseDownloadStore _downloads = downloads;
    private readonly ProtectedUpdateClient? _protectedUpdates = protectedUpdates;
    private PreparedIronmonUpdate? _protectedPreparation;
    private CancellationTokenSource? _cancellation;
    private int _busy;
    private bool _subscribed;
    private bool _disposed;
    private bool _wantSprites;
    private bool _wantShortcut;
    private long _lastProgress;
    private readonly HashSet<string> _approvals = new(StringComparer.Ordinal);

    /// <summary>
    /// Notifies the independent Setup window of changed state.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Gets the last complete destination review.
    /// </summary>
    public SetupReview? Review { get; private set; }

    /// <summary>
    /// Gets the selected local folder and its observed existing package.
    /// </summary>
    public SetupDestination? Destination { get; private set; }

    /// <summary>
    /// Gets whether this release requires explicit run-completion confirmation before review can continue.
    /// </summary>
    public bool NeedsRunConfirmation { get; private set; }

    /// <summary>
    /// Gets whether a serialized operation is active.
    /// </summary>
    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    /// <summary>
    /// Gets the current player-facing progress message.
    /// </summary>
    public string Status { get; private set; } = UpdaterText.SetupSessionChooseWhereToInstallIronmon;

    /// <summary>
    /// Gets the current actionable failure without discarding core success.
    /// </summary>
    public string? Error { get; private set; }

    /// <summary>
    /// Gets whether the core installation was independently verified as complete.
    /// </summary>
    public bool CoreInstalled { get; private set; }

    /// <summary>
    /// Gets whether optional work can be retried without reinstalling the core.
    /// </summary>
    public bool OptionalIncomplete { get; private set; }

    /// <summary>
    /// Gets whether prerequisite installation requires consent before Install.
    /// </summary>
    public bool NeedsWebView => !_platform.WebViewAvailable;

    /// <summary>
    /// Gets actual byte progress for the current package.
    /// </summary>
    public ReleaseDownloadProgress? Download { get; private set; }

    /// <summary>
    /// Gets the most recent measured installation stage.
    /// </summary>
    public InstallationProgress? Progress { get; private set; }

    /// <summary>
    /// Gets the monotonic start time of the current operation.
    /// </summary>
    public long StartedAt { get; private set; }

    /// <summary>
    /// Gets the monotonic time of the last measured progress change.
    /// </summary>
    public long ProgressAt { get; private set; }

    /// <summary>
    /// Gets actual completed sprite work after core commit.
    /// </summary>
    public CustomSpriteInstallProgress? SpriteProgress { get; private set; }

    /// <summary>
    /// Gets the bound installation root for completion or recovery.
    /// </summary>
    public string? InstallationRoot { get; private set; }

    /// <summary>
    /// Gets the interrupted transaction that must be recovered first.
    /// </summary>
    public Guid? RecoveryId { get; private set; }

    /// <summary>
    /// Gets whether the displayed conflicts permit the Install action.
    /// </summary>
    public bool CanInstall => !IsBusy && !CoreInstalled && Error is null && Review is { } review && review.Plan.Entries.Where(entry => entry.Action == FilePlanAction.Conflict).All(entry => entry.CanReplaceAfterBackup && _approvals.Contains(entry.Path));

    /// <summary>
    /// Checks a chosen folder locally before presenting installation options.
    /// </summary>
    /// <param name="destination">The selected game directory.</param>
    /// <returns>The local inspection without network requests or installation writes.</returns>
    public Task SelectDestinationAsync(string destination) => RunAsync(_ =>
    {
        _platform.EnsureSupported();
        ResetReview();
        Destination = null;
        Destination = SetupPreparation.InspectDestination(destination);
        Status = Destination.InstalledFlavor is null ? UpdaterText.SetupSessionChooseYourInstallationOptions : UpdaterText.SetupSessionSetupWillKeepYourInstalledTrackerPackage;
        return Task.CompletedTask;
    });

    /// <summary>
    /// Discards stale review errors and consent when the player returns to their installation choices.
    /// </summary>
    public void ClearReview()
    {
        if (IsBusy)
            return;

        ResetReview();
    }

    /// <summary>
    /// Resets the review within the serialized operation or while idle.
    /// </summary>
    private void ResetReview()
    {
        Review = null;
        Error = null;
        RecoveryId = null;
        NeedsRunConfirmation = false;
        _approvals.Clear();
        Status = UpdaterText.SetupSessionChooseYourInstallationOptions;
    }

    /// <summary>
    /// Reviews a destination and stable release before any program package or optional sprite download.
    /// </summary>
    /// <param name="destination">The chosen local destination.</param>
    /// <param name="flavor">An explicit package choice, or null to preserve the installed flavor.</param>
    /// <param name="noActiveRun">Whether the player explicitly confirmed no active Ironmon run.</param>
    /// <returns>The completed metadata review.</returns>
    public Task ReviewAsync(string destination, string? flavor, bool noActiveRun) => RunAsync(async token =>
    {
        _platform.EnsureSupported();
        Review = null;
        NeedsRunConfirmation = false;
        CoreInstalled = false;
        OptionalIncomplete = false;
        _approvals.Clear();
        InstallationRoot = Path.GetFullPath(destination);
        RecoveryId = Directory.Exists(InstallationRoot) ? UpdateTransaction.ReadActiveId(InstallationRoot) : null;
        if (RecoveryId is not null)
        {
            Status = UpdaterText.SetupSessionAnInterruptedInstallationNeedsRecoveryItsBackupsHaveBeen;
            return;
        }

        Status = UpdaterText.SetupSessionCheckingTheLatestReleaseAndYourInstallation;
        Changed?.Invoke();
        var found = await _discovery.CheckAsync(true, token).ConfigureAwait(false);
        var release = found.Release ?? throw new IOException(found.Error ?? UpdaterText.SetupSessionNoSupportedSignedReleaseIsAvailableYetTryAgain);
        Review = await _preparation.ReviewAsync(InstallationRoot, release, flavor, noActiveRun, token).ConfigureAwait(false);
        Status = Review.AlreadyCurrent ? UpdaterText.SetupSessionYourCoreInstallationIsCurrentYouCanAddThe : UpdaterText.SetupSessionReviewYourInstallationAndOptionsThenChooseInstall;
    });

    /// <summary>
    /// Records consent only for a displayed replaceable file conflict.
    /// </summary>
    /// <param name="path">The exact displayed relative path.</param>
    /// <param name="approved">Whether replacement after backup is approved.</param>
    public void Approve(string path, bool approved)
    {
        if (IsBusy || Review?.Plan.Entries.Any(entry => entry.Path == path && entry.CanReplaceAfterBackup) != true)
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
    /// Gets the exact review consent for a displayed path.
    /// </summary>
    /// <param name="path">The displayed path.</param>
    /// <returns>The current approval.</returns>
    public bool IsApproved(string path)
        => _approvals.Contains(path);

    /// <summary>
    /// Installs prerequisites after consent, commits the core, then starts explicitly selected optional work.
    /// </summary>
    /// <param name="installSprites">Whether sprite downloads are selected.</param>
    /// <param name="createShortcut">Whether a desktop tracker shortcut is selected.</param>
    /// <param name="installWebView">Whether the displayed prerequisite installation has explicit consent.</param>
    /// <returns>The core and independent optional outcomes.</returns>
    public Task InstallAsync(bool installSprites, bool createShortcut, bool installWebView)
    {
        if (!CanInstall || Review is not { } review)
            return Task.CompletedTask;

        string[] approvals = [.. _approvals];
        _wantSprites = installSprites;
        _wantShortcut = createShortcut;
        return RunAsync(async token =>
        {
            if (NeedsWebView)
            {
                if (!installWebView)
                    throw new InvalidOperationException(UpdaterText.SetupSessionSelectTheMicrosoftWebView2PrerequisiteBeforeInstallingTheTracker);

                Status = UpdaterText.SetupSessionInstallingMicrosoftWebView2WindowsMayAskForPermission;
                Changed?.Invoke();
                await _platform.InstallWebViewAsync(token).ConfigureAwait(false);
                if (NeedsWebView)
                    throw new InvalidOperationException(UpdaterText.SetupSessionMicrosoftWebView2CouldNotBeDetectedAfterInstallationRetry);
            }

            PreparedIronmonUpdate? prepared = null;
            var applying = false;
            try
            {
                Status = UpdaterText.SetupSessionDownloadingAndVerifyingYourInstallation;
                Changed?.Invoke();
                prepared = await _preparation.PrepareAsync(review, approvals, token).ConfigureAwait(false);
                Status = UpdaterText.SetupSessionClosingTheGameAndTrackerCompleteAnyGameSave;
                Progress = null;
                Download = null;
                Changed?.Invoke();
                await _platform.CloseInstallationAsync(prepared.InstallationRoot, token).ConfigureAwait(false);
                applying = true;
                var result = prepared.Elevation is not null
                    ? await ProtectedUpdateClient.ApplyAsync(prepared, token).ConfigureAwait(false)
                    : await Engine(prepared.InstallationRoot).ApplyAsync(prepared.InstallationRoot, prepared.TransactionId, cancellationToken: token).ConfigureAwait(false);

                if (result.Phase != TransactionPhase.Committed)
                    throw new IOException(result.Error ?? UpdaterText.SetupSessionInstallationDidNotFinishYourPreviousFilesWereRestored);

                CoreInstalled = true;
                _protectedPreparation = prepared.Elevation is null ? null : prepared;
            }
            catch
            {
                if (prepared is not null && !applying)
                {
                    if (prepared.Elevation is not null)
                    {
                        await ProtectedUpdateClient.DiscardAsync(prepared, CancellationToken.None).ConfigureAwait(false);
                    }
                    else
                    {
                        await Engine(prepared.InstallationRoot).DiscardPreparedAsync(prepared.InstallationRoot, prepared.TransactionId, CancellationToken.None).ConfigureAwait(false);
                    }
                }

                throw;
            }
            finally
            {
                if (!CoreInstalled && prepared?.Elevation is not null)
                    await ProtectedUpdateClient.EndSessionAsync(prepared).ConfigureAwait(false);
            }

            await OptionalAsync(token).ConfigureAwait(false);
        });
    }

    /// <summary>
    /// Restores an interrupted transaction using the shared independently authenticated recovery engine.
    /// </summary>
    /// <returns>The recovery outcome without starting a second install.</returns>
    public Task RecoverAsync() => RunAsync(async token =>
    {
        var root = InstallationRoot ?? throw new InvalidOperationException(UpdaterText.SetupSessionReviewTheInstallationFolderFirst);
        var id = RecoveryId ?? throw new InvalidOperationException(UpdaterText.SetupSessionNoInterruptedInstallationWasSelected);
        var elevated = _protectedUpdates is not null && ProtectedUpdateClient.RequiresElevation(root) ? await _protectedUpdates.OpenRecoveryAsync(root, id, token).ConfigureAwait(false) : null;
        try
        {
            if (elevated is null)
                await Engine(root).ValidateRecoveryAsync(root, id, token).ConfigureAwait(false);

            await _platform.CloseInstallationAsync(root, token).ConfigureAwait(false);
            var result = elevated is not null ? await ProtectedUpdateClient.RecoverAsync(elevated, token).ConfigureAwait(false) : await Engine(root).RecoverAsync(root, id, token).ConfigureAwait(false);
            RecoveryId = UpdateTransaction.ReadActiveId(root);
            Status = result.Phase == TransactionPhase.RolledBack ? UpdaterText.SetupSessionYourPreviousFilesWereRestoredReviewTheFolderAgain : UpdaterText.SetupSessionRecoveryCompleted;
            if (result.Phase == TransactionPhase.RecoveryRequired)
                throw new IOException(result.Error ?? UpdaterText.SetupSessionRecoveryNeedsAttentionYourBackupsHaveBeenKept);
        }
        finally
        {
            if (elevated is not null)
                await ProtectedUpdateClient.EndSessionAsync(elevated).ConfigureAwait(false);
        }
    });

    /// <summary>
    /// Retries only incomplete optional work after a committed core installation.
    /// </summary>
    /// <returns>The optional outcome without repeating the core transaction.</returns>
    public Task RetryOptionalAsync()
        => CoreInstalled ? RunAsync(OptionalAsync) : Task.CompletedTask;

    /// <summary>
    /// Launches only after core success; failures remain separate from installation success.
    /// </summary>
    /// <returns>The launch request outcome.</returns>
    public Task OpenTrackerAsync() => CoreInstalled ? RunAsync(_ =>
    {
        _platform.OpenTracker(InstallationRoot!);
        return Task.CompletedTask;
    }) : Task.CompletedTask;

    /// <summary>
    /// Requests safe cancellation; committed core files remain installed when optional work stops.
    /// </summary>
    public void Cancel()
        => _cancellation?.Cancel();

    /// <summary>
    /// Runs optional downloads and shell integration after core commit, preserving partial progress for the tracker.
    /// </summary>
    /// <param name="cancellationToken">The optional-work token.</param>
    /// <returns>The independently resumable optional outcome.</returns>
    private async Task OptionalAsync(CancellationToken cancellationToken)
    {
        OptionalIncomplete = _wantSprites || _wantShortcut;
        if (_wantShortcut)
        {
            try
            {
                _platform.CreateShortcut(InstallationRoot!);
                _wantShortcut = false;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                Error = UpdaterText.SetupSessionIronmonIsInstalledButTheDesktopShortcutCouldNot + error.Message;
            }
        }

        if (_wantSprites)
        {
            InstallationProgressScope.Report(new(InstallationStage.DownloadingSprites));
            Status = UpdaterText.SetupSessionIronmonIsInstalledDownloadingTheOptionalSpriteLibrary;
            Changed?.Invoke();
            if (_protectedPreparation is null && _protectedUpdates is not null && ProtectedUpdateClient.RequiresElevation(InstallationRoot!))
                _protectedPreparation = await _protectedUpdates.OpenInstalledAsync(InstallationRoot!, cancellationToken).ConfigureAwait(false);

            if (_protectedPreparation is { } prepared)
            {
                _wantSprites = (await ProtectedUpdateClient.InstallSpritesAsync(prepared, cancellationToken: cancellationToken).ConfigureAwait(false)).Failed > 0;
            }
            else
            {
                var plan = _sprites.CreatePlan(InstallationRoot);
                var result = await _sprites.InstallAsync(plan, new SpriteObserver(this), cancellationToken).ConfigureAwait(false);
                _wantSprites = result.FailedSheetCount > 0;
            }
            if (_wantSprites)
                Error = UpdaterText.SetupSessionIronmonIsInstalledSomeSpriteFilesCouldNotBe;
        }

        OptionalIncomplete = _wantSprites || _wantShortcut;
        Status = OptionalIncomplete ? UpdaterText.SetupSessionIronmonIsInstalledOptionalWorkIsIncomplete : UpdaterText.SetupSessionIronmonIsInstalledYouCanOpenTheTrackerAnd;
    }

    /// <summary>
    /// Creates the independent transaction boundary and translates durable progress into Setup status.
    /// </summary>
    /// <param name="root">The bound installation.</param>
    /// <returns>The configured shared engine.</returns>
    private UpdateTransaction Engine(string root) => _transactions(root, progress =>
    {
        Status = progress.Phase == TransactionPhase.RollingBack ? UpdaterText.SetupSessionRestoringYourPreviousFiles : UpdaterText.SetupSessionInstallingAndVerifyingFiles;
        Report();
    });

    /// <summary>
    /// Serializes operations and keeps failures visible without losing committed core state.
    /// </summary>
    /// <param name="operation">The cancellable workflow.</param>
    /// <returns>The complete serialized operation.</returns>
    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (_disposed || Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return;

        if (!_subscribed)
        {
            _subscribed = true;
            _downloads.Progress += HandleDownload;
        }

        Progress = null;
        StartedAt = ProgressAt = Environment.TickCount64;
        using var progress = new InstallationProgressScope(value => { Progress = value; ProgressAt = Environment.TickCount64; Changed?.Invoke(); });
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        Error = null;
        Changed?.Invoke();
        try
        {
            await Task.Run(() => operation(cancellation.Token), cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            OptionalIncomplete = CoreInstalled && (_wantSprites || _wantShortcut);
            Status = CoreInstalled ? UpdaterText.SetupSessionIronmonIsInstalledRemainingOptionalWorkCanContinueIn : UpdaterText.SetupSessionCancelledSafelyReviewTheInstallationBeforeTryingAgain;
        }
        catch (SetupRunConfirmationRequiredException error)
        {
            NeedsRunConfirmation = true;
            Error = error.Message;
            Status = UpdaterText.SetupSessionFinishYourRunBeforeContinuing;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception or System.Net.Http.HttpRequestException or System.Text.Json.JsonException or System.Security.Cryptography.CryptographicException or TimeoutException)
        {
            Error = error.Message;
            Status = CoreInstalled ? UpdaterText.SetupSessionIronmonIsInstalledTheLastActionNeedsAttention : UpdaterText.SetupSessionSetupCouldNotCompleteThisStep;
        }
        finally
        {
            if (_protectedPreparation is { } completed)
            {
                await ProtectedUpdateClient.EndSessionAsync(completed).ConfigureAwait(false);
                _protectedPreparation = null;
            }

            Download = null;
            _cancellation = null;
            Volatile.Write(ref _busy, 0);
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Captures measured shared-cache download progress for the native window.
    /// </summary>
    /// <param name="progress">The current verified asset transfer.</param>
    private void HandleDownload(ReleaseDownloadProgress progress)
    {
        Download = progress.Verified ? null : progress;
        Report();
    }

    /// <summary>
    /// Bounds native UI refreshes during large transfers.
    /// </summary>
    private void Report()
    {
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastProgress) < 100)
            return;

        Interlocked.Exchange(ref _lastProgress, now);
        Changed?.Invoke();
    }

    /// <summary>
    /// Detaches shared progress when the disposable window closes after work has stopped.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _downloads.Progress -= HandleDownload;
        _cancellation?.Cancel();
        if (_protectedPreparation is { } prepared && !IsBusy)
            _ = ProtectedUpdateClient.EndSessionAsync(prepared);
    }

    /// <summary>
    /// Observes sprite progress without capturing a tracker or native synchronization context.
    /// </summary>
    /// <remarks>
    /// Connects the shared sprite service to this disposable session.
    /// </remarks>
    /// <param name="session">The owning Setup session.</param>
    private sealed class SpriteObserver(SetupSession session) : IProgress<CustomSpriteInstallProgress>
    {
        /// <summary>
        /// Stores completed sheet progress and requests a bounded refresh.
        /// </summary>
        /// <param name="value">The measured shared sprite progress.</param>
        public void Report(CustomSpriteInstallProgress value)
        {
            session.SpriteProgress = value;
            InstallationProgressScope.Report(new(InstallationStage.DownloadingSprites, value.CompletedSheetCount, value.TotalSheetCount, value.DownloadedBytes));
            session.Report();
        }
    }
}
