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
    /// <param name="source">The fully materialized image source.</param>
    /// <param name="label">The accessible Pokemon label.</param>
    /// <param name="size">The requested square image size in pixels.</param>
    /// <param name="redesigned">Whether to use the migrated dialog presentation.</param>
    public void Open(string source, string label, int size, bool redesigned = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(size, 0);

        Current = new PokemonSpriteDialogRequest(source, label, size, redesigned);
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
/// Describes one enlarged sprite to render in the page-level dialog host.
/// </summary>
/// <remarks>
/// Initializes a request from a fully materialized image source, an accessible Pokemon label,
/// and the requested square image size in pixels.
/// </remarks>
/// <param name="source">The fully materialized image source.</param>
/// <param name="label">The accessible Pokemon label.</param>
/// <param name="size">The requested square image size in pixels.</param>
/// <param name="redesigned">Whether to use the migrated dialog presentation.</param>
internal sealed class PokemonSpriteDialogRequest(string source, string label, int size, bool redesigned = false)
{
    /// <summary>
    /// Gets the fully materialized image source.
    /// </summary>
    public string Source { get; } = source;

    /// <summary>
    /// Gets the accessible Pokemon label.
    /// </summary>
    public string Label { get; } = label;

    /// <summary>
    /// Gets the requested square image size in pixels.
    /// </summary>
    public int Size { get; } = size;

    /// <summary>
    /// Gets whether the originating view has migrated to the redesigned dialogs.
    /// </summary>
    public bool Redesigned { get; } = redesigned;
}
