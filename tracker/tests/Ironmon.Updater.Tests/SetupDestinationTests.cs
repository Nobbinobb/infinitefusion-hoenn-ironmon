using Ironmon.Updater.Infrastructure;
using System.Text;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Verifies parent selection, existing-game precedence and collisions using isolated local folders.
/// </summary>
public sealed class SetupDestinationTests
{
    private const string ParentFolder = "parent";
    private const string UnrelatedFile = "personal.txt";
    private const string PersonalContent = "keep this file";
    private const string Ini = "[Game]\nTitle=infinitefusion-hoenn\n";
    private const string CoreRuntime = "Ironmon Tracker/coreclr.dll";

    /// <summary>
    /// Resolves new, empty and occupied parents without creating the destination or modifying the selected folder.
    /// </summary>
    /// <param name="existing">Whether the parent already exists.</param>
    /// <param name="occupied">Whether it contains unrelated files.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ParentSelectionDoesNotWriteFiles(bool existing, bool occupied)
    {
        using var workspace = new TestWorkspace();
        var parent = workspace.PathFor(ParentFolder);
        if (existing)
            Directory.CreateDirectory(parent);

        if (occupied)
            File.WriteAllText(Path.Combine(parent, UnrelatedFile), PersonalContent);

        var result = SetupPreparation.ResolveDestination(parent);
        Assert.Equal(Path.Combine(parent, SetupPreparation.InstallationDirectoryName), result.Root);
        Assert.Null(result.InstalledFlavor);
        Assert.False(Directory.Exists(result.Root));
        Assert.Equal(existing, Directory.Exists(parent));
        if (occupied)
            Assert.Equal(PersonalContent, File.ReadAllText(Path.Combine(parent, UnrelatedFile)));
    }

    /// <summary>
    /// Uses a game in the selected directory before looking at the standard child, preserving its package type.
    /// </summary>
    /// <param name="nested">Whether only the named child contains a game.</param>
    /// <param name="flavor">The installed package, or null for a game without Ironmon.</param>
    [Theory]
    [InlineData(false, null)]
    [InlineData(false, ReleaseProtocol.SelfContained)]
    [InlineData(false, ReleaseProtocol.RuntimeRequired)]
    [InlineData(true, null)]
    [InlineData(true, ReleaseProtocol.SelfContained)]
    [InlineData(true, ReleaseProtocol.RuntimeRequired)]
    public void ExistingGameIsReused(bool nested, string? flavor)
    {
        using var workspace = new TestWorkspace();
        var parent = workspace.PathFor(ParentFolder);
        var child = Path.Combine(parent, SetupPreparation.InstallationDirectoryName);
        var root = nested ? child : parent;
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, GameInstallationLocator.GameExecutable), [77, 90]);
        File.WriteAllText(Path.Combine(root, GameInstallationLocator.GameIni), Ini, Encoding.UTF8);
        if (flavor is not null)
        {
            var tracker = Path.Combine(root, UpdaterHandoff.TrackerRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(tracker)!);
            File.WriteAllBytes(tracker, [77, 90]);
            if (flavor == ReleaseProtocol.SelfContained)
                File.WriteAllBytes(Path.Combine(root, CoreRuntime), []);
        }

        if (!nested)
            File.WriteAllText(child, PersonalContent);

        var result = SetupPreparation.ResolveDestination(parent);
        Assert.Equal(root, result.Root);
        Assert.Equal(flavor, result.InstalledFlavor);
        Assert.Equal(root, SetupPreparation.ResolveDestination(root).Root);
    }

    /// <summary>
    /// Stops at a colliding child rather than nesting repeatedly or accepting unrelated files as installation content.
    /// </summary>
    /// <param name="file">Whether the child name is occupied by a file instead of a folder.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrelatedChildIsPreservedAndRejected(bool file)
    {
        using var workspace = new TestWorkspace();
        var child = workspace.PathFor(SetupPreparation.InstallationDirectoryName);
        var unrelated = file ? child : Path.Combine(child, UnrelatedFile);
        if (!file)
            Directory.CreateDirectory(child);

        File.WriteAllText(unrelated, PersonalContent);
        if (file)
        {
            Assert.Throws<IOException>(() => SetupPreparation.ResolveDestination(workspace.Root));
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => SetupPreparation.ResolveDestination(workspace.Root));
        }

        Assert.Equal(PersonalContent, File.ReadAllText(unrelated));
        Assert.False(Directory.Exists(Path.Combine(child, SetupPreparation.InstallationDirectoryName)));
    }

    /// <summary>
    /// Reuses an empty named child without adding another directory level.
    /// </summary>
    [Fact]
    public void EmptyNamedChildIsReused()
    {
        using var workspace = new TestWorkspace();
        var child = workspace.PathFor(SetupPreparation.InstallationDirectoryName);
        Directory.CreateDirectory(child);
        Assert.Equal(child, SetupPreparation.ResolveDestination(workspace.Root).Root);
        Assert.Empty(Directory.EnumerateFileSystemEntries(child));
    }

    /// <summary>
    /// Rejects a file selected as the parent before considering a child destination.
    /// </summary>
    [Fact]
    public void SelectedFileIsRejected()
    {
        using var workspace = new TestWorkspace();
        var file = workspace.PathFor(UnrelatedFile);
        File.WriteAllText(file, PersonalContent);
        Assert.Throws<IOException>(() => SetupPreparation.ResolveDestination(file));
        Assert.Equal(PersonalContent, File.ReadAllText(file));
    }
}
