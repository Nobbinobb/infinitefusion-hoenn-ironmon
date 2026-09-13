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
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(15);

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
