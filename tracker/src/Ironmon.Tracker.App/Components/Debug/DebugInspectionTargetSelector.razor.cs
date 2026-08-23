using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Debug;

/// <summary>
/// Renders the authorized live Pokemon targets for Debug Inspector selection.
/// </summary>
public partial class DebugInspectionTargetSelector
{
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
    /// Gets or sets whether the player target is authorized.
    /// </summary>
    [Parameter]
    public bool PlayerAvailable { get; set; }

    /// <summary>
    /// Gets or sets whether enemy targets are authorized.
    /// </summary>
    [Parameter]
    public bool EnemiesAvailable { get; set; }

    /// <summary>
    /// Gets or sets the stable selected target value.
    /// </summary>
    [Parameter]
    public string SelectedTarget { get; set; } = DebugTargetIds.Player;

    /// <summary>
    /// Gets or sets whether target selection is disabled by an active request.
    /// </summary>
    [Parameter]
    public bool Loading { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked with a newly selected target value.
    /// </summary>
    [Parameter]
    public EventCallback<string> SelectedTargetChanged { get; set; }

    /// <summary>
    /// Gets whether at least one authorized target is currently available.
    /// </summary>
    private bool HasAvailableTarget => PlayerAvailable && Player is not null || EnemiesAvailable && Enemies.Count > 0;

    /// <summary>
    /// Forwards a changed select value as a stable target identifier.
    /// </summary>
    /// <param name="args">The select element change.</param>
    /// <returns>A task representing the callback.</returns>
    private Task HandleSelectedTargetChanged(ChangeEventArgs args)
        => SelectedTargetChanged.InvokeAsync(args.Value?.ToString() ?? DebugTargetIds.Player);
}
