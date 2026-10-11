using Ironmon.Updater.Infrastructure;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace Ironmon.Tracker.App.Tests.Settings;

/// <summary>
/// Exercises opt-in startup, installation readiness, process identity, and cancellation without opening a player game.
/// </summary>
[Collection(nameof(SpriteSettingsCollection))]
public sealed class TrackerGameLauncherTests
{
    private const string GameExecutable = "InfiniteFusion2.exe";
    private const string PerformanceExecutable = "InfiniteFusion2-performance.exe";
    private const string SystemCommandExecutable = "cmd.exe";
    private const string TestDirectory = "IronmonGameLauncherTests";
    private const string LocateBoundary = "locate";
    private const string ProbeBoundary = "probe";
    private const string InspectBoundary = "inspect";
    private const string LaunchBoundary = "launch";

    /// <summary>
    /// Verifies missing preferences preserve separate startup and that changes survive recreating the settings access.
    /// </summary>
    [Fact]
    public void GameStartupIsOffByDefaultAndPersistsBothChoices()
    {
        FieldInfo implementation = typeof(Preferences).GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(field => typeof(IPreferences).IsAssignableFrom(field.FieldType));
        object? original = implementation.GetValue(null);
        implementation.SetValue(null, new StartupPreferences());
        try
        {
            Assert.False(TrackerStartupSettings.AutoStartGame);
            TrackerStartupSettings.AutoStartGame = true;
            Assert.True(TrackerStartupSettings.AutoStartGame);
            TrackerStartupSettings.AutoStartGame = false;
            Assert.False(TrackerStartupSettings.AutoStartGame);
        }
        finally
        {
            implementation.SetValue(null, original);
        }
    }

    /// <summary>
    /// Verifies disabled startup performs no installation or process work.
    /// </summary>
    [Fact]
    public async Task DisabledStartupDoesNotLocateInspectOrLaunchTheGame()
    {
        TrackerGameLauncher launcher = new(() => false, () => throw new InvalidOperationException(), _ => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException(), () => throw new InvalidOperationException());
        Assert.Null(await launcher.StartAsync());
    }

