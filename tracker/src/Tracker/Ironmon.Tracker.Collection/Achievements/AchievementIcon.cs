namespace Ironmon.Tracker.Collection.Achievements;

/// <summary>
/// Carries revealed artwork entirely in memory for the game and tracker.
/// </summary>
/// <remarks>
/// Initializes bounded RGBA8 pixels and a PNG data URL without creating image files.
/// </remarks>
/// <param name="Width">The pixel width, at most sixty-four.</param>
/// <param name="Height">The pixel height, at most sixty-four.</param>
/// <param name="Pixels">Base64-encoded, row-major RGBA8 pixels for the game bitmap.</param>
/// <param name="ImageDataUri">The in-memory PNG data URL used by the earned collection.</param>
public sealed record AchievementIcon(int Width, int Height, string Pixels, string ImageDataUri);
