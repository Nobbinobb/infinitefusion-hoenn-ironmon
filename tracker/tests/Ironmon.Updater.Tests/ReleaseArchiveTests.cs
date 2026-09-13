using System.IO.Compression;
using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises hostile archives after their outer download hash has already been verified.
/// </summary>
public sealed class ReleaseArchiveTests
{
    private const string ArchiveName = "package.zip";
    private const string ExtractionName = "extracted";
    private const string ValidFile = "Data/Ironmon/value.json";
    private const string WrongCase = "Data/Ironmon/VALUE.json";
    private const string Traversal = "../escape";
    private const string AlternateStream = "Data/Ironmon/value.json:stream";
    private const string ExtraFile = "Data/Ironmon/extra.json";
    private const string ReservedDevice = "Data/Ironmon/CON.txt";

    /// <summary>
    /// Rejects unsafe or unlisted names and removes only the private extraction directory.
    /// </summary>
    /// <param name="path">The hostile archive entry.</param>
    [Theory]
    [InlineData(Traversal)]
    [InlineData(AlternateStream)]
    [InlineData(WrongCase)]
    [InlineData(ExtraFile)]
    [InlineData(ReservedDevice)]
    public async Task UnsafeArchiveNamesCannotEscapeExtraction(string path)
    {
        using var workspace = new TestWorkspace();
        var bytes = SignedReleaseFixture.Archive(new Dictionary<string, byte[]> { [ValidFile] = [1], [path] = [2] });
        await RejectAsync(workspace, bytes, [SignedReleaseFixture.Entry(ValidFile, [1])]);
    }

    /// <summary>
    /// Rejects exact duplicate entries and Unix symbolic links despite a correct archive fingerprint.
    /// </summary>
    /// <param name="link">Whether to inject a symbolic link instead of duplicate content.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LinksAndDuplicateEntriesAreRejected(bool link)
    {
        using var workspace = new TestWorkspace();
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            var first = archive.CreateEntry(ValidFile);
            if (link)
                first.ExternalAttributes = 0xA000 << 16;

            using (var stream = first.Open())
                stream.WriteByte(1);

            if (!link)
            {
                using var stream = archive.CreateEntry(ValidFile).Open();
                stream.WriteByte(1);
            }
        }

        await RejectAsync(workspace, output.ToArray(), [SignedReleaseFixture.Entry(ValidFile, [1])]);
    }

    /// <summary>
    /// Rejects missing entries, wrong extracted hashes and incorrect signed sizes.
    /// </summary>
    /// <param name="failure">The independent inventory failure variant.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task EveryExtractedFileMustMatchInventory(int failure)
    {
        using var workspace = new TestWorkspace();
        var bytes = SignedReleaseFixture.Archive(new Dictionary<string, byte[]> { [ValidFile] = [1] });
        var entry = SignedReleaseFixture.Entry(ValidFile, failure == 1 ? [2] : failure == 2 ? [1, 2] : [1]);
        await RejectAsync(workspace, bytes, failure == 0 ? [entry, SignedReleaseFixture.Entry(ExtraFile, [1])] : [entry]);
    }

    /// <summary>
    /// Runs the authenticated-outer-archive boundary and checks private cleanup.
    /// </summary>
    /// <param name="workspace">The disposable fixture owner.</param>
    /// <param name="bytes">The malformed ZIP bytes.</param>
    /// <param name="inventory">The independently expected file list.</param>
    /// <returns>The completed rejection assertions.</returns>
    private static async Task RejectAsync(TestWorkspace workspace, byte[] bytes, ReleaseFile[] inventory)
    {
        var archive = workspace.PathFor(ArchiveName);
        var destination = workspace.PathFor(ExtractionName);
        await File.WriteAllBytesAsync(archive, bytes);
        var expected = new GameFileContent(bytes.LongLength, TransactionStorage.Hash(bytes));
        await Assert.ThrowsAnyAsync<Exception>(() => ReleaseArchive.ExtractAsync(archive, expected, destination, inventory));
        Assert.False(Directory.Exists(destination));
        Assert.Equal(new[] { archive }, Directory.GetFileSystemEntries(workspace.Root));
    }
}
