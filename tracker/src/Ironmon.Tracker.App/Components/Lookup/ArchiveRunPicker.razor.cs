using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Selects archived runs and save-file history from the application header.
/// </summary>
public partial class ArchiveRunPicker
{
    private const string _escapeKey = "Escape";
    private ElementReference _trigger;
    private bool _open;
    private bool _restoreFocus;

    /// <summary>
    /// Gets or sets archived recipes in display order.
    /// </summary>
    [Parameter]
    public IReadOnlyList<CompletedRunRecipePayload> Recipes { get; set; } = [];

    /// <summary>
    /// Gets or sets the run retained while navigating the archive.
    /// </summary>
    [Parameter]
    public CompletedRunRecipePayload? SelectedRecipe { get; set; }

    /// <summary>
    /// Gets or sets whether history across save files is displayed.
    /// </summary>
    [Parameter]
    public bool HistorySelected { get; set; }

    /// <summary>
    /// Gets or sets the callback selecting a run.
    /// </summary>
    [Parameter]
    public EventCallback<string> RunSelected { get; set; }

    /// <summary>
    /// Gets or sets the callback opening save-file history.
    /// </summary>
    [Parameter]
    public EventCallback HistoryRequested { get; set; }

    /// <summary>
    /// Toggles the archive navigation popup.
    /// </summary>
    private void Toggle()
        => _open = !_open;

    /// <summary>
    /// Closes the archive navigation popup.
    /// </summary>
    private void Close()
    {
        _open = false;
        _restoreFocus = true;
    }

    /// <summary>
    /// Returns keyboard focus to the header control after its popup is dismissed.
    /// </summary>
    /// <param name="firstRender">Whether this is the initial render.</param>
    /// <returns>The focus restoration task.</returns>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_restoreFocus || _open)
            return;

        _restoreFocus = false;
        await _trigger.FocusAsync();
    }

    /// <summary>
    /// Dismisses the popup with the Escape key.
    /// </summary>
    /// <param name="args">The pressed key.</param>
    private void HandleKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == _escapeKey)
            Close();
    }

    /// <summary>
    /// Opens a run after dismissing navigation.
    /// </summary>
    /// <param name="runId">The selected archived run.</param>
    /// <returns>The selection callback task.</returns>
    private async Task SelectRunAsync(string runId)
    {
        Close();
        await RunSelected.InvokeAsync(runId);
    }

    /// <summary>
    /// Opens history after dismissing navigation.
    /// </summary>
    /// <returns>The history callback task.</returns>
    private async Task OpenHistoryAsync()
    {
        Close();
        await HistoryRequested.InvokeAsync();
    }
}
