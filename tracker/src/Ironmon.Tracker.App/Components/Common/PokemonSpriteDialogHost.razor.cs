using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using System.Globalization;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Renders the application-wide enlarged-sprite dialog outside graph and card stacking contexts.
/// </summary>
public partial class PokemonSpriteDialogHost : IDisposable
{
    private const string _closeLocalizationKey = "PokemonSprite.Close";
    private ElementReference _dialogElement;
    private bool _focusPending;

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
    /// Moves keyboard focus into a newly opened dialog after it is rendered.
    /// </summary>
    /// <param name="firstRender">Whether this is the component's first completed render.</param>
    /// <returns>A task representing the focus operation.</returns>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_focusPending || Dialog.Current is null || Dialog.Current.Redesigned)
            return;

        _focusPending = false;
        await _dialogElement.FocusAsync(preventScroll: true);
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
        _focusPending = Dialog.Current is not null;
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Closes the active enlarged-sprite dialog.
    /// </summary>
    private void CloseDialog()
        => Dialog.Close();

    /// <summary>
    /// Closes the active enlarged-sprite dialog when Escape is pressed.
    /// </summary>
    /// <param name="args">The keyboard event.</param>
    private void HandleKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == TrackerKeyboardKeys.Escape)
            CloseDialog();
    }

    /// <summary>
    /// Gets an invariant inline size declaration.
    /// </summary>
    /// <param name="size">The requested square size in pixels.</param>
    /// <returns>The CSS size declaration.</returns>
    private static string GetSizeStyle(int size)
        => string.Create(CultureInfo.InvariantCulture, $"width: {size}px; height: {size}px");
}
