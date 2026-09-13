using Ironmon.Tracker.Connection.Sprites;

namespace Ironmon.Tracker.App;

/// <summary>
/// Coordinates the existing tracker services for acknowledged updater handoff without changing tracker views.
/// </summary>
/// <remarks>
/// Constructs the lifecycle adapter used by the later tracker update orchestration.
/// </remarks>
/// <param name="connection">The existing polling and listener service.</param>
/// <param name="shortcuts">The existing shortcut service.</param>
/// <param name="sprites">The existing installation sprite writer.</param>
public sealed class TrackerUpdateLifecycle(TrackerConnectionService connection, TrackerGlobalShortcutService shortcuts, CustomSpriteSheetInstaller sprites)
{
    /// <summary>
    /// Drains sprite writes before stopping tracker services, leaving application closure to the acknowledged handoff.
    /// </summary>
    /// <param name="gameRoot">The installation being updated.</param>
    /// <param name="cancellationToken">The bounded preparation token.</param>
    /// <returns>A task completing only after owned background work has stopped.</returns>
    public async Task QuiesceAsync(string gameRoot, CancellationToken cancellationToken = default)
    {
        await sprites.PauseForUpdateAsync(gameRoot, cancellationToken);
        await shortcuts.StopAsync();
        await connection.StopAsync();
    }

    /// <summary>
    /// Restores normal tracker activity after a failed handoff while preserving the existing navigation view.
    /// </summary>
    /// <param name="gameRoot">The installation whose update was cancelled.</param>
    public void Resume(string gameRoot)
    {
        connection.Start();
        shortcuts.Start();
        sprites.ResumeAfterUpdate(gameRoot);
    }
}
