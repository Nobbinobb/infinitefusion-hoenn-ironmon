using System.Security.Cryptography;
using System.Text;
using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Builds an approved A/B/C history and a separate ZIP-style installation with no Git metadata.
/// </summary>
internal sealed class ZipGameFixture : IDisposable
{
    internal const string TextFile = "Data/game.txt";
    internal const string ObsoleteFile = "obsolete.txt";
    internal const string NewFile = "new.txt";
    internal const string NestedFile = "more/child.txt";
    internal const string FlattenedFile = "flatten";
    internal const string UserFile = "personal-save.txt";
    internal const string ProtectedFile = "Graphics/CustomBattlers/spritesheets/.DS_Store";
    internal const string TrackerPath = "Ironmon Tracker/Ironmon Tracker.exe";
    internal const string IniText = "[Game]\nTitle=infinitefusion-hoenn\nLibrary=RGSS104E.dll\n";
    internal const string TextA = "original game\nsecond line\n";
    internal const string TextB = "updated game\nsecond line\n";
    internal const string ExtraText = "player-owned data";
    private const string ExecutableText = "MZ fixture executable";
    private const string UpstreamFolder = "upstream";
    private const string InstallationFolder = "ZIP game ü";
    private const string StagingFolder = "preparation";
    private const string HomeFolder = "private-home";
    private const string InitialBranch = "--initial-branch=releases";
    private const string EmptyTemplate = "--template=";
    private const string Init = "init";
    private const string Add = "add";
    private const string All = "--all";
    private const string CommitCommand = "commit";
    private const string Message = "-m";
    private const string Config = "-c";
    private const string Author = "user.name=ZIP Fixture";
    private const string Email = "user.email=zip@example.invalid";
    private const string RevParse = "rev-parse";
    private const string Head = "HEAD";
    private const string Tree = "ls-tree";
    private const string Recursive = "-r";
    private const string Null = "-z";
    private const string Tag = "tag";
    private const string Annotate = "-a";
    private const string TagName = "fixture-target";
    private const string CrLf = "\r\n";
    private const string Lf = "\n";
    private readonly TestWorkspace _workspace = new();
    private readonly PinnedGitFixture _tool;

    /// <summary>
    /// Gets the disposable upstream repository.
    /// </summary>
    internal string Upstream => _workspace.PathFor(UpstreamFolder);

    /// <summary>
    /// Gets the installation that starts without .git.
    /// </summary>
    internal string Installation => _workspace.PathFor(InstallationFolder);

    /// <summary>
    /// Gets the private metadata preparation root.
    /// </summary>
    internal string Staging => _workspace.PathFor(StagingFolder);

    /// <summary>
    /// Gets the local-only test upstream policy.
    /// </summary>
    internal RepositoryPolicy Policy => new(Upstream.Replace('\\', '/'), GameFixture.Branch, true);

    /// <summary>
    /// Gets the complete trusted baseline after fixture initialization.
    /// </summary>
    internal GameBaseline Baseline { get; private set; } = null!;

