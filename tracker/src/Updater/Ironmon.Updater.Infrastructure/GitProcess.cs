using Ironmon.Updater.Core;
using System.Diagnostics;
using System.Text;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Contains bounded process output without logging inherited environment or credentials.
/// </summary>
/// <remarks>
/// Initializes the exit status and captured output of a completed Git command.
/// </remarks>
/// <param name="ExitCode">The exit code.</param>
/// <param name="Output">The standard output.</param>
/// <param name="Error">The standard error.</param>
internal sealed record GitResult(int ExitCode, string Output, string Error);

/// <summary>
/// Runs only an explicitly selected private Git with an isolated environment.
/// </summary>
internal sealed class GitProcess
{
    private readonly string _executable;
    private readonly string _home;
    private readonly TimeProvider _clock;
    private readonly bool _localFixtures;
    private const int MaximumOutput = 8 * 1024 * 1024;
    private const string ConfigOption = "-c";
    private const string NoOptionalLocks = "--no-optional-locks";
    private const string NoReplaceObjects = "--no-replace-objects";
    private const string LocalProtocol = "protocol.file.allow=always";
    private const string WindowsDirectory = "SystemRoot";
    private const string WindowsAlias = "WINDIR";
    private const string PathVariable = "PATH";
    private const string HomeVariable = "HOME";
    private const string ProfileVariable = "USERPROFILE";
    private const string TempVariable = "TEMP";
    private const string TmpVariable = "TMP";
    private const string CommandVariable = "ComSpec";
    private const string CommandExecutable = "cmd.exe";
    private const string GitExecVariable = "GIT_EXEC_PATH";
    private const string CeilingVariable = "GIT_CEILING_DIRECTORIES";
    private const string EnvironmentDefaults = "GIT_CONFIG_NOSYSTEM=1\nGIT_CONFIG_SYSTEM=/dev/null\nGIT_CONFIG_GLOBAL=/dev/null\nGIT_TERMINAL_PROMPT=0\nGCM_INTERACTIVE=never\nGIT_ATTR_NOSYSTEM=1\nGIT_OPTIONAL_LOCKS=0\nGIT_LFS_SKIP_SMUDGE=1\nGIT_NO_LAZY_FETCH=1\nLC_ALL=C";
    private const string Configuration = "core.hooksPath=/dev/null\ncore.fsmonitor=false\ncore.untrackedCache=false\ncore.attributesFile=/dev/null\ncore.askPass=\ncredential.helper=\ncredential.interactive=false\nhttp.sslVerify=true\nhttp.sslBackend=schannel\nhttp.followRedirects=false\nprotocol.allow=never\nprotocol.https.allow=always\nsubmodule.recurse=false\nfetch.recurseSubmodules=false\nfetch.writeCommitGraph=false\nfetch.fsckObjects=true\ntransfer.fsckObjects=true\ngc.auto=0\nmaintenance.auto=false\ncore.longpaths=true";

    /// <summary>
    /// Initializes execution with private paths; file transport is available only to internal fixtures.
    /// </summary>
    /// <param name="executable">The fully qualified path to the verified private Git executable.</param>
    /// <param name="home">The dedicated updater-owned directory used for the child's home and temporary files.</param>
    /// <param name="clock">The timeout clock, or <see langword="null" /> to use the system clock.</param>
    /// <param name="localFixtures">Whether to enable local file transport for trusted, disposable test repositories.</param>
    internal GitProcess(string executable, string home, TimeProvider? clock = null, bool localFixtures = false)
    {
        _executable = PlainPaths.Full(executable);
        _home = PlainPaths.Full(home);
        _clock = clock ?? TimeProvider.System;
        _localFixtures = localFixtures;
        Directory.CreateDirectory(_home);
    }

