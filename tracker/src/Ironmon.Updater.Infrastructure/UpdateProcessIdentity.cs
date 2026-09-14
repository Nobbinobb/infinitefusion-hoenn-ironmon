using Ironmon.Updater.Core;
using System.Diagnostics;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Identifies a process by executable and creation time in addition to its reusable operating-system PID.
/// </summary>
/// <remarks>
/// Constructs a process identity captured while its executable can be inspected.
/// </remarks>
/// <param name="Id">The operating-system PID.</param>
/// <param name="StartedUtcTicks">The exact process creation timestamp.</param>
/// <param name="ExecutablePath">The canonical executable path.</param>
public sealed record UpdateProcessIdentity(int Id, long StartedUtcTicks, string ExecutablePath)
{
    private const string Launchers = "PokemonInfiniteFusion-Launcher|PokemonInfiniteFusion-Installer";

    /// <summary>
    /// Captures executable path and creation time while confirming the process has not exited.
    /// </summary>
    /// <param name="process">The process owned or inspected by the caller.</param>
    /// <returns>The captured identity.</returns>
    public static UpdateProcessIdentity Capture(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        var identity = new UpdateProcessIdentity(process.Id, process.StartTime.ToUniversalTime().Ticks, PlainPaths.Full(process.MainModule?.FileName ?? throw new IOException(UpdaterText.UpdateProcessIdentityTheProcessExecutableCouldNotBeInspected)));
        if (process.HasExited)
            throw new InvalidOperationException(UpdaterText.UpdateProcessIdentityTheProcessExitedDuringIdentityInspection);

        return identity;
    }

    /// <summary>
    /// Reopens only the original process; a reused PID is treated as the original process having exited.
    /// </summary>
    /// <returns>An owned handle to the matching process, or null after exit or PID reuse.</returns>
    public Process? Open()
    {
        Process process;
        try
        {
            process = Process.GetProcessById(Id);
        }
        catch (ArgumentException)
        {
            return null;
        }

        try
        {
            if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != StartedUtcTicks)
            {
                process.Dispose();
                return null;
            }

            if (!string.Equals(PlainPaths.Full(process.MainModule?.FileName ?? throw new IOException(UpdaterText.UpdateProcessIdentityTheProcessExecutableCouldNotBeInspected)), ExecutablePath, StringComparison.OrdinalIgnoreCase))
                throw new IOException(UpdaterText.UpdateProcessIdentityTheProcessPathDoesNotMatchTheShutdownHandoff);

            return process;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Requests normal closure when allowed and waits for actual exit without ever force-terminating the process.
    /// </summary>
    /// <param name="requestClose">Whether to send a normal window-close request.</param>
    /// <param name="waiting">An optional explanation that manual closure is still needed.</param>
    /// <param name="cancellationToken">Cancels waiting before installation mutations.</param>
    /// <returns>A task that completes only when this process identity has exited.</returns>
    public async Task WaitForExitAsync(bool requestClose, Action<string>? waiting = null, CancellationToken cancellationToken = default)
    {
        using var process = Open();
        if (process is null)
            return;

        try
        {
            if (requestClose)
                process.CloseMainWindow();

            if (!process.HasExited)
                waiting?.Invoke(UpdaterText.UpdateProcessIdentityWaitingForTheGameOrTrackerToCloseSave);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
        }
    }

    /// <summary>
    /// Blocks writes if any process currently executes the installation's game or tracker binary.
    /// </summary>
    /// <param name="installationRoot">The checked installation.</param>
    /// <param name="trackerRelativePath">The fixed tracker package executable path.</param>
    /// <param name="cancellationToken">The inspection token.</param>
    /// <returns>A completed idle check or an explicit running/inaccessible process blocker.</returns>
    public static Task EnsureInstallationIdleAsync(string installationRoot, string trackerRelativePath, CancellationToken cancellationToken = default)
    {
        if (FindInstallationBlocker(installationRoot, trackerRelativePath, cancellationToken) is { } blocker)
            throw new IOException(blocker);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Retains prepared files while waiting for the selected installation and external launchers to become idle.
    /// </summary>
    /// <param name="installationRoot">The selected installation.</param>
    /// <param name="trackerRelativePath">The fixed tracker executable path.</param>
    /// <param name="cancellationToken">The token that cancels waiting without terminating applications.</param>
    /// <returns>A task completing when installation can continue automatically.</returns>
    public static Task WaitForInstallationIdleAsync(string installationRoot, string trackerRelativePath, CancellationToken cancellationToken = default)
        => WaitForInstallationIdleAsync(token => FindInstallationBlocker(installationRoot, trackerRelativePath, token), cancellationToken);

    /// <summary>
    /// Waits on an independently supplied process probe while keeping other inspection failures visible.
    /// </summary>
    /// <param name="findBlocker">The process probe returning a running-application message or null.</param>
    /// <param name="cancellationToken">The cancellable wait token.</param>
    /// <returns>A task completing after the blocker disappears.</returns>
    internal static async Task WaitForInstallationIdleAsync(Func<CancellationToken, string?> findBlocker, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (findBlocker(cancellationToken) is null)
                return;

            InstallationProgressScope.Report(new(InstallationStage.WaitingForApplications));
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Identifies running game, tracker and launcher processes without closing them or changing installation files.
    /// </summary>
    /// <param name="installationRoot">The selected installation.</param>
    /// <param name="trackerRelativePath">The fixed tracker executable path.</param>
    /// <param name="cancellationToken">The process inspection token.</param>
    /// <returns>The localized blocker, or null when no relevant application is running.</returns>
    private static string? FindInstallationBlocker(string installationRoot, string trackerRelativePath, CancellationToken cancellationToken)
    {
        var paths = new[] { PlainPaths.Child(installationRoot, GameInstallationLocator.GameExecutable), PlainPaths.Child(installationRoot, trackerRelativePath) };
        foreach (var path in paths)
        {
            var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path));
            try
            {
                foreach (var process in processes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!process.HasExited && string.Equals(Capture(process).ExecutablePath, path, StringComparison.OrdinalIgnoreCase))
                        return UpdaterText.UpdateProcessIdentityTheInstallationSGameOrTrackerIsRunningClose;
                }
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }

        foreach (var name in Launchers.Split('|'))
        {
            var processes = Process.GetProcessesByName(name);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (processes.Any(process => !process.HasExited))
                    return UpdaterText.UpdateProcessIdentityCloseTheInfiniteFusionLauncherAndInstallerBeforeUpdating;
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }

        return null;
    }
}