    /// <summary>
    /// Verifies concurrent startup callbacks launch only once and use the game directory rather than the shortcut directory.
    /// </summary>
    [Fact]
    public async Task EnabledStartupLaunchesOnlyOnceWithTheInstalledGameDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), TestDirectory, Guid.NewGuid().ToString());
        int launches = 0;
        ProcessStartInfo? start = null;
        TrackerGameLauncher launcher = new(() => true, () => root, _ => false, (path, _) =>
        {
            Interlocked.Increment(ref launches);
            start = TrackerGameLauncher.CreateStartInfo(path);
        }, () => Task.FromResult(new GameCompatibilityResult(true, string.Empty)));

        string?[] results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => launcher.StartAsync()));
        Assert.All(results, Assert.Null);
        Assert.Equal(1, launches);
        Assert.NotNull(start);
        Assert.Equal(Path.Combine(root, GameExecutable), start.FileName);
        Assert.Equal(root, start.WorkingDirectory);
        Assert.True(start.UseShellExecute);
        Assert.Empty(start.ArgumentList);
    }

    /// <summary>
    /// Verifies an already running game remains untouched without waiting for installation inspection.
    /// </summary>
    [Fact]
    public async Task ExistingGameSkipsInspectionAndLaunch()
    {
        TrackerGameLauncher launcher = new(() => true, () => TestDirectory, _ => true, (_, _) => throw new InvalidOperationException(), () => throw new InvalidOperationException());
        Assert.Null(await launcher.StartAsync());
    }

    /// <summary>
    /// Verifies a manually opened game during compatibility checking prevents a second launch.
    /// </summary>
    [Fact]
    public async Task GameOpenedWhileInspectionRunsIsNotStartedAgain()
    {
        TaskCompletionSource<GameCompatibilityResult> inspection = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool running = false;
        TrackerGameLauncher launcher = new(() => true, () => TestDirectory, _ => running, (_, _) => throw new InvalidOperationException(), () =>
        {
            entered.SetResult();
            return inspection.Task;
        });

        Task<string?> startup = launcher.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(startup.IsCompleted);
        running = true;
        inspection.SetResult(new(true, string.Empty));
        Assert.Null(await startup);
    }

    /// <summary>
    /// Verifies pending recovery or unsupported installed files prevent automatic game startup.
    /// </summary>
    [Fact]
    public async Task InstallationRequiringRecoveryOrUpdateDoesNotLaunch()
    {
        TrackerGameLauncher launcher = new(() => true, () => TestDirectory, _ => false, (_, _) => throw new InvalidOperationException(), () => Task.FromResult(new GameCompatibilityResult(false, string.Empty)));
        Assert.Equal(TrackerGameLauncher.InstallationNotReadyKey, await launcher.StartAsync());
    }

    /// <summary>
    /// Verifies expected installation, access, verification, and process failures become a recoverable startup message.
    /// </summary>
    /// <param name="boundary">The failing startup boundary.</param>
    [Theory]
    [InlineData(LocateBoundary)]
    [InlineData(ProbeBoundary)]
    [InlineData(InspectBoundary)]
    [InlineData(LaunchBoundary)]
    public async Task StartupFailuresLeaveTheTrackerUsable(string boundary)
    {
        string locate() => boundary == LocateBoundary ? throw new DirectoryNotFoundException() : TestDirectory;
        bool probe(string _) => boundary == ProbeBoundary ? throw new Win32Exception() : false;
        void launch(string _1, CancellationToken _2)
        {
            if (boundary == LaunchBoundary)
                throw new Win32Exception();
        }

        Task<GameCompatibilityResult> inspect() => boundary == InspectBoundary ? Task.FromException<GameCompatibilityResult>(new IOException()) : Task.FromResult(new GameCompatibilityResult(true, string.Empty));
        TrackerGameLauncher launcher = new(() => true, locate, probe, launch, inspect);
        Assert.Equal(TrackerGameLauncher.FailedKey, await launcher.StartAsync());
    }

    /// <summary>
    /// Verifies closing the tracker during inspection never opens the game later.
    /// </summary>
    [Fact]
    public async Task ClosingTrackerCancelsPendingGameStartup()
    {
        TaskCompletionSource<GameCompatibilityResult> inspection = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cancellation = new();
        TrackerGameLauncher launcher = new(() => true, () => TestDirectory, _ => false, (_, _) => throw new InvalidOperationException(), () =>
        {
            entered.SetResult();
            return inspection.Task;
        });

        Task<string?> startup = launcher.StartAsync(cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        Assert.Null(await startup.WaitAsync(TimeSpan.FromSeconds(5)));
        inspection.SetResult(new(true, string.Empty));
    }

    /// <summary>
    /// Verifies real Windows process matching distinguishes both executable variants and unrelated installations.
    /// </summary>
    /// <param name="executable">The game executable variant to simulate with an owned inert process.</param>
    [Theory]
    [InlineData(GameExecutable)]
    [InlineData(PerformanceExecutable)]
    public async Task RunningGameDetectionUsesTheExactInstallation(string executable)
    {
        string root = Path.Combine(Path.GetTempPath(), TestDirectory, Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, executable);
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), SystemCommandExecutable), path);
        ProcessStartInfo start = new(path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
        using Process process = Process.Start(start)!;
        try
        {
            Assert.True(TrackerGameLauncher.IsGameRunning(root));
            Assert.False(TrackerGameLauncher.IsGameRunning(Path.Combine(root, TestDirectory)));
        }
        finally
        {
            process.StandardInput.Close();
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill();
                await process.WaitForExitAsync();
            }

            process.Dispose();
            await DeleteOwnedFixtureAsync(root);
        }
    }

    /// <summary>
    /// Removes only this test's temporary installation after Windows releases its exited executable mapping.
    /// </summary>
    /// <param name="root">The fixture directory created by this test.</param>
    /// <returns>The bounded fixture cleanup.</returns>
    private static async Task DeleteOwnedFixtureAsync(string root)
    {
        string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), TestDirectory));
        string target = Path.GetFullPath(root);
        if (!string.Equals(Path.GetDirectoryName(target), parent, StringComparison.OrdinalIgnoreCase) || !Guid.TryParse(Path.GetFileName(target), out _))
            throw new InvalidOperationException("The cleanup target is not an owned game-launch fixture.");

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(target, true);
                return;
            }
            catch (Exception error) when (attempt < 19 && error is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }
        }
    }

    /// <summary>
    /// Keeps startup preference tests independent of the player's desktop preferences.
    /// </summary>
    private sealed class StartupPreferences : IPreferences
    {
        private readonly Dictionary<string, object?> _values = [];

        /// <summary>Checks whether the isolated preference exists.</summary>
        /// <param name="key">The preference key.</param>
        /// <param name="sharedName">The unused shared group.</param>
        /// <returns>Whether the value has been stored.</returns>
        public bool ContainsKey(string key, string? sharedName = null)
            => _values.ContainsKey(key);

        /// <summary>Removes one isolated preference.</summary>
        /// <param name="key">The preference key.</param>
        /// <param name="sharedName">The unused shared group.</param>
        public void Remove(string key, string? sharedName = null)
            => _values.Remove(key);

        /// <summary>Clears the isolated preference store.</summary>
        /// <param name="sharedName">The unused shared group.</param>
        public void Clear(string? sharedName = null)
            => _values.Clear();

        /// <summary>Stores the requested preference without accessing desktop settings.</summary>
        /// <typeparam name="T">The preference value type.</typeparam>
        /// <param name="key">The preference key.</param>
        /// <param name="value">The requested value.</param>
        /// <param name="sharedName">The unused shared group.</param>
        public void Set<T>(string key, T value, string? sharedName = null)
            => _values[key] = value;

        /// <summary>Reads the saved preference or its normal application default.</summary>
        /// <typeparam name="T">The preference value type.</typeparam>
        /// <param name="key">The preference key.</param>
        /// <param name="defaultValue">The normal application default.</param>
        /// <param name="sharedName">The unused shared group.</param>
        /// <returns>The persisted value when available.</returns>
        public T Get<T>(string key, T defaultValue, string? sharedName = null)
            => _values.TryGetValue(key, out object? value) ? (T)value! : defaultValue;
    }
}
