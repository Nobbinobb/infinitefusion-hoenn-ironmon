using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises real process isolation, structured arguments and bounded cancellation.
/// </summary>
/// <remarks>
/// Initializes process tests with the verified private executable.
/// </remarks>
/// <param name="tool">The pinned runtime.</param>
public sealed class GitProcessTests(PinnedGitFixture tool) : IClassFixture<PinnedGitFixture>
{
    private const string HomeName = "home";
    private const string VersionOption = "--version";
    private const string ExpectedVersion = "git version 2.55.0.windows.5";
    private const string ConfigVariable = "GIT_CONFIG_COUNT";
    private const string DirectoryVariable = "GIT_DIR";
    private const string AskPassVariable = "GIT_ASKPASS";
    private const string PathVariable = "PATH";
    private const string GlobalConfigVariable = "GIT_CONFIG_GLOBAL";
    private const string NullPath = "/dev/null";
    private const string ConfigOption = "-c";
    private const string EchoAlias = "alias.argument=rev-parse --sq-quote";
    private const string EchoCommand = "argument";
    private const string SpecialArgument = "two words; $(never-run) & ü";
    private const string WaitAlias = "alias.wait=!while :; do :; done";
    private const string WaitCommand = "wait";
    private const string OverflowAlias = "alias.overflow=!chunk=output; for i in 1 2 3 4 5 6 7 8 9 10; do chunk=$chunk$chunk; done; while :; do printf '%s\\n' \"$chunk\"; done";
    private const string OverflowCommand = "overflow";

    /// <summary>
    /// Runs the pinned executable with no inherited Git, credentials or system Git search path.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task UsesPrivateExecutableAndAnExplicitEnvironment()
    {
        using var workspace = new TestWorkspace();
        var git = new GitProcess(tool.Executable, workspace.PathFor(HomeName));
        var start = git.CreateStartInfo(workspace.Root, [VersionOption]);
        Assert.Equal(tool.Executable, start.FileName);
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
        Assert.False(start.Environment.ContainsKey(ConfigVariable));
        Assert.False(start.Environment.ContainsKey(DirectoryVariable));
        Assert.False(start.Environment.ContainsKey(AskPassVariable));
        Assert.Equal(NullPath, start.Environment[GlobalConfigVariable]);
        var privateRoot = Path.GetDirectoryName(Path.GetDirectoryName(tool.Executable))!;
        Assert.All(start.Environment[PathVariable]!.Split(Path.PathSeparator), entry => Assert.True(entry.StartsWith(privateRoot, StringComparison.OrdinalIgnoreCase) || entry == Environment.GetFolderPath(Environment.SpecialFolder.System)));
        var result = await git.RunAsync(workspace.Root, [VersionOption]);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(ExpectedVersion, result.Output.Trim());
    }

    /// <summary>
    /// Passes metacharacters as one argument instead of executing them as shell code.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PreservesStructuredArguments()
    {
        using var workspace = new TestWorkspace();
        var git = new GitProcess(tool.Executable, workspace.PathFor(HomeName));
        var result = await git.RunAsync(workspace.Root, [ConfigOption, EchoAlias, EchoCommand, SpecialArgument]);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(SpecialArgument, result.Output);
    }

    /// <summary>
    /// Stops only the child operation when a timeout expires.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task TimeoutStopsTheOwnedProcessTree()
    {
        using var workspace = new TestWorkspace();
        var git = new GitProcess(tool.Executable, workspace.PathFor(HomeName));
        await Assert.ThrowsAsync<TimeoutException>(() => git.RunAsync(workspace.Root, [ConfigOption, WaitAlias, WaitCommand], timeout: TimeSpan.FromMilliseconds(500)));
        Assert.Equal(0, (await git.RunAsync(workspace.Root, [VersionOption])).ExitCode);
    }

    /// <summary>
    /// Propagates explicit cancellation separately from timeout.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CancellationStopsTheOwnedProcessTree()
    {
        using var workspace = new TestWorkspace();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var git = new GitProcess(tool.Executable, workspace.PathFor(HomeName));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => git.RunAsync(workspace.Root, [ConfigOption, WaitAlias, WaitCommand], cancellationToken: cancel.Token));
    }

    /// <summary>
    /// Bounds untrusted process output instead of buffering indefinitely.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ExcessiveOutputAbortsTheProcess()
    {
        using var workspace = new TestWorkspace();
        var git = new GitProcess(tool.Executable, workspace.PathFor(HomeName));
        var error = await Record.ExceptionAsync(() => git.RunAsync(workspace.Root, [ConfigOption, OverflowAlias, OverflowCommand], timeout: TimeSpan.FromSeconds(15)));
        Assert.True(error is InvalidDataException or OperationCanceledException, error?.ToString());
    }
}
