using System.Diagnostics;
using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;
using Xunit.Abstractions;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises folder promotion, ordinary recovery compatibility and bounded preparation with real disposable files.
/// </summary>
/// <remarks>
/// Captures comparative timings without imposing a machine-dependent performance threshold.
/// </remarks>
/// <param name="output">The test result timing sink.</param>
public sealed class FreshInstallationTests(ITestOutputHelper output)
{
    private const string DataFile = "Data/a.bin";
    private const string OtherDataFile = "Data/b.bin";
    private const string GameFile = "Game.exe";
    private const string SpriteFile = "Graphics/sheet.png";
    private const string ExtraFile = "Data/unlisted.bin";
    private const string ReplacementFile = "replacement.bin";
    private const string BoundaryReady = "BOUNDARY";
    private const string ApplyMode = "apply";
    private const string RecoverMode = "recover";
#if DEBUG
    private const string Host = "../../../../Ironmon.Updater.CrashHost/bin/Debug/net10.0/Ironmon.Updater.CrashHost.exe";
#else
    private const string Host = "../../../../Ironmon.Updater.CrashHost/bin/Release/net10.0/Ironmon.Updater.CrashHost.exe";
#endif
    private static readonly string[] _files = [DataFile, OtherDataFile, GameFile, SpriteFile];
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Allows a cancelled fresh-install destination to be selected again while retaining its recovery evidence.
    /// </summary>
    [Fact]
    public async Task DiscardedFreshPreparationCanBeSelectedAgain()
    {
        using var fixture = new TransactionFixture();
        await fixture.PrepareFreshAsync(_files);
        Assert.Equal(fixture.Root, SetupPreparation.ResolveDestination(fixture.Root).Root);
        await fixture.Engine().DiscardPreparedAsync(fixture.Root, fixture.Description.TransactionId);
        Assert.Null(UpdateTransaction.ReadActiveId(fixture.Root));
        Assert.True(Directory.Exists(fixture.DirectoryPath));
        Assert.Empty(await InstallationFileSnapshot.ReadAsync(fixture.Root));
        var destination = SetupPreparation.InspectDestination(fixture.Root);
        Assert.Equal(fixture.Root, destination.Root);
        Assert.Null(destination.InstalledFlavor);
        Assert.Equal(fixture.Root, SetupPreparation.ResolveDestination(fixture.Root).Root);
    }

    /// <summary>
    /// Retained updater state does not make an unrelated folder eligible for a fresh installation.
    /// </summary>
    [Fact]
    public async Task DiscardedFreshPreparationStillRejectsUnrelatedContent()
    {
        using var fixture = new TransactionFixture();
        await fixture.PrepareFreshAsync(_files);
        await fixture.Engine().DiscardPreparedAsync(fixture.Root, fixture.Description.TransactionId);
        TransactionFixture.Write(fixture.Root, ExtraFile, 7);
        Assert.Throws<InvalidDataException>(() => SetupPreparation.InspectDestination(fixture.Root));
        Assert.Throws<InvalidDataException>(() => SetupPreparation.ResolveDestination(fixture.Root));
        Assert.True(File.Exists(Path.Combine(fixture.Root, ExtraFile)));
    }

    /// <summary>
    /// Commits with three folder/file moves and one intent whether extraction is transferred or copied.
    /// </summary>
    /// <param name="consumePayload">Whether extraction ownership is available.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FreshInstallPromotesCompleteFolders(bool consumePayload)
    {
        using var fixture = new TransactionFixture();
        await fixture.PrepareFreshAsync(_files, consumePayload);
        Assert.Equal(!consumePayload, Directory.Exists(fixture.Payload));
        var boundaries = new List<TransactionProgress>();
        var result = await fixture.Engine(boundaries.Add).ApplyAsync(fixture.Root, fixture.Description.TransactionId);
        Assert.Equal(TransactionPhase.Committed, result.Phase);
        Assert.Single(boundaries, value => value.Phase == TransactionPhase.Applying && value.Boundary == TransactionBoundary.Intent);
        Assert.Equal(3, boundaries.Count(value => value.Phase == TransactionPhase.Applying && value.Boundary == TransactionBoundary.Mutation));
        Assert.False(File.Exists(Path.Combine(fixture.DirectoryPath, ReplacementFile)));
        Assert.Equal(FilePlanApplicability.AlreadyApplied, fixture.Description.Plan.Rebuild().Assess(await InstallationFileSnapshot.ReadAsync(fixture.Root)));
    }

