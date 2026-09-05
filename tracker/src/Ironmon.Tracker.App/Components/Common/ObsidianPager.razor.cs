using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Presents shared paging controls without owning the request or selection state.
/// </summary>
public partial class ObsidianPager
{
    private const string _rangeKey = "Lookup.Search.ResultRange";

    /// <summary>
    /// Gets or sets the parent-owned pagination.
    /// </summary>
    [Parameter, EditorRequired]
    public PaginationState State { get; set; } = null!;

    /// <summary>
    /// Gets or sets the total number of entries.
    /// </summary>
    [Parameter]
    public int Total { get; set; }

    /// <summary>
    /// Gets or sets the accessible name of these paging controls.
    /// </summary>
    [Parameter]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether an in-flight request prevents paging.
    /// </summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>
    /// Gets or sets the previous-page action.
    /// </summary>
    [Parameter]
    public EventCallback Previous { get; set; }

    /// <summary>
    /// Gets or sets the next-page action.
    /// </summary>
    [Parameter]
    public EventCallback Next { get; set; }

    /// <summary>
    /// Formats the current inclusive entry range.
    /// </summary>
    /// <returns>The localized range and total.</returns>
    private string GetRange()
    {
        (int first, int last) = State.GetRange(Total);
        return Text[_rangeKey, first, last, Total];
    }
}
