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
                        throw new IOException(UpdaterText.UpdateProcessIdentityTheInstallationSGameOrTrackerIsRunningClose);
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
                    throw new IOException(UpdaterText.UpdateProcessIdentityCloseTheInfiniteFusionLauncherAndInstallerBeforeUpdating);
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }

        return Task.CompletedTask;
    }
}
