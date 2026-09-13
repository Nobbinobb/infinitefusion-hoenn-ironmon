using Ironmon.Updater.Core;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater;

/// <summary>
/// Shows progress and recovery independently while the game and tracker are closed.
/// </summary>
internal sealed class UpdaterWindow : Window
{
    private readonly string[] _arguments;
    private readonly ITransactionAuthority _authority;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TextBlock _status;
    private readonly ProgressBar _progress;
    private readonly Button _action;
    private bool _running;
    private UpdaterHandoffRequest? _request;
    private bool _retryRecovery;
    private PreparedIronmonUpdate? _directProtected;
    private const string CacheDirectory = "Ironmon/Updater/downloads";

    /// <summary>
    /// Builds the independent native progress window without modifying any existing tracker view.
    /// </summary>
    /// <param name="arguments">The bounded process entry point.</param>
    /// <param name="authority">The independently supplied release and component verifier.</param>
    internal UpdaterWindow(string[] arguments, ITransactionAuthority authority)
    {
        _arguments = [.. arguments];
        _authority = authority;
        Title = UpdaterText.UpdaterWindowIronmonUpdate;
        Width = 510;
        Height = 285;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(22, 19, 31));
        Foreground = new SolidColorBrush(Color.FromRgb(237, 233, 242));
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new TextBlock { Text = UpdaterText.UpdaterWindowIronmonUpdate, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
        _status = new TextBlock { Text = UpdaterText.UpdaterWindowPreparingIndependentRecovery, TextWrapping = TextWrapping.Wrap, MinHeight = 56 };
        _progress = new ProgressBar { IsIndeterminate = true, Height = 5, Margin = new Thickness(0, 12, 0, 20) };
        _action = new Button { Content = UpdaterText.UpdaterWindowCancel, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(18, 5, 18, 5) };
        _action.Click += HandleAction;
        panel.Children.Add(_status);
        panel.Children.Add(_progress);
        panel.Children.Add(_action);
        Content = panel;
        Loaded += HandleLoaded;
        Closing += HandleClosing;
        Closed += HandleClosed;
    }

