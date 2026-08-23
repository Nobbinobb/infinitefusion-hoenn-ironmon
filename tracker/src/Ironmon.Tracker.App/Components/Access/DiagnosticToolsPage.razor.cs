using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Access;

/// <summary>
/// Hosts always-available diagnostic access and capability-controlled diagnostic tools.
/// </summary>
public partial class DiagnosticToolsPage : IDisposable
{
    private bool _toolsSelected;

    /// <summary>
    /// Gets or initializes the active tracker connection service.
    /// </summary>
    [Inject]
    private TrackerConnectionService TrackerConnection { get; set; } = null!;

    /// <summary>
    /// Gets or initializes tracker-owned diagnostic access.
    /// </summary>
    [Inject]
    private DiagnosticAccessService AccessService { get; set; } = null!;

    /// <summary>
    /// Gets or initializes the shared connection status service.
    /// </summary>
    [Inject]
    private TrackerConnectionState ConnectionState { get; set; } = null!;

    /// <summary>
    /// Gets or sets the initialized player Pokemon when available.
    /// </summary>
    [Parameter]
    public PlayerPokemonSnapshot? Player { get; set; }

    /// <summary>
    /// Gets or sets the active enemy Pokemon choices.
    /// </summary>
    [Parameter]
    public IReadOnlyList<EnemyPokemonSnapshot> Enemies { get; set; } = [];

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Subscribes to authorization sources controlling tool availability.
    /// </summary>
    protected override void OnInitialized()
    {
        AccessService.Changed += HandleAvailabilityChanged;
        ConnectionState.Changed += HandleAvailabilityChanged;
    }

    /// <summary>
    /// Gets whether at least one diagnostic tool can be opened.
    /// </summary>
    private bool ToolsAvailable => TrackerConnection.DebugAuthorized || AccessService.Snapshot.IsActive;

    /// <summary>
    /// Shows diagnostic-access lifecycle controls.
    /// </summary>
    private void ShowAccess()
        => _toolsSelected = false;

    /// <summary>
    /// Shows authorized diagnostic tools.
    /// </summary>
    private void ShowTools()
    {
        if (ToolsAvailable)
            _toolsSelected = true;
    }

    /// <summary>
    /// Gets the visual classes for one diagnostic page tab.
    /// </summary>
    /// <param name="tools">Whether the tab represents diagnostic tools.</param>
    /// <returns>The tab CSS classes.</returns>
    private string GetTabClass(bool tools)
        => tools == _toolsSelected ? "diagnostic-tool-tab selected" : "diagnostic-tool-tab";

    /// <summary>
    /// Returns to access controls when authorization disappears and refreshes the host.
    /// </summary>
    /// <param name="sender">The changed authorization source.</param>
    /// <param name="args">The empty change arguments.</param>
    private void HandleAvailabilityChanged(object? sender, EventArgs args)
    {
        if (!ToolsAvailable)
            _toolsSelected = false;

        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Removes authorization-source subscriptions.
    /// </summary>
    public void Dispose()
    {
        AccessService.Changed -= HandleAvailabilityChanged;
        ConnectionState.Changed -= HandleAvailabilityChanged;
    }
}
