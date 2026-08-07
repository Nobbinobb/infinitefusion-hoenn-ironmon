using Ironmon.Tracker.App.Components;
using Ironmon.Tracker.Connection;
using Ironmon.Tracker.Protocol;
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
    }

    /// <summary>
    /// Selects the requested primary tracker view.
    /// </summary>
    /// <param name="view">The view selected by the user.</param>
    private void SelectView(TrackerView view) => _selectedView = view;

    /// <summary>
    /// Handles keyboard shortcuts for switching between primary tracker views.
    /// </summary>
    /// <param name="args">The browser keyboard event.</param>
    private void HandleKeyDown(KeyboardEventArgs args)
    {
        TrackerView? requestedView = args.Key.ToUpperInvariant() switch
        {
            "P" or "1" => TrackerView.Player,
            "E" or "2" => TrackerView.Enemy,
            "L" or "3" => TrackerView.Lookup,
            _ => null
        };

        if (requestedView is not null)
            SelectView(requestedView.Value);
    }

    /// <summary>
    /// Gets the CSS class for a primary tracker tab.
    /// </summary>
    /// <param name="view">The tab's tracker view.</param>
    /// <returns>The tab CSS classes.</returns>
    private string GetTabClass(TrackerView view) => view == _selectedView ? "view-tab selected" : "view-tab";

    /// <summary>
    /// Gets the concise connection state shown in the tracker header.
    /// </summary>
    /// <returns>The connection state label.</returns>
    private string GetConnectionText() => _connection.Status switch
    {
        TrackerConnectionStatus.Waiting => "Waiting for game",
        TrackerConnectionStatus.Handshaking => "Connecting",
        TrackerConnectionStatus.Connected => "Game connected",
        TrackerConnectionStatus.Error => "Connection error",
        TrackerConnectionStatus.Stopped => "Tracker stopped",
        _ => "Unknown state"
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
            return $"Infinite Fusion {_connection.Game.GameVersion} · Ironmon {_connection.Game.IronmonVersion}";

        return "Listening on 127.0.0.1:38521";
    }

    /// <summary>
    /// Gets the CSS classes for the connection state indicator.
    /// </summary>
    /// <returns>The connection indicator CSS classes.</returns>
    private string GetConnectionDotClass() => _connection.Status switch
    {
        TrackerConnectionStatus.Connected => "connection-dot connected",
        TrackerConnectionStatus.Error => "connection-dot error",
        TrackerConnectionStatus.Handshaking => "connection-dot handshaking",
        _ => "connection-dot"
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
        _selectedView = (enemyAppeared, moveMenuOpened, battleEnded) switch
        {
            (true, _, _) => TrackerView.Enemy,
            (_, true, _) or (_, _, true) => TrackerView.Player,
            _ => _selectedView
        };

        _lastMoveMenuPokemonId = _run.MoveMenuPokemonId;
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Removes tracker-state subscriptions when the page is disposed.
    /// </summary>
    public void Dispose()
    {
        ConnectionState.Changed -= HandleConnectionChanged;
        RunState.Changed -= HandleRunChanged;
    }
}
