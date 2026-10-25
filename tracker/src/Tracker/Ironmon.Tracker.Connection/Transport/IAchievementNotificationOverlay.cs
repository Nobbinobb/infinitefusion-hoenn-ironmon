namespace Ironmon.Tracker.Connection.Transport;

/// <summary>
/// Presents game-clocked achievement notifications at the actual desktop resolution.
/// </summary>
public interface IAchievementNotificationOverlay : IAsyncDisposable
{
    /// <summary>
    /// Prepares one authorized notification without advancing its animation.
    /// </summary>
    /// <param name="presentation">The authorized title and in-memory encrypted-pack images.</param>
    /// <param name="cancellationToken">Cancels preparation when the game disconnects or falls back.</param>
    /// <returns>The renderer's availability and current game visibility.</returns>
    Task<AchievementOverlayState> PrepareAsync(AchievementOverlayPresentation presentation, CancellationToken cancellationToken);

    /// <summary>
    /// Applies the game's animation position without running an independent notification clock.
    /// </summary>
    /// <param name="presentationId">The active presentation identity.</param>
    /// <param name="elapsedSeconds">Visible animation time in seconds.</param>
    /// <param name="cancellationToken">Cancels the bounded renderer update.</param>
    /// <returns>The renderer's availability and current game visibility.</returns>
    Task<AchievementOverlayState> UpdateAsync(string presentationId, double elapsedSeconds, CancellationToken cancellationToken);

    /// <summary>
    /// Removes one matching presentation without affecting a newer notification.
    /// </summary>
    /// <param name="presentationId">The presentation to remove.</param>
    void Stop(string presentationId);

    /// <summary>
    /// Hides and releases the current notification on disconnect or application shutdown.
    /// </summary>
    void Reset();
}

/// <summary>
/// Carries one authorized notification image pair without persistent extraction.
/// </summary>
/// <remarks>
/// Initializes a presentation owned by one game session.
/// </remarks>
/// <param name="PresentationId">The opaque game-generated presentation identity.</param>
/// <param name="GameRoot">The exact game installation whose window may be followed.</param>
/// <param name="Title">The approved achievement title.</param>
/// <param name="SmallPng">The approved small PNG bytes.</param>
/// <param name="DetailPng">The optional detail PNG bytes, or null for unfinished artwork.</param>
public sealed record AchievementOverlayPresentation(string PresentationId, string GameRoot, string Title, byte[] SmallPng, byte[]? DetailPng);

/// <summary>
/// Reports whether the renderer is healthy and whether the game currently owns focus.
/// </summary>
/// <remarks>
/// Initializes the state returned to the authoritative game clock.
/// </remarks>
/// <param name="Available">Whether the matching notification can still be rendered.</param>
/// <param name="Visible">Whether its game window currently owns focus.</param>
public sealed record AchievementOverlayState(bool Available, bool Visible);
