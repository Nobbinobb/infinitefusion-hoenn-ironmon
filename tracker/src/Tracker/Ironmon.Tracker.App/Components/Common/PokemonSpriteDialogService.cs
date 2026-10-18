namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Coordinates the single enlarged-sprite dialog rendered at the application layout level.
/// </summary>
internal sealed class PokemonSpriteDialogService
{
    /// <summary>
    /// Occurs after the active enlarged-sprite dialog changes.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Gets the active enlarged-sprite dialog request, or <see langword="null"/> when the dialog is closed.
    /// </summary>
    public PokemonSpriteDialogRequest? Current { get; private set; }

    /// <summary>
    /// Opens or replaces the enlarged-sprite dialog.
    /// </summary>
    /// <remarks>
    /// Adds the visual head and body only when their local metadata and both image sources are available.
    /// Otherwise, the viewer retains the complete sprite as its only image.
    /// </remarks>
    /// <param name="source">The fully materialized image source.</param>
    /// <param name="label">The complete sprite's Pokémon label and dialog heading.</param>
    /// <param name="size">The requested square image size in pixels.</param>
    /// <param name="gameRoot">The installation owning the component metadata, or <see langword="null"/> to omit component lookup.</param>
    /// <param name="spritePath">The displayed sprite's game-relative path, or <see langword="null"/> to omit component lookup.</param>
    /// <param name="selectedPart">The initially requested image role; the host shows the complete sprite when that role is unavailable.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="source"/> is empty or contains only white-space characters.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is zero or negative.</exception>
    public void Open(string source, string label, int size, string? gameRoot = null, string? spritePath = null, PokemonSpritePart selectedPart = PokemonSpritePart.Fusion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(size, 0);

        List<PokemonSpriteDialogImage> images = [new(source, label, PokemonSpritePart.Fusion)];
        if (LocalSpriteComponentsLoader.Load(gameRoot, spritePath) is { } components)
        {
            string? head = LocalSpriteLoader.Load(gameRoot, components.Head.SpritePath);
            string? body = LocalSpriteLoader.Load(gameRoot, components.Body.SpritePath);
            if (head is not null && body is not null)
            {
                images.Add(new(head, components.Head.Name, PokemonSpritePart.Head));
                images.Add(new(body, components.Body.Name, PokemonSpritePart.Body));
            }
        }

        Current = new PokemonSpriteDialogRequest(source, label, size, images, selectedPart);
        Changed?.Invoke();
    }

    /// <summary>
    /// Closes the enlarged-sprite dialog when one is active.
    /// </summary>
    public void Close()
    {
        if (Current is null)
            return;

        Current = null;
        Changed?.Invoke();
    }
}

/// <summary>
/// Describes the images, image size, and initial selection for the shared sprite dialog.
/// </summary>
/// <remarks>
/// Initializes a request with the complete sprite, any available visual components, and the initial image role.
/// Later image selection is owned by the dialog host and does not change this request.
/// </remarks>
/// <param name="source">The complete sprite's fully materialized image source.</param>
/// <param name="label">The complete sprite's Pokémon label and dialog heading.</param>
/// <param name="size">The square size in pixels used to enlarge any selected image.</param>
/// <param name="images">The complete sprite followed by its optional visual head and body images.</param>
/// <param name="selectedPart">The image role initially requested from the host.</param>
internal sealed class PokemonSpriteDialogRequest(string source, string label, int size, IReadOnlyList<PokemonSpriteDialogImage> images, PokemonSpritePart selectedPart)
{
    /// <summary>
    /// Gets the complete sprite's fully materialized image source.
    /// </summary>
    public string Source { get; } = source;

    /// <summary>
    /// Gets the complete sprite's Pokémon label and dialog heading.
    /// </summary>
    public string Label { get; } = label;

    /// <summary>
    /// Gets the square size in pixels used to enlarge the selected image.
    /// </summary>
    public int Size { get; } = size;

    /// <summary>
    /// Gets the complete sprite and any visual-component images selectable within this dialog.
    /// </summary>
    public IReadOnlyList<PokemonSpriteDialogImage> Images { get; } = images;

    /// <summary>
    /// Gets the image role initially requested from the host.
    /// </summary>
    public PokemonSpritePart SelectedPart { get; } = selectedPart;
}

/// <summary>
/// Identifies the complete Pokémon sprite or one of its visual fusion components.
/// </summary>
internal enum PokemonSpritePart
{
    /// <summary>
    /// Identifies the complete displayed sprite, including an ordinary Pokémon without fusion components.
    /// </summary>
    Fusion = 0,
    /// <summary>
    /// Identifies the normal Pokémon represented by the head.
    /// </summary>
    Head = 1,
    /// <summary>
    /// Identifies the normal Pokémon represented by the body.
    /// </summary>
    Body = 2
}

/// <summary>
/// Describes an image selected without opening an additional dialog.
/// </summary>
/// <remarks>
/// Initializes a materialized image, its Pokémon label, and its role in the shared viewer.
/// </remarks>
/// <param name="source">The fully materialized image data.</param>
/// <param name="label">The displayed Pokémon's name.</param>
/// <param name="part">The image's visual role.</param>
internal sealed class PokemonSpriteDialogImage(string source, string label, PokemonSpritePart part)
{
    /// <summary>
    /// Gets the fully materialized image source.
    /// </summary>
    public string Source { get; } = source;

    /// <summary>
    /// Gets the Pokémon label used by the selector, enlarged image, and caption.
    /// </summary>
    public string Label { get; } = label;

    /// <summary>
    /// Gets the complete-sprite or component role represented by this image.
    /// </summary>
    public PokemonSpritePart Part { get; } = part;
}
