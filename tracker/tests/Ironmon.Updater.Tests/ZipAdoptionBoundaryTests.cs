using System.Diagnostics;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises Windows path and installation-identity boundaries without downloading game objects.
/// </summary>
public sealed class ZipAdoptionBoundaryTests
{
    private const string NormalFile = "data/file.txt";
    private const string CaseCollision = "DATA/FILE.TXT";
    private const string ParentCollision = "DATA";
    private const string Traversal = "../outside.txt";
    private const string StreamPath = "file.txt:alternate";
    private const string AttributesPath = "Data/.gitattributes";
    private const string SymlinkFolder = "redirected";
    private const string DestinationFolder = "external";
    private const string SourceFolder = "installation";
    private const string Metadata = ".git";
    private const string ExecutableBytes = "MZ candidate";
    private const string CommandExecutable = "cmd.exe";
    private const string CommandOption = "/c";
    private const string DisableAutoRun = "/d";
    private const string MakeLink = "mklink";
    private const string Junction = "/J";

    /// <summary>
    /// Rejects Git paths that would alias, escape or enable unsupported conversion behavior on Windows.
    /// </summary>
    /// <param name="other">The hostile path to combine with a normal target file.</param>
    [Theory]
    [InlineData(CaseCollision)]
    [InlineData(ParentCollision)]
    [InlineData(Traversal)]
    [InlineData(StreamPath)]
    [InlineData(AttributesPath)]
    public void RejectsUnsafeAndCollidingTargetPaths(string other)
    {
        using var workspace = new TestWorkspace();
        Assert.Throws<InvalidDataException>(() => ZipGameAdopter.ValidateTreePaths(workspace.Root, [NormalFile, other]));
    }

    /// <summary>
    /// Rejects a pre-existing metadata directory before treating an installation as a plain ZIP.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SnapshotRejectsExistingMetadataDirectory()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(workspace.PathFor(Metadata));
        await Assert.ThrowsAsync<InvalidDataException>(() => GameDirectorySnapshot.ReadAsync(workspace.Root, CancellationToken.None));
        Assert.True(Directory.Exists(workspace.PathFor(Metadata)));
    }

    /// <summary>
    /// Rejects directory junctions before reading a target outside the installation and preserves the target bytes.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SnapshotRejectsRedirectedDirectories()
    {
        using var workspace = new TestWorkspace();
        var source = workspace.PathFor(SourceFolder);
        var destination = workspace.PathFor(DestinationFolder);
        var link = Path.Combine(source, SymlinkFolder);
        Directory.CreateDirectory(source);
        ZipGameFixture.Write(destination, ZipGameFixture.UserFile, ZipGameFixture.ExtraText);
        var command = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), CommandExecutable);
        var start = new ProcessStartInfo(command)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in new[] { DisableAutoRun, CommandOption, MakeLink, Junction, link, destination })
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new IOException("The fixture junction command could not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        try
        {
            Assert.True(process.ExitCode == 0, await output + await error);
            await Assert.ThrowsAsync<IOException>(() => GameDirectorySnapshot.ReadAsync(source, CancellationToken.None));
            Assert.Equal(ZipGameFixture.ExtraText, File.ReadAllText(Path.Combine(destination, ZipGameFixture.UserFile)));
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
        }
    }

    /// <summary>
    /// Requires both candidate identity files and an executable header, independently of the folder name.
    /// </summary>
    [Fact]
    public void CandidateIdentityRequiresExecutableAndIni()
    {
        using var workspace = new TestWorkspace();
        ZipGameFixture.Write(workspace.Root, GameInstallationLocator.GameIni, ZipGameFixture.IniText);
        Assert.Throws<InvalidDataException>(() => GameInstallationLocator.ValidateCandidate(workspace.Root));
        ZipGameFixture.Write(workspace.Root, GameInstallationLocator.GameExecutable, ZipGameFixture.ExtraText);
        Assert.Throws<InvalidDataException>(() => GameInstallationLocator.ValidateCandidate(workspace.Root));
        ZipGameFixture.Write(workspace.Root, GameInstallationLocator.GameExecutable, ExecutableBytes);
        GameInstallationLocator.ValidateCandidate(workspace.Root);
    }
}