    /// <summary>
    /// Runs a bounded handoff or direct recovery and keeps installation failures visible in this independent process.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="args">The load event.</param>
    private async void HandleLoaded(object sender, RoutedEventArgs args)
    {
        _running = true;
        using var progressScope = new InstallationProgressScope(value => Dispatcher.InvokeAsync(() =>
        {
            if (!_running)
                return;

            _status.Text = value.Text;
            _progress.IsIndeterminate = value.Total <= 0;
            _progress.Maximum = Math.Max(1, value.Total);
            _progress.Value = value.Completed;
            if (value.Stage == InstallationStage.RestoringFiles)
                _action.IsEnabled = false;
        }));
        try
        {
            TransactionResult result;
            if (_arguments.Length == 2 && _arguments[0] == UpdaterHandoff.HandoffArgument)
            {
                var request = _request ?? await UpdaterHandoff.AcceptAsync(_arguments[1], ValidateHandoffAsync, _cancellation.Token);
                if (_retryRecovery && request.ProtectedPreparation is not null)
                    request = request with { ProtectedPreparation = await OpenProtectedRecoveryAsync(request.InstallationRoot, request.TransactionId, _cancellation.Token) };

                _request = request;
                if (request.ProtectedPreparation is { } protectedPreparation)
                {
                    await ProtectedUpdateClient.PreserveAsync(protectedPreparation, request.Navigation, _cancellation.Token);
                }
                else
                {
                    TrackerRelaunch.Preserve(request);
                }
                SetStatus(UpdaterText.UpdaterWindowClosingTheTrackerAndWaitingForTheGameTo);
                if (request.Game is not null)
                    await request.Game.WaitForExitAsync(requestClose: true, SetStatus, _cancellation.Token);

                await request.Tracker.WaitForExitAsync(requestClose: false, SetStatus, _cancellation.Token);
                if (request.Recover || _retryRecovery)
                {
                    result = request.ProtectedPreparation is { } recovery
                        ? await ProtectedUpdateClient.RecoverAsync(recovery, _cancellation.Token)
                        : await Engine(request.InstallationRoot).RecoverAsync(request.InstallationRoot, request.TransactionId, _cancellation.Token);
                }
                else
                {
                    result = request.ProtectedPreparation is { } preparation
                        ? await ProtectedUpdateClient.ApplyAsync(preparation, _cancellation.Token)
                        : await Engine(request.InstallationRoot).ApplyAsync(request.InstallationRoot, request.TransactionId, cancellationToken: _cancellation.Token);
                }

                if (result.Phase is TransactionPhase.Committed or TransactionPhase.RolledBack)
                {
                    try
                    {
                        await TrackerRelaunch.StartAsync(request.InstallationRoot, request.TransactionId);
                    }
                    catch (Exception error)
                    {
                        result = result with { RelaunchError = error.Message };
                    }
                }

                _retryRecovery = result.Phase == TransactionPhase.RecoveryRequired;
            }
            else if (_arguments.Length == 3 && _arguments[0] == UpdaterHandoff.RecoverArgument && Guid.TryParse(_arguments[2], out var id))
            {
                SetStatus(UpdaterText.UpdaterWindowCheckingTheInterruptedUpdateAndItsRecoveryBackup);
                result = await RecoverDirectAsync(_arguments[1], id, _cancellation.Token);
                _retryRecovery = result.Phase == TransactionPhase.RecoveryRequired;
            }
            else
            {
                throw new InvalidOperationException(UpdaterText.UpdaterWindowStartThisHelperFromTheTrackerOrUseThe);
            }

            SetStatus(result.Phase switch
            {
                TransactionPhase.Committed => result.RelaunchError ?? result.Error ?? UpdaterText.UpdaterWindowTheUpdateFinishedSuccessfully,
                TransactionPhase.RolledBack => UpdaterText.UpdaterWindowThePreviousInstallationHasBeenRestored + (result.RelaunchError ?? result.Error),
                _ => UpdaterText.UpdaterWindowRecoveryNeedsAttentionYourBackupsHaveBeenKept + result.Error
            });
        }
        catch (OperationCanceledException)
        {
            await CancelPreparedAsync();
        }
        catch (Exception error)
        {
            _retryRecovery = _request is not null || _directProtected is not null;
            SetStatus(error.Message);
        }
        finally
        {
            if (_request?.ProtectedPreparation is { } prepared)
                await ProtectedUpdateClient.EndSessionAsync(prepared);

            if (_directProtected is { } direct)
            {
                await ProtectedUpdateClient.EndSessionAsync(direct);
                _directProtected = null;
            }

            _running = false;
            _progress.IsIndeterminate = false;
            _action.Content = _retryRecovery && !_cancellation.IsCancellationRequested ? UpdaterText.UpdaterWindowRetryRecovery : UpdaterText.UpdaterWindowClose;
            _action.IsEnabled = true;
        }
    }

    /// <summary>
    /// Independently authenticates the selected operation before acknowledging that the tracker may close.
    /// </summary>
    /// <param name="request">The bounded tracker handoff.</param>
    /// <param name="cancellationToken">The acknowledgement token.</param>
    /// <returns>The preparation or recovery authorization check.</returns>
    private Task ValidateHandoffAsync(UpdaterHandoffRequest request, CancellationToken cancellationToken)
        => request.ProtectedPreparation is { } prepared ? ProtectedUpdateClient.ValidateAsync(prepared, cancellationToken) : request.Recover ? Engine(request.InstallationRoot).ValidateRecoveryAsync(request.InstallationRoot, request.TransactionId, cancellationToken) : Engine(request.InstallationRoot).ValidatePreparedAsync(request.InstallationRoot, request.TransactionId, cancellationToken);

    /// <summary>
    /// Requests administrator recovery from the independent retained helper when the tracker cannot start.
    /// </summary>
    /// <param name="root">The affected installation.</param>
    /// <param name="id">The retained transaction.</param>
    /// <param name="cancellationToken">The recovery token.</param>
    /// <returns>The independently authenticated restoration result.</returns>
    private async Task<TransactionResult> RecoverDirectAsync(string root, Guid id, CancellationToken cancellationToken)
    {
        if (!ProtectedUpdateClient.RequiresElevation(root))
            return await Engine(root).RecoverAsync(root, id, cancellationToken);

        _directProtected = await OpenProtectedRecoveryAsync(root, id, cancellationToken);
        return await ProtectedUpdateClient.RecoverAsync(_directProtected, cancellationToken);
    }

