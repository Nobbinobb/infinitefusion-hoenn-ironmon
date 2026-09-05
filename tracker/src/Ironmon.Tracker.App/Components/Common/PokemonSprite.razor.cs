using Microsoft.AspNetCore.Components;
using System.Globalization;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Loads and presents one game sprite with a reusable enlarged-image dialog.
/// </summary>
public partial class PokemonSprite
{
    private const int _defaultSize = 80;
    private const int _defaultEnlargedSize = 288;
    private const string _enlargeLocalizationKey = "PokemonSprite.Enlarge";
    private const string _fittedSpriteClass = "pokemon-sprite-fitted";
    private const string _fitSpriteScript = "window.ironmonTrackerUi.fitSprite(this)";
    private string? _spriteKey;
    private string? _spriteSource;

    /// <summary>
    /// Gets the page-level dialog coordinator used to display enlarged sprites outside local stacking contexts.
    /// </summary>
    [Inject]
    private PokemonSpriteDialogService Dialog { get; set; } = null!;

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets or sets the game-relative sprite path.
    /// </summary>
    [Parameter]
    public string? SpritePath { get; set; }

    /// <summary>
    /// Gets or sets the Pokemon label shown in the enlarged-image dialog.
    /// </summary>
    [Parameter]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the displayed sprite size in pixels.
    /// </summary>
    [Parameter]
    public int Size { get; set; } = _defaultSize;

    /// <summary>
    /// Gets or sets the enlarged sprite size in pixels.
    /// </summary>
    [Parameter]
    public int EnlargedSize { get; set; } = _defaultEnlargedSize;

    /// <summary>
    /// Gets or sets additional presentation classes for the sprite button.
    /// </summary>
    [Parameter]
    public string? CssClass { get; set; }

    /// <summary>
    /// Gets or sets whether the enlarged sprite uses the redesigned dialog.
    /// </summary>
    [Parameter]
    public bool Redesigned { get; set; }

    /// <summary>
    /// Gets or sets whether the visible PNG pixels are centered and scaled to fit the sprite box with a small inset.
    /// Animated images retain full-frame containment so later frames cannot be clipped.
    /// </summary>
    [Parameter]
    public bool FitVisibleBounds { get; set; }

    /// <summary>
    /// Gets or sets whether the sprite opens its dialog, disabling interaction when embedded in another control.
    /// </summary>
    [Parameter]
    public bool Interactive { get; set; } = true;

    /// <summary>
    /// Refreshes the local sprite when its game installation or path changes.
    /// </summary>
    protected override void OnParametersSet()
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Size, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(EnlargedSize, 0);

        string? key = GameRoot is null || SpritePath is null ? null : $"{GameRoot}|{SpritePath}";
        if (key == _spriteKey)
            return;

        _spriteKey = key;
        _spriteSource = LocalSpriteLoader.Load(GameRoot, SpritePath);
    }

    /// <summary>
    /// Gets an invariant inline size declaration.
    /// </summary>
    /// <param name="size">The requested square size in pixels.</param>
    /// <returns>The CSS size declaration.</returns>
    private static string GetSizeStyle(int size)
        => string.Create(CultureInfo.InvariantCulture, $"width: {size}px; height: {size}px");

    /// <summary>
    /// Gets the localized action label for opening this sprite.
    /// </summary>
    /// <returns>The accessible action label.</returns>
    private string GetEnlargeLabel()
        => Text[_enlargeLocalizationKey, Label];

    /// <summary>
    /// Opens the enlarged-image dialog when a sprite is available.
    /// </summary>
    private void OpenDialog()
    {
        if (_spriteSource is not null)
            Dialog.Open(_spriteSource, Label, EnlargedSize, Redesigned);
    }
}
