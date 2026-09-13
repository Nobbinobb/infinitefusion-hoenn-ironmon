using System.Diagnostics;
using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;
using static Ironmon.Updater.Tests.FilePlannerFixture;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Proves read-only planning and repeat-application behavior using disposable real files.
/// </summary>
public sealed class FilePlanFilesystemTests
{
    private const string RootName = "installation";
    private const string GitDirectory = ".git";
    private const string UpdateDirectory = ".ironmon-update";
    private const string MetadataFile = "config";
    private const string SavePath = "File A.rxdata";
    private const string EmptyDirectory = "personal-empty";
    private const string LinkName = "redirected";
    private const string CommandExecutable = "cmd.exe";
    private const string DisableAutoRun = "/d";
    private const string CommandOption = "/c";
    private const string MakeLink = "mklink";
    private const string Junction = "/J";
    private const string ShapePath = "Data/shape";
    private const string ShapeChild = "Data/shape/owned.bin";

    /// <summary>
    /// Executes both clean structural transitions with non-recursive directory operations on real fixture files.
    /// </summary>
    /// <param name="directoryToFile">Whether the old state is a directory containing a managed file.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FixtureAppliesDirectoryTransitionsInSafeOrder(bool directoryToFile)
    {
        using var workspace = new TestWorkspace();
        var root = workspace.PathFor(RootName);
        var oldPath = directoryToFile ? ShapeChild : ShapePath;
        var newPath = directoryToFile ? ShapePath : ShapeChild;
        Write(root, oldPath, 1);
        var before = await InstallationFileSnapshot.ReadAsync(root);
        var plan = Planner().Create([File(oldPath, 1)], before, [File(newPath, 2)]);
        Assert.Single(ApplyFixture(root, plan));
        Assert.Equal(new byte[] { 2 }, System.IO.File.ReadAllBytes(Path.Combine(root, newPath)));
        Assert.Equal(FilePlanApplicability.AlreadyApplied, plan.Assess(await InstallationFileSnapshot.ReadAsync(root)));
    }

    /// <summary>
    /// Applies reviewed operations only in a fixture, verifies backups and skips a complete second application.
    /// </summary>
    [Fact]
    public async Task FixtureApplicationBacksUpExactBytesAndIsNotReplayed()
    {
        using var workspace = new TestWorkspace();
        var root = workspace.PathFor(RootName);
        Write(root, GamePath, 1);
        Write(root, OtherPath, 1);
        Write(root, ExtraPath, 3);
        Write(root, SavePath, 3);
        Directory.CreateDirectory(Path.Combine(root, EmptyDirectory));
        Directory.CreateDirectory(Path.Combine(root, GitDirectory));
        Write(root, GitDirectory + '/' + MetadataFile, 3);
        Directory.CreateDirectory(Path.Combine(root, UpdateDirectory));
        Write(root, UpdateDirectory + '/' + MetadataFile, 3);
        var before = await InstallationFileSnapshot.ReadAsync(root);
        var plan = Planner().Create([File(GamePath, 1), File(OtherPath, 1)], before, [File(GamePath, 2)]);
        Assert.Equal(before, await InstallationFileSnapshot.ReadAsync(root));
        Assert.Equal(FilePlanApplicability.Ready, plan.Assess(before));
        var backups = ApplyFixture(root, plan);
        Assert.Equal(2, backups.Count);
        Assert.All(backups.Values, bytes => Assert.Equal(new byte[] { 1 }, bytes));
        var after = await InstallationFileSnapshot.ReadAsync(root);
        Assert.Equal(FilePlanApplicability.AlreadyApplied, plan.Assess(after));
        Assert.Equal(new byte[] { 3 }, System.IO.File.ReadAllBytes(Path.Combine(root, ExtraPath)));
        Assert.Equal(new byte[] { 3 }, System.IO.File.ReadAllBytes(Path.Combine(root, SavePath)));
        Assert.True(Directory.Exists(Path.Combine(root, EmptyDirectory)));
        Assert.True(System.IO.File.Exists(Path.Combine(root, GitDirectory, MetadataFile)));
        Assert.Empty(ApplyFixture(root, plan));
        Assert.Equal(after, await InstallationFileSnapshot.ReadAsync(root));
    }

