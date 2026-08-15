using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Ironmon.Tracker.App.Components.Pages;

/// <summary>
/// Coordinates the tracker shell, connection state, and primary view navigation.
/// </summary>
public partial class Home : IDisposable
{
    private TrackerConnectionSnapshot _connection = new(TrackerConnectionStatus.Stopped, null, null, null);
    private TrackerRunStateSnapshot _run = new(null, null);
    private TrackerView _selectedView = TrackerView.Player;
    private string? _selectedEnemyId;
    private string? _lastMoveMenuPokemonId;
    private bool _completedRunNavigationPending;
    private bool _accessOpen;
    private bool _settingsOpen;
    private bool _autoSelectStarter;
    private int? _maximumStarterBaseStatTotal;
    private string? _settingsStatus;

    /// <summary>
    /// Gets or initializes the shared connection status service.
    /// </summary>
    [Inject]
    private TrackerConnectionState ConnectionState { get; set; } = null!;

    /// <summary>
    /// Gets or initializes the shared live run state.
    /// </summary>
    [Inject]
    private TrackerRunState RunState { get; set; } = null!;

    /// <summary>
    /// Gets or initializes the active tracker connection service.
    /// </summary>
    [Inject]
    private TrackerConnectionService TrackerConnection { get; set; } = null!;

    /// <summary>
    /// Gets or initializes the completed-run archive and selection source.
    /// </summary>
    [Inject]
    private CompletedRunArchive CompletedRuns { get; set; } = null!;

    /// <summary>
    /// Gets or initializes the foreground-safe global shortcut service.
    /// </summary>
    [Inject]
    private TrackerGlobalShortcutService ShortcutService { get; set; } = null!;

    /// <summary>
    /// Gets or initializes mutable listener and handshake options.
    /// </summary>
    [Inject]
    private TrackerConnectionOptions ConnectionOptions { get; set; } = null!;

    /// <summary>
    /// Gets or initializes tracker-owned diagnostic access.
    /// </summary>
    [Inject]
    private DiagnosticAccessService AccessService { get; set; } = null!;

    /// <summary>
    /// Subscribes the tracker shell to connection and run-state changes.
    /// </summary>
    protected override void OnInitialized()
    {
        _connection = ConnectionState.Snapshot;
        _run = RunState.Snapshot;
        _selectedEnemyId = _run.Enemies.Count > 0 ? _run.Enemies[0].EnemyId : null;
        _lastMoveMenuPokemonId = _run.MoveMenuPokemonId;
        _autoSelectStarter = ConnectionOptions.AutoSelectStarter;
        _maximumStarterBaseStatTotal = ConnectionOptions.MaximumStarterBaseStatTotal;
        if (_selectedEnemyId is not null)
            _selectedView = TrackerView.Enemy;

        ConnectionState.Changed += HandleConnectionChanged;
        RunState.Changed += HandleRunChanged;
        CompletedRuns.SelectionRequested += HandleCompletedRunSelectionRequested;
        ShortcutService.ViewRequested += HandleGlobalViewRequested;
        AccessService.Changed += HandleDiagnosticAccessChanged;
    }

    /// <summary>
    /// Selects the requested primary tracker view.
    /// </summary>
    /// <param name="view">The view selected by the user.</param>
    private void SelectView(TrackerView view)
    {
        if (view == TrackerView.Debug && !CanShowDebug)
            return;

        _completedRunNavigationPending = false;
        _accessOpen = false;
        _settingsOpen = false;
        _selectedView = view;
    }

    /// <summary>
    /// Opens or closes tracker settings.
    /// </summary>
    private void ToggleSettings()
    {
        _settingsOpen = !_settingsOpen;
        _accessOpen = false;
        _settingsStatus = null;
    }

    /// <summary>
    /// Opens or closes the always-available diagnostic-access screen.
    /// </summary>
    private void ToggleAccess()
    {
        _accessOpen = !_accessOpen;
        _settingsOpen = false;
    }

