using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace Ironmon.Tracker.Tests.Runtime;

/// <summary>
/// Executes the native Ruby smoke entry point using a disposable copy of Infinite Fusion's bundled runtime.
/// </summary>
/// <remarks>
/// Initializes the test with xUnit's output sink for retained game diagnostics.
/// </remarks>
/// <param name="output">The current test's diagnostic output sink.</param>
public sealed class BundledRubySmokeTests(ITestOutputHelper output)
{
    private const string _category = "Category";
    private const string _bundledRuby = "BundledRuby";
    private const string _gameRootVariable = "IRONMON_TEST_GAME_ROOT";
    private const string _sourceRootVariable = "IRONMON_SMOKE_SOURCE_ROOT";
    private const string _outputRootVariable = "IRONMON_SMOKE_OUTPUT_ROOT";
    private const string _collectionModeVariable = "IRONMON_GAMEPLAY_COLLECTION_TEST_MODE";
    private const string _suspended = "suspended";
    private const string _manifestPath = "src/load_order.json";
    private const string _entryPath = "tests/runtime/Quick.rb";
    private const string _outputPath = "data/ruby-smoke";
    private const string _dataDirectory = "Data";
    private const string _scriptDirectory = "Data/Scripts";
    private const string _fontDirectory = "Fonts";
    private const string _windowskinDirectory = "Graphics/Windowskins";
    private const string _executableName = "InfiniteFusion2.exe";
    private const string _gameIni = "Game.ini";
    private const string _configurationName = "mkxp.json";
    private const string _gameName = "Game";
    private const string _currentDirectory = ".";
    private const string _testOrganization = "IronmonRuntimeTests";
    private const string _resultName = "result.json";
    private const string _stdoutName = "stdout.log";
    private const string _stderrName = "stderr.log";
    private const string _statusProperty = "status";
    private const string _casesProperty = "cases";
    private const string _passed = "passed";
    private const string _dllPattern = "*.dll";
    private const string _allFiles = "*";
    private const string _mapPattern = @"^Map\d+\.rxdata$";
    private const string _datExtension = ".dat";
    private const string _jsonExtension = ".json";
    private const string _rxdataExtension = ".rxdata";
    private const string _modPrefix = "997";
    private const string _developmentPrefix = "998";
    private const string _mainPrefix = "999";
    private const string _idFormat = "N";
    private const string _rubyExtension = ".rb";
    private const string _unixLineEnding = "\n";
    private const string _windowsLineEnding = "\r\n";

    /// <summary>
    /// Loads current source and runs the small Ruby suite without generating catalogs or accessing player saves.
    /// </summary>
    /// <param name="lineEnding">The LF archive or CRLF Windows checkout representation of the native scripts.</param>
    /// <returns>Completion after the owned native process exits and every Ruby smoke group has passed.</returns>
    [Theory]
    [InlineData(_unixLineEnding)]
    [InlineData(_windowsLineEnding)]
    [Trait(_category, _bundledRuby)]
    public async Task CurrentSourcePassesBundledRubySmoke(string lineEnding)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Bundled Ruby validation requires the Windows Infinite Fusion runtime.");

        string sourceRoot = FindSourceRoot();
        string gameRoot = Environment.GetEnvironmentVariable(_gameRootVariable) ?? Directory.GetParent(sourceRoot)!.FullName;
        string executable = Path.Combine(gameRoot, _executableName);
        Assert.True(File.Exists(executable), $"Set {_gameRootVariable} to an Infinite Fusion installation containing {_executableName}.");
        string runRoot = Path.Combine(sourceRoot, _outputPath, Guid.NewGuid().ToString(_idFormat));
        NativeRuntimeFixture runtime = new(sourceRoot);
        string fixtureRoot = runtime.DirectoryPath;
        Directory.CreateDirectory(runRoot);
        output.WriteLine($"Ruby smoke evidence: {runRoot}");

