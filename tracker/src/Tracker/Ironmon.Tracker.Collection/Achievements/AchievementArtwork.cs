namespace Ironmon.Tracker.Collection.Achievements;

/// <summary>
/// Carries one earned detail image in memory without adding artwork to collection storage.
/// </summary>
/// <remarks>
/// Initializes the uniform detail dimensions and a PNG data URL for the tracker viewer.
/// </remarks>
/// <param name="Width">The detail canvas width in pixels.</param>
/// <param name="Height">The detail canvas height in pixels.</param>
/// <param name="ImageDataUri">The authenticated in-memory PNG data URL.</param>
public sealed record AchievementArtwork(int Width, int Height, string ImageDataUri);