    /// <summary>
    /// Gets whether the debug navigation entry currently has any local authorization path.
    /// </summary>
    private bool CanShowDebug => TrackerConnection.DebugAuthorized || AccessService.Snapshot.IsActive;

    /// <summary>
    /// Gets the visual classes for the diagnostic-access button.
    /// </summary>
    /// <returns>The access button CSS classes.</returns>
    private string GetAccessButtonClass()
        => _accessOpen ? "settings-button selected" : "settings-button";

    /// <summary>
    /// Gets the visual classes for the settings button.
    /// </summary>
    /// <returns>The settings button CSS classes.</returns>
    private string GetSettingsButtonClass()
        => _settingsOpen ? "settings-button selected" : "settings-button";

    /// <summary>
    /// Persists and synchronizes the automatic starter-selection setting.
    /// </summary>
    /// <param name="enabled">Whether automatic selection is enabled.</param>
    /// <returns>A task representing game synchronization.</returns>
    private async Task HandleAutoSelectStarterChanged(bool enabled)
    {
        _autoSelectStarter = enabled;
        ConnectionOptions.AutoSelectStarter = enabled;
        Preferences.Default.Set(TrackerApplicationConstants.AutoSelectStarterPreferenceKey, enabled);
        if (_connection.Status != TrackerConnectionStatus.Connected)
        {
            _settingsStatus = Text["Settings.Page.AppliesOnConnection"];
            return;
        }

        try
        {
            TrackerSettingsPayload settings = CreateSettingsPayload();
            await TrackerConnection.Requests.UpdateSettingsAsync(settings);
            _settingsStatus = Text["Settings.Page.Saved"];
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or TrackerProtocolException or TimeoutException)
        {
            _settingsStatus = Text["Settings.Page.AppliesOnConnection"];
        }
    }

    /// <summary>
    /// Persists and synchronizes the inclusive maximum starter BST.
    /// </summary>
    /// <param name="maximum">The ceiling, or null to disable it.</param>
    /// <returns>A task representing game synchronization.</returns>
    private async Task HandleMaximumStarterBaseStatTotalChanged(int? maximum)
    {
        if (maximum is not null && (maximum < StarterSelectionConstants.MinimumBaseStatTotal || maximum > StarterSelectionConstants.MaximumBaseStatTotal))
            return;

        _maximumStarterBaseStatTotal = maximum;
        ConnectionOptions.MaximumStarterBaseStatTotal = maximum;
        Preferences.Default.Set(TrackerApplicationConstants.MaximumStarterBaseStatTotalPreferenceKey, maximum ?? 0);
        if (_connection.Status != TrackerConnectionStatus.Connected)
        {
            _settingsStatus = Text["Settings.Page.AppliesOnConnection"];
            return;
        }

        try
        {
            await TrackerConnection.Requests.UpdateSettingsAsync(CreateSettingsPayload());
            _settingsStatus = Text["Settings.Page.Saved"];
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or TrackerProtocolException or TimeoutException)
        {
            _settingsStatus = Text["Settings.Page.AppliesOnConnection"];
        }
    }

    /// <summary>
    /// Synchronizes a changed Favorite Clause list with the connected game.
    /// </summary>
    /// <param name="speciesIds">The complete stable normal-species identifier list.</param>
    /// <returns>A task representing game synchronization.</returns>
    private async Task HandleFavoriteSpeciesIdsChanged(IReadOnlyList<string> speciesIds)
    {
        ConnectionOptions.FavoriteSpeciesIds = speciesIds;
        if (_connection.Status != TrackerConnectionStatus.Connected)
        {
            _settingsStatus = Text["Settings.Page.AppliesOnConnection"];
            return;
        }

        try
        {
            await TrackerConnection.Requests.UpdateSettingsAsync(CreateSettingsPayload());
            _settingsStatus = Text["Settings.Page.Saved"];
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or TrackerProtocolException or TimeoutException)
        {
            _settingsStatus = Text["Settings.Page.AppliesOnConnection"];
        }
    }

