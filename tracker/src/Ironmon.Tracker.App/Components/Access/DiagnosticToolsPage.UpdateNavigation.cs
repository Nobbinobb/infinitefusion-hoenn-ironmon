using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Access;

/// <summary>
/// Preserves explicit navigation selections across an updater relaunch without changing the view's presentation.
/// </summary>
public partial class DiagnosticToolsPage
{
    private const string UpdateNavigationKey = "diagnostics";

    /// <summary>
    /// Gets the optional update navigation registry supplied by the normal tracker layout.
    /// </summary>
    [CascadingParameter]
    private TrackerUpdateNavigation? UpdateNavigation { get; set; }

    /// <summary>
    /// Registers the mounted selection and restores only validated navigation from an updater relaunch.
    /// </summary>
    private void InitializeUpdateNavigation()
    {
        UpdateNavigation?.Register(UpdateNavigationKey, () => new UpdateSelection(_toolsSelected));
        if (UpdateNavigation?.Take<UpdateSelection>(UpdateNavigationKey) is not { } saved)
            return;

        _toolsSelected = saved.Tools;
    }

    /// <summary>
    /// Stores navigation identities only; existing data and access checks remain authoritative after reconnection.
    /// </summary>
    /// <remarks>
    /// Constructs the bounded component resume selection.
    /// </remarks>
    /// <param name="Tools">The saved Tools navigation selection.</param>
    private sealed record UpdateSelection(bool Tools);
}
