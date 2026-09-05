using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Presents pickup progress and category icons only for disclosed item identities.
/// </summary>
public partial class ObsidianLookupItem
{
    /// <summary>
    /// Gets or sets the pickup entry with its revealed identities.
    /// </summary>
    [Parameter, EditorRequired]
    public AreaItemEntryPayload Item { get; set; } = null!;
}