    /// <summary>
    /// Creates the complete current tracker settings payload.
    /// </summary>
    /// <returns>The settings synchronized to the game.</returns>
    private TrackerSettingsPayload CreateSettingsPayload()
        => new() { AutoSelectStarter = _autoSelectStarter, MaximumStarterBaseStatTotal = _maximumStarterBaseStatTotal, FavoriteSpeciesIds = ConnectionOptions.FavoriteSpeciesIds };

    /// <summary>
    /// Handles keyboard shortcuts for switching between primary tracker views.
    /// </summary>
    /// <param name="args">The browser keyboard event.</param>
    private void HandleKeyDown(KeyboardEventArgs args)
    {
        if (!args.CtrlKey)
            return;

        TrackerView? requestedView = args.Key.ToUpperInvariant() switch
        {
            TrackerKeyboardKeys.PlayerNumber => TrackerView.Player,
            TrackerKeyboardKeys.EnemyNumber => TrackerView.Enemy,
            TrackerKeyboardKeys.LookupNumber => TrackerView.Lookup,
            TrackerKeyboardKeys.DebugNumber when CanShowDebug => TrackerView.Debug,
            _ => null
        };

        if (requestedView is not null)
            SelectView(requestedView.Value);
    }

    /// <summary>
    /// Applies a view requested while Infinite Fusion owns keyboard or controller focus.
    /// </summary>
    /// <param name="view">The requested tracker view.</param>
    private void HandleGlobalViewRequested(TrackerView view)
    {
        _ = InvokeAsync(() =>
        {
            SelectView(view);
            StateHasChanged();
        });
    }

    /// <summary>
    /// Gets the CSS class for a primary tracker tab.
    /// </summary>
    /// <param name="view">The tab's tracker view.</param>
    /// <returns>The tab CSS classes.</returns>
    private string GetTabClass(TrackerView view)
        => view == _selectedView ? TrackerUiConstants.SelectedViewTabCssClass : TrackerUiConstants.ViewTabCssClass;

    /// <summary>
    /// Gets the tab-container class for the authorized number of views.
    /// </summary>
    /// <returns>The tab-container CSS classes.</returns>
    private string GetViewTabsClass()
        => CanShowDebug ? TrackerUiConstants.DebugViewTabsCssClass : TrackerUiConstants.ViewTabsCssClass;

    /// <summary>
    /// Gets the concise connection state shown in the tracker header.
    /// </summary>
    /// <returns>The connection state label.</returns>
    private string GetConnectionText() => _connection.Status switch
    {
        TrackerConnectionStatus.Waiting => Text["App.Shell.WaitingForGame"],
        TrackerConnectionStatus.Handshaking => Text["App.Shell.Connecting"],
        TrackerConnectionStatus.Connected => Text["App.Shell.GameConnected"],
        TrackerConnectionStatus.Error => Text["App.Shell.ConnectionError"],
        TrackerConnectionStatus.Stopped => Text["App.Shell.TrackerStopped"],
        _ => Text["App.Shell.UnknownState"]
    };

    /// <summary>
    /// Gets the detailed connection information shown as a tooltip.
    /// </summary>
    /// <returns>The connection detail.</returns>
    private string GetConnectionDetail()
    {
        if (_connection.LastError is not null)
            return _connection.LastError;

        if (_connection.Game is not null)
            return Text["App.Shell.ConnectedGameVersions", _connection.Game.GameVersion, _connection.Game.IronmonVersion];

        return Text["App.Shell.ListeningOn", TrackerProtocol.LoopbackHost, TrackerProtocol.Port];
    }

    /// <summary>
    /// Gets the CSS classes for the connection state indicator.
    /// </summary>
    /// <returns>The connection indicator CSS classes.</returns>
    private string GetConnectionDotClass() => _connection.Status switch
    {
        TrackerConnectionStatus.Connected => TrackerUiConstants.ConnectedDotCssClass,
        TrackerConnectionStatus.Error => TrackerUiConstants.ErrorDotCssClass,
        TrackerConnectionStatus.Handshaking => TrackerUiConstants.HandshakingDotCssClass,
        _ => TrackerUiConstants.ConnectionDotCssClass
    };

