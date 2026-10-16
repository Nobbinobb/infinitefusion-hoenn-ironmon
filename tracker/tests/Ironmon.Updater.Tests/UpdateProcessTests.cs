using System.Diagnostics;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Verifies process identity and refusal-to-close handling using only test-owned child processes.
/// </summary>
public sealed class UpdateProcessTests
{
    private const string HostPath = "../../../../Ironmon.Updater.CrashHost/bin/";
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
    private const string HostBinary = "/net10.0/Ironmon.Updater.CrashHost.exe";
    private const string WaitMode = "wait";
    private const string BoundaryReady = "BOUNDARY";
    private const string DifferentExecutable = "different.exe";
    private const string StandardGame = "InfiniteFusion2.exe";
    private const string PerformanceGame = "InfiniteFusion2-performance.exe";
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Detects both real executable variants in the selected installation without blocking another installation.
    /// </summary>
    /// <param name="name">The supported game executable name.</param>
    [Theory]
    [InlineData(StandardGame)]
    [InlineData(PerformanceGame)]
    public async Task BothGameExecutablesBlockOnlyTheirOwnInstallation(string name)
    {
        using var workspace = new TestWorkspace();
        using var unrelated = new TestWorkspace();
        var host = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, HostPath + Configuration + HostBinary));
        foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(host)!))
            File.Copy(file, PlainPaths.Child(workspace.Root, Path.GetFileName(file)));

        var executable = PlainPaths.Child(workspace.Root, name);
        File.Copy(host, executable);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(WaitMode);
        using var child = Process.Start(start) ?? throw new IOException("The game variant fixture could not start.");
        try
        {
            Assert.Equal(BoundaryReady, await child.StandardOutput.ReadLineAsync().WaitAsync(_timeout));
            Assert.Equal(child.Id, UpdateProcessIdentity.FindGame(workspace.Root)?.Id);
            Assert.Null(UpdateProcessIdentity.FindGame(unrelated.Root));
            await Assert.ThrowsAsync<IOException>(() => UpdateProcessIdentity.EnsureInstallationIdleAsync(workspace.Root, UpdaterHandoff.TrackerRelativePath, CancellationToken.None));
            await UpdateProcessIdentity.EnsureInstallationIdleAsync(unrelated.Root, UpdaterHandoff.TrackerRelativePath, CancellationToken.None);
            Assert.False(child.HasExited);
            var otherName = name == StandardGame ? PerformanceGame : StandardGame;
            var otherExecutable = PlainPaths.Child(workspace.Root, otherName);
            File.Copy(host, otherExecutable);
            var otherStart = new ProcessStartInfo(otherExecutable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            otherStart.ArgumentList.Add(WaitMode);
            using var other = Process.Start(otherStart) ?? throw new IOException("The second game fixture could not start.");
            try
            {
                Assert.Equal(BoundaryReady, await other.StandardOutput.ReadLineAsync().WaitAsync(_timeout));
                Assert.Throws<InvalidOperationException>(() => UpdateProcessIdentity.FindGame(workspace.Root));
                Assert.False(child.HasExited);
                Assert.False(other.HasExited);
            }
            finally
            {
                await StopOwnedAsync(other);
            }
        }
        finally
        {
            await StopOwnedAsync(child);
        }
    }

    /// <summary>
    /// Reports a normal waiting stage and continues after a process blocker clears without bypassing the next probe.
    /// </summary>
    [Fact]
    public async Task WaitingForLauncherContinuesAutomatically()
    {
        var blocked = 1;
        var checks = 0;
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var progress = new InstallationProgressScope(value =>
        {
            Assert.Equal(InstallationStage.WaitingForApplications, value.Stage);
            Assert.Equal(Ironmon.Updater.Core.UpdaterText.ProgressWaitingForApplications, value.Text);
            waiting.TrySetResult();
        });
        using var cancellation = new CancellationTokenSource(_timeout);
        var operation = UpdateProcessIdentity.WaitForInstallationIdleAsync(_ =>
        {
            Interlocked.Increment(ref checks);
            return Volatile.Read(ref blocked) != 0 ? Ironmon.Updater.Core.UpdaterText.UpdateProcessIdentityCloseTheInfiniteFusionLauncherAndInstallerBeforeUpdating : null;
        }, cancellation.Token);
        await waiting.Task.WaitAsync(_timeout);
        Assert.False(operation.IsCompleted);
        Volatile.Write(ref blocked, 0);
        await operation.WaitAsync(_timeout);
        Assert.True(checks >= 2);
    }

    /// <summary>
    /// Cancels the wait without retrying indefinitely or converting cancellation into an installation error.
    /// </summary>
    [Fact]
    public async Task WaitingForLauncherCanBeCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var operation = UpdateProcessIdentity.WaitForInstallationIdleAsync(_ => Ironmon.Updater.Core.UpdaterText.UpdateProcessIdentityCloseTheInfiniteFusionLauncherAndInstallerBeforeUpdating, cancellation.Token);
        Assert.False(operation.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    /// <summary>
    /// Does not mistake access or process-inspection failures for a running launcher that can be waited out.
    /// </summary>
    [Fact]
    public async Task WaitingDoesNotSwallowInspectionFailures()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => UpdateProcessIdentity.WaitForInstallationIdleAsync(_ => throw new UnauthorizedAccessException(), CancellationToken.None));
    }

    /// <summary>
    /// Does not mistake a reused PID for its former process or accept the same PID with a different executable path.
    /// </summary>
    [Fact]
    public async Task CreationTimeAndExecutableAreBothRequired()
    {
        using var child = Start();
        try
        {
            Assert.Equal(BoundaryReady, await child.StandardOutput.ReadLineAsync().WaitAsync(_timeout));
            var identity = UpdateProcessIdentity.Capture(child);
            using var matching = identity.Open();
            Assert.NotNull(matching);
            Assert.Null((identity with { StartedUtcTicks = identity.StartedUtcTicks - 1 }).Open());
            var wrongPath = identity with { ExecutablePath = Path.Combine(Path.GetDirectoryName(identity.ExecutablePath)!, DifferentExecutable) };
            Assert.Throws<IOException>(() => wrongPath.Open());
        }
        finally
        {
            await StopOwnedAsync(child);
        }
    }

    /// <summary>
    /// Waits rather than killing a process that has no closeable window; cancellation stops waiting and leaves it alive.
    /// </summary>
    [Fact]
    public async Task RefusedNormalClosureNeverForceTerminatesGame()
    {
        using var child = Start();
        try
        {
            Assert.Equal(BoundaryReady, await child.StandardOutput.ReadLineAsync().WaitAsync(_timeout));
            var identity = UpdateProcessIdentity.Capture(child);
            using var cancellation = new CancellationTokenSource();
            var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var exit = identity.WaitForExitAsync(requestClose: true, _ => waiting.TrySetResult(), cancellation.Token);
            await waiting.Task.WaitAsync(_timeout);
            Assert.False(exit.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exit);
            Assert.False(child.HasExited);
            await StopOwnedAsync(child);
            await identity.WaitForExitAsync(requestClose: true);
            Assert.Null(identity.Open());
        }
        finally
        {
            await StopOwnedAsync(child);
        }
    }

    /// <summary>
    /// Creates a hidden child with no closeable UI, modeling a game that refuses normal closure.
    /// </summary>
    /// <returns>The exact process owned by this fixture.</returns>
    private static Process Start()
    {
        var executable = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, HostPath + Configuration + HostBinary));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(WaitMode);
        return Process.Start(start) ?? throw new IOException("The process identity fixture could not start.");
    }

    /// <summary>
    /// Cleans up only the exact process started by this test.
    /// </summary>
    /// <param name="child">The fixture-owned process.</param>
    private static async Task StopOwnedAsync(Process child)
    {
        if (!child.HasExited)
            child.Kill(entireProcessTree: true);

        await child.WaitForExitAsync().WaitAsync(_timeout);
    }
}