    /// <summary>
    /// Opens a fresh administrator capability for each explicit recovery attempt.
    /// </summary>
    /// <param name="root">The selected installation.</param>
    /// <param name="id">The interrupted transaction.</param>
    /// <param name="cancellationToken">The recovery token.</param>
    /// <returns>The authenticated recovery session.</returns>
    private static async Task<PreparedIronmonUpdate> OpenProtectedRecoveryAsync(string root, Guid id, CancellationToken cancellationToken)
    {
        using var http = new ReleaseHttpClient();
        var downloads = new ReleaseDownloadStore(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), CacheDirectory), http);
        var client = new ProtectedUpdateClient(UpdaterTrust.CreateVerifier(), downloads);
        return await client.OpenRecoveryAsync(root, id, cancellationToken);
    }

    /// <summary>
    /// Releases untouched preparation and reopens the tracker when cancellation happens while waiting for game closure.
    /// </summary>
    /// <returns>The safe cancellation outcome, retaining independent recovery if application had already begun.</returns>
    private async Task CancelPreparedAsync()
    {
        if (_request is not { Recover: false } request || _retryRecovery)
        {
            SetStatus(UpdaterText.UpdaterWindowRecoveryWasCancelledBeforeRestorationYourBackupsHaveBeen);
            return;
        }

        try
        {
            if (request.ProtectedPreparation is { } prepared)
            {
                await ProtectedUpdateClient.DiscardAsync(prepared);
            }
            else
            {
                await Engine(request.InstallationRoot).DiscardPreparedAsync(request.InstallationRoot, request.TransactionId);
            }
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await request.Tracker.WaitForExitAsync(requestClose: false, cancellationToken: wait.Token);
            await TrackerRelaunch.StartAsync(request.InstallationRoot, request.TransactionId);
            SetStatus(UpdaterText.UpdaterWindowTheUpdateWasCancelledYourInstalledGameAndTracker);
        }
        catch (Exception error)
        {
            SetStatus(UpdaterText.UpdaterWindowCancellationNeedsAttentionYourRecoveryCopiesHaveBeenKept + error.Message);
        }
    }

    /// <summary>
    /// Creates the same transaction engine for normal application and standalone recovery.
    /// </summary>
    /// <param name="root">The authorized installation.</param>
    /// <returns>The process-independent transaction engine.</returns>
    private UpdateTransaction Engine(string root)
        => new(_authority, token => UpdateProcessIdentity.EnsureInstallationIdleAsync(root, UpdaterHandoff.TrackerRelativePath, token));

    /// <summary>
    /// Displays a process-waiting or final status on this helper's own UI thread.
    /// </summary>
    /// <param name="message">The user-facing status.</param>
    private void SetStatus(string message)
        => Dispatcher.Invoke(() => _status.Text = message);

    /// <summary>
    /// Cancels preparation or requests rollback, then waits for the engine to reach a safe terminal state.
    /// </summary>
    /// <param name="sender">The action button.</param>
    /// <param name="args">The click event.</param>
    private void HandleAction(object sender, RoutedEventArgs args)
    {
        if (_running)
        {
            _cancellation.Cancel();
            _action.IsEnabled = false;
            SetStatus(UpdaterText.UpdaterWindowCancellingSafelyPleaseWaitWhileAnyChangedFilesAre);
        }
        else if (_retryRecovery && !_cancellation.IsCancellationRequested)
        {
            _progress.IsIndeterminate = true;
            _action.Content = UpdaterText.UpdaterWindowCancel;
            HandleLoaded(this, new RoutedEventArgs());
        }
        else
        {
            Close();
        }
    }

    /// <summary>
    /// Treats closing the window as a cancellation request rather than abandoning a live transaction.
    /// </summary>
    /// <param name="sender">The helper window.</param>
    /// <param name="args">The cancellable close event.</param>
    private void HandleClosing(object? sender, CancelEventArgs args)
    {
        if (!_running)
            return;

        args.Cancel = true;
        HandleAction(this, new RoutedEventArgs());
    }

    /// <summary>
    /// Releases cancellation resources after the engine has stopped and the helper window is closed.
    /// </summary>
    /// <param name="sender">The helper window.</param>
    /// <param name="args">The close event.</param>
    private void HandleClosed(object? sender, EventArgs args)
        => _cancellation.Dispose();
}