    /// <summary>
    /// Gets the active enemy selected for move-effectiveness calculations.
    /// </summary>
    /// <returns>The selected enemy, or the first active enemy as a fallback.</returns>
    private EnemyPokemonSnapshot? GetSelectedEnemy()
    {
        EnemyPokemonSnapshot? selected = _run.Enemies.FirstOrDefault(enemy => enemy.EnemyId == _selectedEnemyId);
        return selected ?? (_run.Enemies.Count > 0 ? _run.Enemies[0] : null);
    }

    /// <summary>
    /// Refreshes the shell after a background connection change.
    /// </summary>
    /// <param name="sender">The connection state raising the event.</param>
    /// <param name="args">The connection change event arguments.</param>
    private void HandleConnectionChanged(object? sender, EventArgs args)
    {
        _connection = ConnectionState.Snapshot;
        if (_selectedView == TrackerView.Debug && !CanShowDebug)
            _selectedView = TrackerView.Player;

        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Refreshes the shell and applies automatic navigation after live run data changes.
    /// </summary>
    /// <param name="sender">The run state raising the event.</param>
    /// <param name="args">The run-state change event arguments.</param>
    private void HandleRunChanged(object? sender, EventArgs args)
    {
        TrackerRunStateSnapshot next = RunState.Snapshot;
        bool battleEnded = _run.Battle is not null && next.Battle is null;
        bool enemyAppeared = next.Enemies.Any(enemy => _run.Enemies.All(previous => previous.EnemyId != enemy.EnemyId));
        bool starterSelectionAppeared = _run.StarterSelection is null && next.StarterSelection is not null;
        _run = next;
        if (_selectedEnemyId is null || _run.Enemies.All(enemy => enemy.EnemyId != _selectedEnemyId))
            _selectedEnemyId = _run.Enemies.Count > 0 ? _run.Enemies[0].EnemyId : null;

        bool moveMenuOpened = _run.MoveMenuPokemonId is not null && _run.MoveMenuPokemonId != _lastMoveMenuPokemonId;
        if (enemyAppeared)
            _completedRunNavigationPending = false;

        if (starterSelectionAppeared)
        {
            _completedRunNavigationPending = false;
            _accessOpen = false;
            _settingsOpen = false;
            _selectedView = TrackerView.Player;
        }
        else if (_selectedView != TrackerView.Debug && !_completedRunNavigationPending)
        {
            _selectedView = (enemyAppeared, moveMenuOpened, battleEnded) switch
            {
                (true, _, _) => TrackerView.Enemy,
                (_, true, _) or (_, _, true) => TrackerView.Player,
                _ => _selectedView
            };
        }

        _lastMoveMenuPokemonId = _run.MoveMenuPokemonId;
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Navigates to Lookup when a completed run is stored or recovered.
    /// </summary>
    /// <param name="sender">The completed-run archive raising the event.</param>
    /// <param name="args">The change event arguments.</param>
    private void HandleCompletedRunSelectionRequested(object? sender, EventArgs args)
    {
        _completedRunNavigationPending = true;
        _selectedView = TrackerView.Lookup;
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Recomputes navigation immediately after activation, replacement, removal, or expiration.
    /// </summary>
    /// <param name="sender">The diagnostic-access service raising the event.</param>
    /// <param name="args">The empty change event arguments.</param>
    private void HandleDiagnosticAccessChanged(object? sender, EventArgs args)
    {
        if (_selectedView == TrackerView.Debug && !CanShowDebug)
            _selectedView = TrackerView.Player;

        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Removes tracker-state subscriptions when the page is disposed.
    /// </summary>
    public void Dispose()
    {
        ConnectionState.Changed -= HandleConnectionChanged;
        RunState.Changed -= HandleRunChanged;
        CompletedRuns.SelectionRequested -= HandleCompletedRunSelectionRequested;
        ShortcutService.ViewRequested -= HandleGlobalViewRequested;
        AccessService.Changed -= HandleDiagnosticAccessChanged;
    }
}
