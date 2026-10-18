using Ironmon.Updater.Infrastructure;

namespace Ironmon.Tracker.App;

/// <summary>
/// Starts a read-only installation check when the tracker opens, without altering navigation or views.
/// </summary>
public sealed class TrackerInstallationCompatibility
{
    /// <summary>
    /// Gets the background startup result for the later update interface.
    /// </summary>
    public Task<GameCompatibilityResult> Inspection { get; } = Task.Run(InspectAsync);

    /// <summary>
    /// Initializes the startup inspection when the application resolves this service.
    /// </summary>
    public TrackerInstallationCompatibility()
    {
    }

    /// <summary>
    /// Locates and revalidates the game using the same generated baseline as release packaging.
    /// </summary>
    /// <returns>A completed result even when running from an uninstalled development directory.</returns>
    private static async Task<GameCompatibilityResult> InspectAsync()
    {
        try
        {
            var root = GameInstallationLocator.FindFromTracker(Environment.ProcessPath ?? throw new IOException("The tracker executable path is unavailable."));
            return await GameCompatibilityCheck.InspectAsync(root, HoennGameBaseline.Load()).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(false, "An installed game could not be located for startup verification.");
        }
    }
}
