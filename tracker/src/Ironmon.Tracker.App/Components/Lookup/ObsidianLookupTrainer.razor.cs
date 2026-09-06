using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Presents trainer progress and disclosed party information in the redesigned lookup.
/// </summary>
public partial class ObsidianLookupTrainer
{
    /// <summary>
    /// Gets or sets the trainer entry with its disclosure state.
    /// </summary>
    [Parameter, EditorRequired]
    public AreaTrainerEntryPayload Trainer { get; set; } = null!;

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }
}