    /// <summary>
    /// Gets the approved middle target revision.
    /// </summary>
    internal string Target { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the isolated Git runner.
    /// </summary>
    internal GitProcess Git { get; }

    /// <summary>
    /// Creates the fixture using an already verified private Git runtime.
    /// </summary>
    /// <param name="tool">The initialized runtime fixture.</param>
    internal ZipGameFixture(PinnedGitFixture tool)
    {
        _tool = tool;
        Git = new GitProcess(tool.Executable, _workspace.PathFor(HomeFolder), localFixtures: true);
    }

    /// <summary>
    /// Constructs a provider with the fixture baseline or explicit malformed test inventories.
    /// </summary>
    /// <param name="baselines">Replacement trusted inputs for negative tests, or null to use the fixture baseline.</param>
    /// <returns>The ZIP adoption provider targeting disposable directories only.</returns>
    internal ZipGameAdopter CreateAdopter(IReadOnlyList<GameBaseline>? baselines = null)
        => new(_tool.Cache, Policy, baselines ?? [Baseline], Staging);

    /// <summary>
    /// Creates canonical ZIP bytes, an approved inventory and two subsequent upstream revisions.
    /// </summary>
    /// <param name="includeProtectedFile">Whether both revisions track a file in the player-owned sprite-sheet directory.</param>
    /// <returns>A task that completes when the installation is at A and the upstream is beyond B.</returns>
    internal async Task InitializeAsync(bool includeProtectedFile = false)
    {
        await RunAsync(_workspace.Root, Init, InitialBranch, EmptyTemplate, Upstream);
        Write(Upstream, GameInstallationLocator.GameIni, IniText);
        Write(Upstream, GameInstallationLocator.GameExecutable, ExecutableText);
        Write(Upstream, TextFile, TextA);
        Write(Upstream, ObsoleteFile, TextA);
        if (includeProtectedFile)
            Write(Upstream, ProtectedFile, TextA);

        var original = await CommitAsync(TextA);
        var rows = await RunAsync(Upstream, Tree, Recursive, Null, original);
        var files = new List<GameBaselineFile>();
        foreach (var row in rows.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = row.Split('\t');
            var identity = fields[0].Split(' ');
            var path = fields[1];
            var bytes = File.ReadAllBytes(Path.Combine(Upstream, path));
            var windows = path == TextFile ? Content(Encoding.UTF8.GetBytes(TextA.Replace(Lf, CrLf))) : null;
            files.Add(new GameBaselineFile(path, identity[0], identity[2], Content(bytes), windows));
            var destination = Path.Combine(Installation, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, bytes);
        }

        Baseline = new GameBaseline(original, files.AsReadOnly());
        Write(Installation, UserFile, ExtraText);
        Write(Installation, TrackerPath, ExecutableText);
        Write(Upstream, TextFile, TextB);
        if (includeProtectedFile)
            Write(Upstream, ProtectedFile, TextB);

        File.Delete(Path.Combine(Upstream, ObsoleteFile));
        Write(Upstream, NewFile, TextB);
        Write(Upstream, NestedFile, TextB);
        Write(Upstream, FlattenedFile, TextB);
        Target = await CommitAsync(TextB);
        Write(Upstream, TextFile, ExtraText);
        await CommitAsync(ExtraText);
    }

    /// <summary>
    /// Writes trusted fixture text after creating its parent directories.
    /// </summary>
    /// <param name="root">The disposable destination root.</param>
    /// <param name="relative">The fixture-controlled relative path.</param>
    /// <param name="text">The exact UTF-8 text to write without a BOM.</param>
    internal static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    /// <summary>
    /// Calculates the exact byte fingerprint used by the trusted fixture inventory.
    /// </summary>
    /// <param name="bytes">The original file bytes.</param>
    /// <returns>The length and SHA-256 digest.</returns>
    internal static GameFileContent Content(byte[] bytes)
        => new(bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));

    /// <summary>
    /// Runs a structured fixture command and asserts its successful exit.
    /// </summary>
    /// <param name="directory">The disposable working directory.</param>
    /// <param name="arguments">The explicit Git command arguments.</param>
    /// <returns>The command output with only trailing line endings removed.</returns>
    internal async Task<string> RunAsync(string directory, params string[] arguments)
    {
        var result = await Git.RunAsync(directory, arguments);
        Assert.True(result.ExitCode == 0, result.Error);
        return result.Output.TrimEnd('\r', '\n');
    }

    /// <summary>
    /// Creates one upstream fixture revision with an isolated author identity.
    /// </summary>
    /// <param name="message">The fixture commit message.</param>
    /// <returns>The newly created commit ID.</returns>
    internal async Task<string> CommitAsync(string message)
    {
        await RunAsync(Upstream, Add, All);
        await RunAsync(Upstream, Config, Author, Config, Email, CommitCommand, Message, message);
        return await RunAsync(Upstream, RevParse, Head);
    }

    /// <summary>
    /// Creates a tag object that points to the approved commit but must not be accepted as its exact identity.
    /// </summary>
    /// <returns>The annotated tag object's ID.</returns>
    internal async Task<string> CreateAnnotatedTargetAsync()
    {
        await RunAsync(Upstream, Config, Author, Config, Email, Tag, Annotate, TagName, Message, TextB, Target);
        return await RunAsync(Upstream, RevParse, TagName);
    }

    /// <summary>
    /// Removes only this fixture's owned directories.
    /// </summary>
    public void Dispose()
        => _workspace.Dispose();
}