        try
        {
            CopyRuntime(gameRoot, fixtureRoot, lineEnding);
            string configuration = JsonSerializer.Serialize(new { gameFolder = _currentDirectory, customScript = Path.Combine(sourceRoot, _entryPath), execName = _gameName, vsync = false, frameSkip = false, dataPathOrg = _testOrganization, dataPathApp = Path.GetFileName(runRoot) });
            await File.WriteAllTextAsync(Path.Combine(fixtureRoot, _configurationName), configuration);
            ProcessStartInfo start = new(Path.Combine(fixtureRoot, _executableName))
            {
                WorkingDirectory = fixtureRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            start.Environment[_sourceRootVariable] = sourceRoot;
            start.Environment[_outputRootVariable] = runRoot;
            start.Environment[_collectionModeVariable] = _suspended;
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("The owned Ruby runtime could not start.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(120));
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }

                string standardOutput = await stdout;
                string standardError = await stderr;
                await File.WriteAllTextAsync(Path.Combine(runRoot, _stdoutName), standardOutput);
                await File.WriteAllTextAsync(Path.Combine(runRoot, _stderrName), standardError);
                output.WriteLine(standardOutput);
                output.WriteLine(standardError);
            }

            string resultPath = Path.Combine(runRoot, _resultName);
            string result = File.Exists(resultPath) ? await File.ReadAllTextAsync(resultPath) : string.Empty;
            output.WriteLine(result);
            Assert.True(process.ExitCode == 0, $"Bundled Ruby exited with code {process.ExitCode}. {result}");
            Assert.True(result.Length > 0, "The bundled runtime exited without a Ruby smoke result.");
            using JsonDocument document = JsonDocument.Parse(result);
            Assert.Equal(_passed, document.RootElement.GetProperty(_statusProperty).GetString());
            JsonElement cases = document.RootElement.GetProperty(_casesProperty);
            Assert.Equal(7, cases.GetArrayLength());
            Assert.All(cases.EnumerateArray(), item => Assert.Equal(_passed, item.GetProperty(_statusProperty).GetString()));
        }
        finally
        {
            runtime.Dispose();
        }
    }

    /// <summary>
    /// Locates the source checkout independently of the test binary's normal or Quick output layout.
    /// </summary>
    /// <returns>The ancestor containing the canonical Ironmon source manifest.</returns>
    private static string FindSourceRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, _manifestPath)))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("The canonical Ironmon source checkout could not be located.");
    }

    /// <summary>
    /// Copies the native engine and compiled base data, excluding maps and installed mod scripts.
    /// </summary>
    /// <param name="source">The selected upstream game installation.</param>
    /// <param name="destination">The owned fixture game directory.</param>
    /// <param name="lineEnding">The native script line-ending representation to exercise.</param>
    private static void CopyRuntime(string source, string destination, string lineEnding)
    {
        string[] requiredFiles = [_executableName, _gameIni];
        foreach (string name in requiredFiles)
            File.Copy(Path.Combine(source, name), Path.Combine(destination, name));

        foreach (string path in Directory.EnumerateFiles(source, _dllPattern))
            File.Copy(path, Path.Combine(destination, Path.GetFileName(path)));

        string data = Path.Combine(destination, _dataDirectory);
        Directory.CreateDirectory(data);
        foreach (string path in Directory.EnumerateFiles(Path.Combine(source, _dataDirectory)))
        {
            if (Path.GetExtension(path) is not (_datExtension or _jsonExtension or _rxdataExtension) || Regex.IsMatch(Path.GetFileName(path), _mapPattern))
                continue;

            File.Copy(path, Path.Combine(data, Path.GetFileName(path)));
        }

        CopyTree(Path.Combine(source, _scriptDirectory), Path.Combine(destination, _scriptDirectory), excludeInstalledMod: true, lineEnding: lineEnding);
        CopyTree(Path.Combine(source, _fontDirectory), Path.Combine(destination, _fontDirectory), excludeInstalledMod: false);
        CopyTree(Path.Combine(source, _windowskinDirectory), Path.Combine(destination, _windowskinDirectory), excludeInstalledMod: false);
    }

    /// <summary>
    /// Copies a required runtime directory without following filesystem links or including installed mod entry points.
    /// </summary>
    /// <param name="source">The source directory.</param>
    /// <param name="destination">Its location in the disposable game.</param>
    /// <param name="excludeInstalledMod">Whether to exclude the installed mod, development, and game Main folders.</param>
    /// <param name="lineEnding">The native script line-ending representation, or null to preserve file bytes.</param>
    private static void CopyTree(string source, string destination, bool excludeInstalledMod, string? lineEnding = null)
    {
        EnumerationOptions options = new() { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (string path in Directory.EnumerateFiles(source, _allFiles, options))
        {
            string relative = Path.GetRelativePath(source, path);
            if (excludeInstalledMod && (relative.StartsWith(_modPrefix, StringComparison.Ordinal) || relative.StartsWith(_developmentPrefix, StringComparison.Ordinal) || relative.StartsWith(_mainPrefix, StringComparison.Ordinal)))
                continue;

            string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (lineEnding is not null && Path.GetExtension(path) == _rubyExtension)
            {
                string text = Encoding.UTF8.GetString(File.ReadAllBytes(path));
                File.WriteAllBytes(target, Encoding.UTF8.GetBytes(text.Replace(_windowsLineEnding, _unixLineEnding, StringComparison.Ordinal).Replace(_unixLineEnding, lineEnding, StringComparison.Ordinal)));
            }
            else
            {
                File.Copy(path, target);
            }
        }
    }
}