    /// <summary>
    /// Rejects an extra staged file before a folder move could install unmanaged content.
    /// </summary>
    [Fact]
    public async Task UnlistedStagingContentBlocksPromotion()
    {
        using var fixture = new TransactionFixture();
        await fixture.PrepareFreshAsync(_files);
        TransactionFixture.Write(Path.Combine(fixture.DirectoryPath, TransactionStorage.PayloadDirectory), ExtraFile, 7);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Engine().ApplyAsync(fixture.Root, fixture.Description.TransactionId));
        Assert.Empty(await InstallationFileSnapshot.ReadAsync(fixture.Root));
        Assert.Equal(TransactionPhase.RolledBack, (await fixture.Engine().RecoverAsync(fixture.Root, fixture.Description.TransactionId)).Phase);
    }

    /// <summary>
    /// Cancels after a directory was moved and restores the originally empty game folder.
    /// </summary>
    [Fact]
    public async Task CancellationAfterFolderMoveRestoresEmptyInstallation()
    {
        using var fixture = new TransactionFixture();
        await fixture.PrepareFreshAsync(_files);
        using var cancellation = new CancellationTokenSource();
        var engine = fixture.Engine(progress =>
        {
            if (progress.Phase == TransactionPhase.Applying && progress.Boundary == TransactionBoundary.Mutation)
                cancellation.Cancel();
        });
        var result = await engine.ApplyAsync(fixture.Root, fixture.Description.TransactionId, cancellationToken: cancellation.Token);
        Assert.Equal(TransactionPhase.RolledBack, result.Phase);
        Assert.Empty(await InstallationFileSnapshot.ReadAsync(fixture.Root));
    }

    /// <summary>
    /// Retains unexpected user content introduced into a promoted directory during a failed installation.
    /// </summary>
    [Fact]
    public async Task UnexpectedUserFileIsPreservedDuringRecovery()
    {
        using var fixture = new TransactionFixture();
        await fixture.PrepareFreshAsync(_files);
        var changed = false;
        var engine = fixture.Engine(progress =>
        {
            if (!changed && progress.Phase == TransactionPhase.Applying && progress.Boundary == TransactionBoundary.Mutation)
            {
                changed = true;
                TransactionFixture.Write(fixture.Root, ExtraFile, 7);
                throw new IOException("Fixture interruption after unrelated content was added.");
            }
        });
        Assert.Equal(TransactionPhase.RecoveryRequired, (await engine.ApplyAsync(fixture.Root, fixture.Description.TransactionId)).Phase);
        Assert.Equal(new byte[] { 7 }, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, ExtraFile)));
        Assert.NotNull(UpdateTransaction.ReadActiveId(fixture.Root));
    }

    /// <summary>
    /// Recovers from a killed process before, between and after the folder moves using the unchanged journal cursor.
    /// </summary>
    /// <param name="phase">The phase in which the owned helper is stopped.</param>
    /// <param name="boundary">The selected durability boundary.</param>
    /// <param name="occurrence">The selected folder move or matching event.</param>
    [Theory]
    [InlineData(TransactionPhase.Applying, TransactionBoundary.Intent, 1)]
    [InlineData(TransactionPhase.Applying, TransactionBoundary.Mutation, 1)]
    [InlineData(TransactionPhase.Applying, TransactionBoundary.Mutation, 2)]
    [InlineData(TransactionPhase.Applying, TransactionBoundary.Mutation, 3)]
    [InlineData(TransactionPhase.Applying, TransactionBoundary.Completion, 1)]
    [InlineData(TransactionPhase.RollingBack, TransactionBoundary.Intent, 1)]
    [InlineData(TransactionPhase.RollingBack, TransactionBoundary.Mutation, 1)]
    public async Task FreshProcessRecoversPartialFolderPromotion(TransactionPhase phase, TransactionBoundary boundary, int occurrence)
    {
        using var fixture = new TransactionFixture();
        await fixture.PrepareFreshAsync(_files);
        var cursor = fixture.Description.Plan.Rebuild().GetOperations().Count - 1;
        using (var child = Start(fixture, ApplyMode, phase, cursor, boundary, occurrence))
        {
            try
            {
                Assert.Equal(BoundaryReady, await child.StandardOutput.ReadLineAsync().WaitAsync(_timeout));
            }
            finally
            {
                if (!child.HasExited)
                    child.Kill(entireProcessTree: true);

                await child.WaitForExitAsync().WaitAsync(_timeout);
            }
        }

        using var recovery = Start(fixture, RecoverMode, TransactionPhase.Prepared, -1, TransactionBoundary.Intent, 1);
        try
        {
            var text = await recovery.StandardOutput.ReadToEndAsync().WaitAsync(_timeout);
            var error = await recovery.StandardError.ReadToEndAsync().WaitAsync(_timeout);
            await recovery.WaitForExitAsync().WaitAsync(_timeout);
            Assert.True(recovery.ExitCode == 0, error + text);
            Assert.Contains(nameof(TransactionPhase.RolledBack), text, StringComparison.Ordinal);
        }
        finally
        {
            if (!recovery.HasExited)
            {
                recovery.Kill(entireProcessTree: true);
                await recovery.WaitForExitAsync().WaitAsync(_timeout);
            }
        }

        Assert.Empty(await InstallationFileSnapshot.ReadAsync(fixture.Root));
        Assert.Null(UpdateTransaction.ReadActiveId(fixture.Root));
    }

    /// <summary>
    /// Measures equal small-file payloads and checks that fresh-install bookkeeping no longer scales with file count.
    /// </summary>
    [Fact]
    public async Task ManySmallFilesUseOneFreshIntent()
    {
        const string FilePattern = "Data/files/{0:D4}.bin";
        var files = Enumerable.Range(0, 1000).Select(index => string.Format(System.Globalization.CultureInfo.InvariantCulture, FilePattern, index)).ToArray();
        foreach (var existing in new[] { true, false })
        {
            using var fixture = new TransactionFixture();
            var timer = Stopwatch.StartNew();
            await fixture.PrepareFreshAsync(files, existing: existing);
            var preparation = timer.Elapsed;
            var intents = 0;
            timer.Restart();
            var engine = fixture.Engine(progress =>
            {
                if (progress.Phase == TransactionPhase.Applying && progress.Boundary == TransactionBoundary.Intent)
                    intents++;
            });
            Assert.Equal(TransactionPhase.Committed, (await engine.ApplyAsync(fixture.Root, fixture.Description.TransactionId)).Phase);
            output.WriteLine($"Existing={existing}; files={files.Length}; prepare={preparation.TotalSeconds:F2}s; apply={timer.Elapsed.TotalSeconds:F2}s; intents={intents}");
            Assert.Equal(existing ? files.Length + 2 : 1, intents);
        }
    }

    /// <summary>
    /// Launches only the disposable crash host with a marker and independently supplied descriptor hash.
    /// </summary>
    /// <param name="fixture">The owned installation.</param>
    /// <param name="mode">The apply or recovery command.</param>
    /// <param name="phase">The phase to interrupt.</param>
    /// <param name="cursor">The durable file cursor.</param>
    /// <param name="boundary">The event to interrupt.</param>
    /// <param name="occurrence">The matching event count.</param>
    /// <returns>The exact test process that may be terminated.</returns>
    private static Process Start(TransactionFixture fixture, string mode, TransactionPhase phase, int cursor, TransactionBoundary boundary, int occurrence)
    {
        var start = new ProcessStartInfo(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, Host))) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, WorkingDirectory = fixture.Root };
        foreach (var argument in new[] { mode, fixture.Root, fixture.Description.TransactionId.ToString(TransactionStorage.GuidFormat), fixture.AuthorizedHash, phase.ToString(), cursor.ToString(System.Globalization.CultureInfo.InvariantCulture), boundary.ToString(), occurrence.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            start.ArgumentList.Add(argument);

        return Process.Start(start) ?? throw new IOException("The fixture crash host did not start.");
    }
}
