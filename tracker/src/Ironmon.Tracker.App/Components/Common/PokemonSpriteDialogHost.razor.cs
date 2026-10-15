using Microsoft.AspNetCore.Components;
using System.Globalization;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Renders the application-wide enlarged-sprite dialog outside graph and card stacking contexts.
/// </summary>
public partial class PokemonSpriteDialogHost : IDisposable
{
    private const string _headResourceKey = "Common.BaseStats.Head";
    private const string _bodyResourceKey = "Common.BaseStats.Body";
    private const string _fusionResourceKey = "PokemonSprite.Fusion";
    private PokemonSpritePart _selectedPart;

    /// <summary>
    /// Gets the coordinator that owns the active enlarged-sprite request.
    /// </summary>
    [Inject]
    private PokemonSpriteDialogService Dialog { get; set; } = null!;

    /// <summary>
    /// Initializes the requested image selection and subscribes to shared sprite dialog changes.
    /// </summary>
    protected override void OnInitialized()
    {
        _selectedPart = Dialog.Current?.SelectedPart ?? PokemonSpritePart.Fusion;
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
    /// Resets image selection from the current request and schedules the host to render.
    /// </summary>
    private void HandleDialogChanged()
    {
        _selectedPart = Dialog.Current?.SelectedPart ?? PokemonSpritePart.Fusion;
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Selects another image without replacing or stacking the shared dialog.
    /// </summary>
    /// <param name="part">The visual side to enlarge.</param>
    private void SelectImage(PokemonSpritePart part)
    {
        if (Dialog.Current?.Images.Any(image => image.Part == part) == true)
            _selectedPart = part;
    }

    /// <summary>
    /// Gets the selected image or the complete sprite when component data is unavailable.
    /// </summary>
    /// <param name="request">The active shared request.</param>
    /// <returns>The image enlarged in this dialog.</returns>
    private PokemonSpriteDialogImage GetSelectedImage(PokemonSpriteDialogRequest request)
        => request.Images.FirstOrDefault(image => image.Part == _selectedPart) ?? request.Images[0];

    /// <summary>
    /// Gets the localized role label for an image in the shared viewer.
    /// </summary>
    /// <param name="part">The complete-sprite or visual-component role.</param>
    /// <returns>The localized fusion, head, or body label.</returns>
    private string GetPartLabel(PokemonSpritePart part)
        => Text[part switch { PokemonSpritePart.Head => _headResourceKey, PokemonSpritePart.Body => _bodyResourceKey, _ => _fusionResourceKey }];

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
