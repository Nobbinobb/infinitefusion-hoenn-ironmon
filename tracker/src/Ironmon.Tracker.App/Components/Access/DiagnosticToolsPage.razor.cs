using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Access;

/// <summary>
/// Hosts always-available diagnostic access and capability-controlled diagnostic tools.
/// </summary>
public partial class DiagnosticToolsPage
{
    private bool _toolsSelected;

    /// <summary>
    /// Gets or sets whether at least one diagnostic tool can be opened.
    /// </summary>
    [Parameter]
    public bool ToolsAvailable { get; set; }

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
    /// Returns to the access page if authorization disappears.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (!ToolsAvailable)
            _toolsSelected = false;
    }

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
}
