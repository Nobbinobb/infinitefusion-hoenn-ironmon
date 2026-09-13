using System.Diagnostics;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Terminates only test-owned helper processes and verifies recovery in another independently started process.
/// </summary>
public sealed class TransactionCrashTests
{
    private const string HostPath = "../../../../Ironmon.Updater.CrashHost/bin/";
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
    private const string HostBinary = "/net10.0/Ironmon.Updater.CrashHost.exe";
    private const string ApplyMode = "apply";
    private const string RecoverMode = "recover";
    private const string BoundaryReady = "BOUNDARY";
    private const string GitIndex = ".git/index";
    private const string GitRef = ".git/refs/heads/releases";
    private const string GitObject = ".git/objects/old";
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Enumerates every forward and reverse file durability boundary and both Git rename sides.
    /// </summary>
    /// <returns>Independent crash scenarios.</returns>
    public static IEnumerable<object[]> Boundaries()
    {
        foreach (var zip in new[] { false, true })
        {
            for (var operation = 0; operation <= 4; operation++)
            {
                foreach (var boundary in new[] { TransactionBoundary.Intent, TransactionBoundary.Mutation, TransactionBoundary.Completion })
                    yield return [TransactionPhase.Applying, operation, boundary, zip];
            }

            yield return [TransactionPhase.Applying, 4, TransactionBoundary.GitDetached, zip];
            foreach (var boundary in new[] { TransactionBoundary.Intent, TransactionBoundary.GitDetached, TransactionBoundary.Mutation, TransactionBoundary.Completion })
                yield return [TransactionPhase.RollingBack, 4, boundary, zip];

            for (var operation = 0; operation < 4; operation++)
            {
                yield return [TransactionPhase.RollingBack, operation, TransactionBoundary.Intent, zip];
                yield return [TransactionPhase.RollingBack, operation, TransactionBoundary.Mutation, zip];
                yield return [TransactionPhase.RollingBack, operation - 1, TransactionBoundary.Completion, zip];
            }
        }
    }

    /// <summary>
    /// Kills the isolated engine at an actual mutation boundary and restores A using a new process and persisted data only.
    /// </summary>
    /// <param name="phase">The selected application or rollback phase.</param>
    /// <param name="operation">The selected journal cursor.</param>
    /// <param name="boundary">The selected durability boundary.</param>
    /// <param name="zip">Whether the original installation had no Git metadata.</param>
    [Theory]
    [MemberData(nameof(Boundaries))]
    public async Task FreshProcessRecoversEveryInterruptedMutation(TransactionPhase phase, int operation, TransactionBoundary boundary, bool zip)
    {
        using var fixture = new TransactionFixture();
        await fixture.PrepareAsync(git: true, zip);
        using (var child = Start(fixture, ApplyMode, phase, operation, boundary))
        {
            try
            {
                var line = await child.StandardOutput.ReadLineAsync().WaitAsync(_timeout);
                Assert.Equal(BoundaryReady, line);
            }
            finally
            {
                if (!child.HasExited)
                    child.Kill(entireProcessTree: true);

                await child.WaitForExitAsync().WaitAsync(_timeout);
            }
        }

        using var recovery = Start(fixture, RecoverMode, TransactionPhase.Prepared, -1, TransactionBoundary.Intent);
        try
        {
            var output = await recovery.StandardOutput.ReadToEndAsync().WaitAsync(_timeout);
            var error = await recovery.StandardError.ReadToEndAsync().WaitAsync(_timeout);
            await recovery.WaitForExitAsync().WaitAsync(_timeout);
            Assert.True(recovery.ExitCode == 0, error + output);
            Assert.Contains(nameof(TransactionPhase.RolledBack), output, StringComparison.Ordinal);
        }
        finally
        {
            if (!recovery.HasExited)
            {
                recovery.Kill(entireProcessTree: true);
                await recovery.WaitForExitAsync().WaitAsync(_timeout);
            }
        }

        await fixture.AssertRestoredAsync();
        if (zip)
        {
            Assert.False(Directory.Exists(Path.Combine(fixture.Root, TransactionStorage.GitDirectory)));
        }
        else
        {
            Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(fixture.Root, GitIndex)));
            Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(fixture.Root, GitRef)));
            Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(fixture.Root, GitObject)));
        }
    }

    /// <summary>
    /// Starts a hidden test-owned console host with structured arguments and no shell interpretation.
    /// </summary>
    /// <param name="fixture">The disposable installation.</param>
    /// <param name="mode">The fixture apply or recover command.</param>
    /// <param name="phase">The selected interruption phase.</param>
    /// <param name="operation">The selected cursor.</param>
    /// <param name="boundary">The selected boundary.</param>
    /// <returns>The exact process the fixture owns and may terminate.</returns>
    private static Process Start(TransactionFixture fixture, string mode, TransactionPhase phase, int operation, TransactionBoundary boundary)
    {
        var executable = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, HostPath + Configuration + HostBinary));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, WorkingDirectory = fixture.Root };
        foreach (var argument in new[] { mode, fixture.Root, fixture.Description.TransactionId.ToString(TransactionStorage.GuidFormat), fixture.AuthorizedHash, phase.ToString(), operation.ToString(System.Globalization.CultureInfo.InvariantCulture), boundary.ToString() })
            start.ArgumentList.Add(argument);

        return Process.Start(start) ?? throw new IOException("The fixture crash host did not start.");
    }
}
