using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Tracker.App;

/// <summary>
/// Starts the selected installation's game at tracker startup when that game is not already running.
/// </summary>
public sealed class TrackerGameLauncher
{
    internal const string ErrorTitleKey = "Startup.GameLaunch.ErrorTitle";
    internal const string CloseKey = "Startup.GameLaunch.Close";
    internal const string FailedKey = "Startup.GameLaunch.Failed";
    internal const string InstallationNotReadyKey = "Startup.GameLaunch.InstallationNotReady";
    private const string GameExecutable = "InfiniteFusion2.exe";
    private const string PerformanceExecutable = "InfiniteFusion2-performance.exe";
    private const string MutexPrefix = "Local\\Ironmon.Tracker.GameLaunch.";
    private static readonly TimeSpan _launchLockTimeout = TimeSpan.FromSeconds(5);
    private readonly Lock _gate = new();
    private readonly Func<string> _findRoot;
    private readonly Func<string, bool> _isRunning;
    private readonly Action<string, CancellationToken> _launch;
    private readonly Func<Task<GameCompatibilityResult>> _inspect;
    private Task<string?>? _startup;

    /// <summary>
    /// Connects game startup to the existing read-only installation check.
    /// </summary>
    /// <param name="compatibility">The startup installation check already shared with the updater.</param>
    public TrackerGameLauncher(TrackerInstallationCompatibility compatibility) : this(FindGameRoot, IsGameRunning, LaunchGame, () => compatibility.Inspection)
    {
        ArgumentNullException.ThrowIfNull(compatibility);
    }

    /// <summary>
    /// Initializes game startup with independently supplied installation and process operations.
    /// </summary>
    /// <param name="findRoot">Locates the installation belonging to this tracker.</param>
    /// <param name="isRunning">Checks for an existing game in that installation.</param>
    /// <param name="launch">Starts the game after a final process check.</param>
    /// <param name="inspect">Reuses the startup compatibility inspection.</param>
    internal TrackerGameLauncher(Func<string> findRoot, Func<string, bool> isRunning, Action<string, CancellationToken> launch, Func<Task<GameCompatibilityResult>> inspect)
    {
        _findRoot = findRoot;
        _isRunning = isRunning;
        _launch = launch;
        _inspect = inspect;
    }

    /// <summary>
    /// Performs at most one startup attempt without blocking the tracker window or retrying on reconnect.
    /// </summary>
    /// <param name="cancellationToken">Cancels startup when the tracker closes.</param>
    /// <returns>A task containing a localized error key, or null when the game is already running, launch succeeds, or startup is canceled.</returns>
    public Task<string?> StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return _startup ??= Task.Run(() => LaunchAsync(cancellationToken));
    }

    /// <summary>
    /// Starts the installed game and rechecks for a game opened during installation verification.
    /// </summary>
    /// <param name="cancellationToken">Cancels the pending startup attempt.</param>
    /// <returns>A task containing a localized error key, or null when launch succeeds, the game is already running, or startup is canceled.</returns>
    private async Task<string?> LaunchAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string root = _findRoot();
            if (_isRunning(root))
                return null;

            GameCompatibilityResult compatibility = await _inspect().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!compatibility.Compatible)
                return InstallationNotReadyKey;

            cancellationToken.ThrowIfCancellationRequested();
            if (!_isRunning(root))
                _launch(root, cancellationToken);

            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or Win32Exception)
        {
            return FailedKey;
        }
    }

    /// <summary>
    /// Resolves the game from the tracker executable rather than the shortcut's working directory.
    /// </summary>
    /// <returns>The nearest validated installation directory.</returns>
    private static string FindGameRoot()
        => GameInstallationLocator.FindFromTracker(Environment.ProcessPath ?? throw new IOException("The tracker executable path is unavailable."));

    /// <summary>
    /// Detects both normal and performance game executables belonging to this exact installation.
    /// </summary>
    /// <param name="root">The selected game installation.</param>
    /// <returns>True when its game is already running.</returns>
    internal static bool IsGameRunning(string root)
    {
        foreach (string executable in new[] { GameExecutable, PerformanceExecutable })
        {
            string expected = Path.GetFullPath(Path.Combine(root, executable));
            Process[] processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable));
            try
            {
                foreach (Process process in processes)
                {
                    try
                    {
                        if (!process.HasExited && UpdateProcessIdentity.Capture(process).ExecutablePath.Equals(expected, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    catch (InvalidOperationException) when (process.HasExited)
                    {
                    }
                }
            }
            finally
            {
                foreach (Process process in processes)
                    process.Dispose();
            }
        }

        return false;
    }

    /// <summary>
    /// Serializes concurrent tracker launches for one installation before checking and starting its game.
    /// </summary>
    /// <param name="root">The verified installation directory.</param>
    /// <param name="cancellationToken">Cancels launch if the tracker closes while another instance starts the game.</param>
    private static void LaunchGame(string root, CancellationToken cancellationToken)
    {
        string identity = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        string name = MutexPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        using Mutex mutex = new(false, name);
        bool acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(_launchLockTimeout);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
                throw new IOException("Another tracker is still starting the game.");

            cancellationToken.ThrowIfCancellationRequested();
            if (IsGameRunning(root))
                return;

            using Process process = Process.Start(CreateStartInfo(root)) ?? throw new IOException("The game process could not be started.");
        }
        finally
        {
            if (acquired)
                mutex.ReleaseMutex();
        }
    }

    /// <summary>
    /// Creates the installed game launch configuration with its own directory as the working directory.
    /// </summary>
    /// <param name="root">The verified game installation.</param>
    /// <returns>The fixed game launch configuration without shell commands or arguments.</returns>
    internal static ProcessStartInfo CreateStartInfo(string root)
        => new(Path.Combine(root, GameExecutable)) { WorkingDirectory = root, UseShellExecute = true };
}