    /// <summary>
    /// Rejects a previously reviewed plan after a user edits a preserved file.
    /// </summary>
    [Fact]
    public async Task FreshSnapshotDetectsLateUserChanges()
    {
        using var workspace = new TestWorkspace();
        var root = workspace.PathFor(RootName);
        Write(root, GamePath, 1);
        Write(root, ExtraPath, 3);
        var plan = Planner().Create([File(GamePath, 1)], await InstallationFileSnapshot.ReadAsync(root), [File(GamePath, 2)]);
        Write(root, ExtraPath, 2);
        Assert.Equal(FilePlanApplicability.Changed, plan.Assess(await InstallationFileSnapshot.ReadAsync(root)));
        Assert.Equal(new byte[] { 1 }, System.IO.File.ReadAllBytes(Path.Combine(root, GamePath)));
    }

    /// <summary>
    /// Rejects redirected directories instead of following them during planning.
    /// </summary>
    [Fact]
    public async Task SnapshotRejectsRedirectedPathsAndHonorsCancellation()
    {
        using var workspace = new TestWorkspace();
        var root = workspace.PathFor(RootName);
        Directory.CreateDirectory(root);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => InstallationFileSnapshot.ReadAsync(root, cancellation.Token));
        var link = Path.Combine(root, LinkName);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), CommandExecutable))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in new[] { DisableAutoRun, CommandOption, MakeLink, Junction, link, workspace.Root })
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new IOException("The fixture junction process could not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        try
        {
            Assert.True(process.ExitCode == 0, await output + await error);
            await Assert.ThrowsAsync<IOException>(() => InstallationFileSnapshot.ReadAsync(root));
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
        }
    }

    /// <summary>
    /// Writes one fixture byte beneath the disposable root.
    /// </summary>
    /// <param name="root">The fixture root.</param>
    /// <param name="path">The fixture-relative file path.</param>
    /// <param name="value">The one-byte payload.</param>
    private static void Write(string root, string path, byte value)
    {
        var destination = PlainPaths.Child(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        System.IO.File.WriteAllBytes(destination, [value]);
    }

    /// <summary>
    /// Executes one reviewed fixture plan with in-memory verified backups; production transactions belong to Step 5.
    /// </summary>
    /// <param name="root">The disposable fixture installation.</param>
    /// <param name="plan">The reviewed plan with exact byte expectations.</param>
    /// <returns>The original file bytes captured before mutation, or no backups when already applied.</returns>
    private static Dictionary<string, byte[]> ApplyFixture(string root, FileUpdatePlan plan)
    {
        var snapshot = InstallationFileSnapshot.ReadAsync(root).GetAwaiter().GetResult();
        var backups = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var applicability = plan.Assess(snapshot);
        if (applicability == FilePlanApplicability.AlreadyApplied)
            return backups;

        Assert.Equal(FilePlanApplicability.Ready, applicability);
        foreach (var operation in plan.GetOperations().Where(operation => operation.RequiresBackup))
        {
            var bytes = System.IO.File.ReadAllBytes(PlainPaths.Child(root, operation.Path));
            Assert.Equal(operation.ExpectedBefore, Content(Assert.Single(bytes)));
            backups.Add(operation.Path, bytes);
        }

        foreach (var operation in plan.GetOperations())
        {
            var path = PlainPaths.Child(root, operation.Path);
            if (operation.Kind == FileOperationKind.DeleteFile)
            {
                System.IO.File.Delete(path);
            }
            else if (operation.Kind == FileOperationKind.RemoveDirectory)
            {
                Directory.Delete(path);
            }
            else if (operation.Kind == FileOperationKind.CreateDirectory)
            {
                Directory.CreateDirectory(path);
            }
            else
            {
                Assert.Equal(Content(2), operation.ExpectedAfter);
                System.IO.File.WriteAllBytes(path, [2]);
            }
        }

        return backups;
    }
}
