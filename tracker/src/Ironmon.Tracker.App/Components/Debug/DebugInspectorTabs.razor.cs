using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Debug;

/// <summary>
/// Renders the authorized primary Debug Inspector page navigation.
/// </summary>
public partial class DebugInspectorTabs
{
    /// <summary>
    /// Gets or sets the authorized pages in display order.
    /// </summary>
    [Parameter]
    public IReadOnlyList<DebugInspectorPage> Pages { get; set; } = [];

    /// <summary>
    /// Gets or sets the selected page.
    /// </summary>
    [Parameter]
    public DebugInspectorPage SelectedPage { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when a page is selected.
    /// </summary>
    [Parameter]
    public EventCallback<DebugInspectorPage> SelectedPageChanged { get; set; }

    /// <summary>
    /// Gets the CSS class for one page button.
    /// </summary>
    /// <param name="page">The represented page.</param>
    /// <returns>The page button CSS classes.</returns>
    private string GetPageClass(DebugInspectorPage page)
        => page == SelectedPage ? TrackerUiConstants.SelectedCssClass : string.Empty;

    /// <summary>
    /// Gets the localized label for one page.
    /// </summary>
    /// <param name="page">The represented page.</param>
    /// <returns>The localized page label.</returns>
    private string GetPageText(DebugInspectorPage page) => page switch
    {
        DebugInspectorPage.Pokemon => Text["Debug.Inspector.Pokemon"],
        DebugInspectorPage.Lookup => Text["Debug.Inspector.Lookup"],
        DebugInspectorPage.Diagnostics => Text["Debug.Inspector.RunDiagnostics"],
        DebugInspectorPage.Protocol => Text["Debug.Inspector.Protocol"],
        _ => page.ToString()
    };
}
