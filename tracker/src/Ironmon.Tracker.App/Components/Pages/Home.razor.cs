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
    /// Subscribes the tracker shell to connection and run-state changes.
    /// </summary>
    protected override void OnInitialized()
    {
        _connection = ConnectionState.Snapshot;
        _run = RunState.Snapshot;
        _selectedEnemyId = _run.Enemies.Count > 0 ? _run.Enemies[0].EnemyId : null;
        _lastMoveMenuPokemonId = _run.MoveMenuPokemonId;
        if (_selectedEnemyId is not null)
            _selectedView = TrackerView.Enemy;

        ConnectionState.Changed += HandleConnectionChanged;
        RunState.Changed += HandleRunChanged;
        CompletedRuns.SelectionRequested += HandleCompletedRunSelectionRequested;
        ShortcutService.ViewRequested += HandleGlobalViewRequested;
    }

    /// <summary>
    /// Selects the requested primary tracker view.
    /// </summary>
    /// <param name="view">The view selected by the user.</param>
    private void SelectView(TrackerView view)
    {
        if (view == TrackerView.Debug && !TrackerConnection.DebugAuthorized)
            return;

        _completedRunNavigationPending = false;
        _selectedView = view;
    }

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
            TrackerKeyboardKeys.DebugNumber when TrackerConnection.DebugAuthorized => TrackerView.Debug,
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
        => TrackerConnection.DebugAuthorized ? TrackerUiConstants.DebugViewTabsCssClass : TrackerUiConstants.ViewTabsCssClass;

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
        if (_selectedView == TrackerView.Debug && !TrackerConnection.DebugAuthorized)
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
        _run = next;
        if (_selectedEnemyId is null || _run.Enemies.All(enemy => enemy.EnemyId != _selectedEnemyId))
            _selectedEnemyId = _run.Enemies.Count > 0 ? _run.Enemies[0].EnemyId : null;

        bool moveMenuOpened = _run.MoveMenuPokemonId is not null && _run.MoveMenuPokemonId != _lastMoveMenuPokemonId;
        if (_selectedView != TrackerView.Debug && !_completedRunNavigationPending)
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
    /// Removes tracker-state subscriptions when the page is disposed.
    /// </summary>
    public void Dispose()
    {
        ConnectionState.Changed -= HandleConnectionChanged;
        RunState.Changed -= HandleRunChanged;
        CompletedRuns.SelectionRequested -= HandleCompletedRunSelectionRequested;
        ShortcutService.ViewRequested -= HandleGlobalViewRequested;
    }
}
