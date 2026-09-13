using System.Diagnostics;
using System.Text.Json;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises the real cross-process acknowledgement protocol with disposable renamed tracker and helper hosts.
/// </summary>
public sealed class UpdaterHandoffTests
{
    private const string HostPath = "../../../../Ironmon.Updater.CrashHost/bin/";
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
    private const string HostFolder = "/net10.0";
    private const string HostFile = "Ironmon.Updater.CrashHost.exe";
    private const string HelperPath = ".ironmon-update/recovery/1/Ironmon.Updater.exe";
    private const string ParentMode = "handoff-parent";
    private const string HandoffLog = ".ironmon-update/handoff-fixture.log";
    private const string RejectMarker = ".ironmon-update/reject-fixture";
    private const string FailedHandoff = "FAILED";
    private const string Validated = "validated";
    private const string Quiesced = "quiesced";
    private const string Closing = "closing";
    private const string Continued = "continued";
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(35);

    /// <summary>
    /// Keeps the parent alive when readiness fails and orders normal handoff acknowledgement before quiescence and exit.
    /// </summary>
    /// <param name="reject">Whether independent helper validation rejects the handoff.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HelperAcknowledgesBeforeTrackerShutdown(bool reject)
    {
        using var workspace = new TestWorkspace();
        TransactionFixture.Write(workspace.Root, TransactionFixture.FixtureMarker, 1);
        CopyHost(workspace.Root, UpdaterHandoff.TrackerRelativePath);
        CopyHost(workspace.Root, HelperPath);
        if (reject)
            TransactionFixture.Write(workspace.Root, RejectMarker, 1);

        var start = new ProcessStartInfo(Path.Combine(workspace.Root, UpdaterHandoff.TrackerRelativePath)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(ParentMode);
        start.ArgumentList.Add(workspace.Root);
        using var parent = Process.Start(start) ?? throw new IOException("The fixture tracker could not start.");
        UpdateProcessIdentity? helper = null;
        try
        {
            var line = await parent.StandardOutput.ReadLineAsync().WaitAsync(_timeout);
            if (reject)
            {
                Assert.Equal(FailedHandoff, line);
                Assert.False(parent.HasExited);
                Assert.False(File.Exists(Path.Combine(workspace.Root, HandoffLog)));
            }
            else
            {
                Assert.NotNull(line);
                if (line == FailedHandoff)
                    Assert.Fail(await parent.StandardOutput.ReadLineAsync().WaitAsync(_timeout));

                helper = JsonSerializer.Deserialize<UpdateProcessIdentity>(line)!;
                await parent.WaitForExitAsync().WaitAsync(_timeout);
                await helper.WaitForExitAsync(requestClose: false).WaitAsync(_timeout);
                Assert.Equal(0, parent.ExitCode);
                Assert.Equal(new[] { Validated, Quiesced, Closing, Continued }, await File.ReadAllLinesAsync(Path.Combine(workspace.Root, HandoffLog)));
            }
        }
        finally
        {
            if (!parent.HasExited)
            {
                parent.Kill(entireProcessTree: true);
                await parent.WaitForExitAsync().WaitAsync(_timeout);
            }

            using var child = helper?.Open();
            if (child is not null)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync().WaitAsync(_timeout);
            }

            await WaitForFixtureHelpersAsync(workspace.Root);
        }
    }

    /// <summary>
    /// Waits for rejected fixture helpers before deleting their loaded libraries; waiting for the parent alone does not await terminated descendants.
    /// </summary>
    /// <param name="root">The unique fixture installation that bounds helper ownership.</param>
    /// <returns>A task completing after helpers at this fixture's exact executable path exit.</returns>
    private static async Task WaitForFixtureHelpersAsync(string root)
    {
        var executable = Path.GetFullPath(Path.Combine(root, HelperPath));
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(HelperPath)))
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited && string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                        await process.WaitForExitAsync().WaitAsync(_timeout);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                }
            }
        }
    }

    /// <summary>
    /// Copies the built fixture host and its runtime dependencies beneath a disposable fixed executable path.
    /// </summary>
    /// <param name="root">The fixture installation.</param>
    /// <param name="relativeExecutable">The fixed tracker or helper path being tested.</param>
    private static void CopyHost(string root, string relativeExecutable)
    {
        var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, HostPath + Configuration + HostFolder));
        var executable = Path.Combine(root, relativeExecutable);
        var destination = Path.GetDirectoryName(executable)!;
        Directory.CreateDirectory(destination);
        foreach (var path in Directory.EnumerateFiles(source))
            File.Copy(path, Path.Combine(destination, Path.GetFileName(path)));

        File.Copy(Path.Combine(source, HostFile), executable);
    }
}
