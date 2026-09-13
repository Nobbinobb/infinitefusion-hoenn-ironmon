using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Tests streamed progress, administrator framing and independent asynchronous observers.
/// </summary>
public sealed class InstallationProgressTests
{
    /// <summary>
    /// Routes a companion's visible measurements to the originating scope without recursion through its own observer.
    /// </summary>
    [Fact]
    public void CapturedReporterRetainsTheOriginatingObserver()
    {
        var values = new List<InstallationProgress>();
        using var original = new InstallationProgressScope(values.Add);
        var report = InstallationProgressScope.CaptureReporter();
        using var companion = new InstallationProgressScope(_ => throw new InvalidOperationException("The companion scope must not receive its own forwarded progress."));
        var measurement = new InstallationProgress(InstallationStage.DownloadingPackage, 3, 4);
        report(measurement);
        Assert.Equal(measurement, Assert.Single(values));
    }

    /// <summary>
    /// Parses split carriage-return frames and discards remote text and oversized measurements.
    /// </summary>
    [Fact]
    public void GitFramesReportMeasuredCountsWithoutRemoteText()
    {
        var values = new List<InstallationProgress>();
        using var scope = new InstallationProgressScope(values.Add);
        var parser = new GitTransferProgress();
        parser.Append("remote: untrusted progress\rReceiving obj");
        parser.Append("ects: 100% (25/25), 1.50 MiB | 2.0 MiB/s, done.\rResolving deltas: 100% (12/12), done.\n");
        parser.Append(new string('x', 5000));
        parser.Append("Receiving objects: 100% (25/25)\rReceiving objects: 150% (30/25)\n");
        Assert.Collection(values, value => Assert.Equal(new InstallationProgress(InstallationStage.DownloadingGame, 25, 25, 1572864), value), value => Assert.Equal(new InstallationProgress(InstallationStage.ProcessingGame, 12, 12), value));
    }

    /// <summary>
    /// Keeps simultaneous operations isolated and restores the enclosing observer after nested work.
    /// </summary>
    [Fact]
    public async Task ObserversFollowOnlyTheirOwnAsyncOperation()
    {
        var outer = new List<InstallationProgress>();
        using var scope = new InstallationProgressScope(outer.Add);
        var results = await Task.WhenAll(Enumerable.Range(1, 4).Select(async count =>
        {
            var values = new List<InstallationProgress>();
            using var inner = new InstallationProgressScope(values.Add);
            await Task.Yield();
            InstallationProgressScope.Report(new(InstallationStage.ExtractingFiles, count, count));
            return Assert.Single(values).Completed;
        }));
        Assert.Equal([1L, 2L, 3L, 4L], results);
        Assert.Empty(outer);
        InstallationProgressScope.Report(new(InstallationStage.VerifyingFiles));
        Assert.Single(outer);
    }

    /// <summary>
    /// Reads progress frames without mistaking them for administrator completion or cancellation.
    /// </summary>
    [Fact]
    public async Task AdministratorProgressWaitsForTheTerminalReply()
    {
        using var stream = new MemoryStream();
        var measurement = new InstallationProgress(InstallationStage.PreparingRecovery, 3, 9);
        await ProtectedUpdateProtocol.WriteAsync(stream, new ProtectedUpdateReply(Progress: measurement), CancellationToken.None);
        await ProtectedUpdateProtocol.WriteAsync(stream, new ProtectedUpdateReply(Cancelled: true), CancellationToken.None);
        stream.Position = 0;
        var values = new List<InstallationProgress>();
        using var scope = new InstallationProgressScope(values.Add);
        var result = await ProtectedUpdateClient.ReadReplyAsync(stream, CancellationToken.None);
        Assert.True(result.Cancelled);
        Assert.Equal(measurement, Assert.Single(values));
        Assert.Equal(stream.Length, stream.Position);
    }

    /// <summary>
    /// Rejects progress-bearing terminal responses instead of concealing an administrator error.
    /// </summary>
    [Fact]
    public async Task AdministratorProgressCannotHideATerminalError()
    {
        using var stream = new MemoryStream();
        await ProtectedUpdateProtocol.WriteAsync(stream, new ProtectedUpdateReply(Error: "failure", Progress: new(InstallationStage.VerifyingFiles)), CancellationToken.None);
        stream.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => ProtectedUpdateClient.ReadReplyAsync(stream, CancellationToken.None));
    }

    /// <summary>
    /// Preserves immediate phase and final notifications while dropping invalid external measurements.
    /// </summary>
    [Fact]
    public void BoundsMeasurementsAndPreservesStageCompletion()
    {
        var values = new List<InstallationProgress>();
        using var scope = new InstallationProgressScope(values.Add);
        InstallationProgressScope.Report(new(InstallationStage.ExtractingFiles, 0, 100));
        for (var count = 1; count <= 100; count++)
            InstallationProgressScope.Report(new(InstallationStage.ExtractingFiles, count, 100));

        InstallationProgressScope.Report(new(InstallationStage.ExtractingFiles, 101, 100));
        InstallationProgressScope.Report(new((InstallationStage)999));
        InstallationProgressScope.Report(new(InstallationStage.InstallingFiles));
        Assert.Equal(0, values[0].Completed);
        Assert.Contains(new InstallationProgress(InstallationStage.ExtractingFiles, 100, 100), values);
        Assert.Equal(InstallationStage.InstallingFiles, values[^1].Stage);
        Assert.DoesNotContain(values, value => value.Completed > value.Total);
    }
}
