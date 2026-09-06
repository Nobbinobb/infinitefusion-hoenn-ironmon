using Microsoft.AspNetCore.Components;
using System.Globalization;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Renders the application-wide enlarged-sprite dialog outside graph and card stacking contexts.
/// </summary>
public partial class PokemonSpriteDialogHost : IDisposable
{
    /// <summary>
    /// Gets the coordinator that owns the active enlarged-sprite request.
    /// </summary>
    [Inject]
    private PokemonSpriteDialogService Dialog { get; set; } = null!;

    /// <summary>
    /// Subscribes the layout-level host to enlarged-sprite dialog changes.
    /// </summary>
    protected override void OnInitialized()
    {
        Dialog.Changed += HandleDialogChanged;
    }

    /// <summary>
    /// Unsubscribes the layout-level host from dialog changes.
    /// </summary>
    public void Dispose()
    {
        Dialog.Changed -= HandleDialogChanged;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Schedules a render and focus update after the active dialog changes.
    /// </summary>
    private void HandleDialogChanged()
    {
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Closes the active enlarged-sprite dialog.
    /// </summary>
    private void CloseDialog()
        => Dialog.Close();

    /// <summary>
    /// Gets an invariant inline size declaration.
    /// </summary>
    /// <param name="size">The requested square size in pixels.</param>
    /// <returns>The CSS size declaration.</returns>
    private static string GetSizeStyle(int size)
        => string.Create(CultureInfo.InvariantCulture, $"width: {size}px; height: {size}px");
}
