using System.Diagnostics;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Tracker.App;

/// <summary>
/// Adapts the shared update flow to the running tracker and its normal Windows application lifecycle.
/// </summary>
/// <remarks>
/// Constructs the lifecycle boundary without performing network work during dependency injection.
/// </remarks>
/// <param name="connection">The latest live run observation.</param>
/// <param name="lifecycle">The existing polling and sprite writer shutdown coordinator.</param>
/// <param name="navigation">The renderer-owned navigation capture.</param>
public sealed class TrackerUpdateHost(TrackerConnectionState connection, TrackerUpdateLifecycle lifecycle, TrackerUpdateNavigation navigation) : ITrackerUpdateHost
{
    private const string CoreRuntime = "coreclr.dll";
    private const string GameProcess = "InfiniteFusion2";
    private const string GameExecutable = GameProcess + ".exe";
    private readonly TrackerConnectionState _connection = connection;
    private readonly TrackerUpdateLifecycle _lifecycle = lifecycle;
    private readonly TrackerUpdateNavigation _navigation = navigation;
    private readonly Lazy<string> _baseline = new(() => HoennGameBaseline.Load().Commit);

    /// <summary>
    /// Observes the actual installed tracker, package flavor and game without requiring player-installed Git.
    /// </summary>
    /// <returns>The installation selection with unknown run state treated conservatively.</returns>
    public IronmonUpdateRequest Observe()
    {
        var executable = Environment.ProcessPath ?? throw new IOException("The running tracker executable could not be located.");
        var root = GameInstallationLocator.FindFromTracker(executable);
        if (!Path.GetFullPath(executable).Equals(Path.GetFullPath(Path.Combine(root, UpdaterHandoff.TrackerRelativePath)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Updates are available from the installed tracker. Open it from your game folder or usual shortcut.");

        var version = typeof(TrackerUpdateHost).Assembly.GetName().Version?.ToString(3) ?? TrackerApplicationConstants.DefaultVersion;
        var flavor = File.Exists(Path.Combine(Path.GetDirectoryName(executable)!, CoreRuntime)) ? ReleaseProtocol.SelfContained : ReleaseProtocol.RuntimeRequired;
        var snapshot = _connection.Snapshot;
        var active = snapshot.Status != TrackerConnectionStatus.Connected || snapshot.CurrentState is null || (snapshot.CurrentState.IronmonActive && snapshot.CurrentState.CompletedRun is null);
        return new IronmonUpdateRequest(root, version, flavor, flavor, GameCompatibilityCheck.ReadHead(root) ?? _baseline.Value, active, []);
    }

    /// <summary>
    /// Captures the origin and starts the independent window before the running tracker is asked to close.
    /// </summary>
    /// <param name="prepared">The verified transaction and helper identity.</param>
    /// <param name="recover">Whether the independent window performs recovery.</param>
    /// <param name="cancellationToken">The pre-shutdown token.</param>
    /// <returns>The acknowledged handoff.</returns>
    public async Task HandoffAsync(PreparedIronmonUpdate prepared, bool recover, CancellationToken cancellationToken)
    {
        var capture = _navigation.Capture ?? throw new InvalidOperationException("The tracker view is not ready to preserve its navigation. Try again after it finishes opening.");
        var context = await capture().ConfigureAwait(false);
        using var tracker = Process.GetCurrentProcess();
        var request = new UpdaterHandoffRequest(prepared.InstallationRoot, prepared.TransactionId, UpdateProcessIdentity.Capture(tracker), FindGame(prepared.InstallationRoot), context, recover, prepared.Elevation is null ? null : prepared);
        await UpdaterHandoff.LaunchAsync(request, prepared.HelperContent, token => _lifecycle.QuiesceAsync(prepared.InstallationRoot, token), () => ResumeAsync(prepared.InstallationRoot), CloseAsync, prepared.HelperVersion, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Captures only the game belonging to this installation, never an unrelated process with the same name.
    /// </summary>
    /// <param name="root">The installation being updated.</param>
    /// <returns>The single matching process, or null when the game is already closed.</returns>
    private static UpdateProcessIdentity? FindGame(string root)
    {
        UpdateProcessIdentity? result = null;
        var expected = Path.GetFullPath(Path.Combine(root, GameExecutable));
        foreach (var process in Process.GetProcessesByName(GameProcess))
        {
            using (process)
            {
                if (process.HasExited)
                    continue;

                var identity = UpdateProcessIdentity.Capture(process);
                if (!identity.ExecutablePath.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (result is not null)
                    throw new InvalidOperationException("More than one game window is open for this installation. Close the extra game window before updating.");

                result = identity;
            }
        }

        return result;
    }

    /// <summary>
    /// Restarts tracker listeners and sprite work if independent handoff fails before shutdown.
    /// </summary>
    /// <param name="root">The installation whose writers were paused.</param>
    /// <returns>The normal dispatcher-owned resumption.</returns>
    private Task ResumeAsync(string root)
        => MainThread.InvokeOnMainThreadAsync(() => _lifecycle.Resume(root));

    /// <summary>
    /// Requests normal application exit after the verified independent window acknowledges readiness.
    /// </summary>
    /// <returns>The dispatcher-owned shutdown request.</returns>
    private static Task CloseAsync()
        => MainThread.InvokeOnMainThreadAsync(() => Application.Current?.Quit());
}