    /// <summary>
    /// Runs a structured command and drains both pipes under one cancellation/timeout boundary.
    /// </summary>
    /// <remarks>
    /// Failure or cancellation stops only the process tree started for this operation. Each output pipe has a separate size limit.
    /// </remarks>
    /// <param name="workingDirectory">The fully qualified working directory for the child process.</param>
    /// <param name="arguments">The command arguments, passed individually without shell interpolation.</param>
    /// <param name="timeout">The operation deadline, or <see langword="null" /> for two minutes.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task whose result contains the exit code and captured output; a nonzero exit code is returned without throwing.</returns>
    /// <exception cref="TimeoutException">The deadline expires without caller cancellation.</exception>
    /// <exception cref="OperationCanceledException">The operation is cancelled.</exception>
    internal async Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = CreateStartInfo(workingDirectory, arguments);
        using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(2), _clock);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new IOException(UpdaterText.GitProcessThePrivateGitProcessCouldNotStart);

        var output = ReadAsync(process.StandardOutput, null, cancelled);
        var error = ReadAsync(process.StandardError, new GitTransferProgress(), cancelled);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(cancelled.Token), output, error).ConfigureAwait(false);
            return new GitResult(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

            await process.WaitForExitAsync().ConfigureAwait(false);
            if (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                throw new TimeoutException(UpdaterText.GitProcessThePrivateGitOperationTimedOut);

            throw;
        }
    }

    /// <summary>
    /// Creates the explicit executable, arguments and child-only environment.
    /// </summary>
    /// <param name="workingDirectory">The fully qualified directory in which Git will run.</param>
    /// <param name="arguments">The arguments to append after the isolation options.</param>
    /// <returns>Process start information that selects the private executable and replaces the inherited environment.</returns>
    internal ProcessStartInfo CreateStartInfo(string workingDirectory, IReadOnlyList<string> arguments)
    {
        PlainPaths.Full(_executable);
        var directory = PlainPaths.Full(workingDirectory);
        var tool = Path.GetDirectoryName(Path.GetDirectoryName(_executable))!;
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var start = new ProcessStartInfo(_executable)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        start.Environment.Clear();
        start.Environment[WindowsDirectory] = windows;
        start.Environment[WindowsAlias] = windows;
        start.Environment[CommandVariable] = Path.Combine(system, CommandExecutable);
        start.Environment[HomeVariable] = _home;
        start.Environment[ProfileVariable] = _home;
        start.Environment[TempVariable] = _home;
        start.Environment[TmpVariable] = _home;
        start.Environment[PathVariable] = string.Join(Path.PathSeparator, Path.Combine(tool, MinGitPackage.Cmd), Path.Combine(tool, MinGitPackage.MingwBin), Path.Combine(tool, MinGitPackage.UsrBin), system);
        start.Environment[GitExecVariable] = Path.Combine(tool, MinGitPackage.GitCore);
        start.Environment[CeilingVariable] = Path.GetDirectoryName(directory);
        foreach (var setting in EnvironmentDefaults.Split('\n'))
        {
            var pair = setting.Split('=', 2);
            start.Environment[pair[0]] = pair[1];
        }

        start.ArgumentList.Add(NoOptionalLocks);
        start.ArgumentList.Add(NoReplaceObjects);
        foreach (var setting in Configuration.Split('\n'))
        {
            start.ArgumentList.Add(ConfigOption);
            start.ArgumentList.Add(setting);
        }

        if (_localFixtures)
        {
            start.ArgumentList.Add(ConfigOption);
            start.ArgumentList.Add(LocalProtocol);
        }

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        return start;
    }

    /// <summary>
    /// Reads one output pipe within its size limit and signals operation cancellation on failure.
    /// </summary>
    /// <param name="reader">The redirected output reader; ownership remains with the process.</param>
    /// <param name="progress">The optional standard-error measurement parser.</param>
    /// <param name="cancellation">The shared cancellation source observed by both readers and the process wait.</param>
    /// <returns>A task whose result is the captured text when the pipe reaches its end.</returns>
    private static async Task<string> ReadAsync(StreamReader reader, GitTransferProgress? progress, CancellationTokenSource cancellation)
    {
        var buffer = new char[8192];
        var result = new StringBuilder();
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation.Token).ConfigureAwait(false)) > 0)
            {
                if (result.Length + count > MaximumOutput)
                    throw new InvalidDataException(UpdaterText.GitProcessGitOutputExceededTheSupportedSize);

                result.Append(buffer, 0, count);
                progress?.Append(buffer.AsSpan(0, count));
            }

            return result.ToString();
        }
        catch
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }
}
