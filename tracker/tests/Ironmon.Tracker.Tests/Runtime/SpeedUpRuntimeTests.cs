using System.Diagnostics;
using Xunit.Abstractions;

namespace Ironmon.Tracker.Tests.Runtime;

/// <summary>
/// Exercises speed pacing, configuration and input safety using Infinite Fusion's bundled engine.
/// </summary>
/// <remarks>
/// Initializes the acceptance test with its retained diagnostic output.
/// </remarks>
/// <param name="output">The test diagnostic sink.</param>
public sealed class SpeedUpRuntimeTests(ITestOutputHelper output)
{
    private const string _manifest = "src/load_order.json";
    private const string _outputDirectory = "data/speed-up-runtime";
    private const string _entryPoint = "tests/runtime/Speed-Up-Acceptance.rb";
    private const string _executable = "InfiniteFusion2.exe";
    private const string _gameIni = "Game.ini";
    private const string _dllPattern = "*.dll";
    private const string _configuration = "mkxp.json";
    private const string _gameRootVariable = "IRONMON_TEST_GAME_ROOT";
    private const string _sourceVariable = "IRONMON_SMOKE_SOURCE_ROOT";
    private const string _outputVariable = "IRONMON_SMOKE_OUTPUT_ROOT";
    private const string _collectionVariable = "IRONMON_GAMEPLAY_COLLECTION_TEST_MODE";
    private const string _vsyncVariable = "IRONMON_SPEED_UP_VSYNC";
    private const string _enabled = "1";
    private const string _suspended = "suspended";
    private const string _testOrganization = "IronmonRuntimeTests";
    private const string _result = "speed-up-acceptance.json";
    private const string _stdout = "stdout.log";
    private const string _stderr = "stderr.log";
    private const string _idFormat = "N";
    private const string _category = "Category";
    private const string _acceptance = "SpeedUpAcceptance";
    private const string _status = "status";
    private const string _cases = "cases";
    private const string _passed = "passed";

    /// <summary>
    /// Runs the canonical Ruby speed-up regressions with isolated save data and no live tracker connection.
    /// </summary>
    /// <returns>Completion after the owned native process exits and all requested Ruby groups pass.</returns>
    [Fact]
    [Trait(_category, _acceptance)]
    public async Task CurrentSourcePassesBundledSpeedUpAcceptance()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Speed-up acceptance requires Infinite Fusion's Windows runtime.");

        string sourceRoot = FindSourceRoot();
        string gameRoot = Environment.GetEnvironmentVariable(_gameRootVariable) ?? Directory.GetParent(sourceRoot)!.FullName;
        string runRoot = Path.Combine(sourceRoot, _outputDirectory, Guid.NewGuid().ToString(_idFormat));
        using NativeRuntimeFixture runtime = new(sourceRoot);
        string engineRoot = runtime.DirectoryPath;
        Directory.CreateDirectory(runRoot);
        foreach (string path in Directory.EnumerateFiles(gameRoot, _dllPattern).Concat([Path.Combine(gameRoot, _executable), Path.Combine(gameRoot, _gameIni)]))
            File.Copy(path, Path.Combine(engineRoot, Path.GetFileName(path)));

        bool vsync = Environment.GetEnvironmentVariable(_vsyncVariable) == _enabled;
        string configuration = JsonSerializer.Serialize(new { gameFolder = gameRoot, customScript = Path.Combine(sourceRoot, _entryPoint), vsync, frameSkip = false, dataPathOrg = _testOrganization, dataPathApp = Path.GetFileName(runRoot) });
        await File.WriteAllTextAsync(Path.Combine(engineRoot, _configuration), configuration);
        ProcessStartInfo start = new(Path.Combine(engineRoot, _executable))
        {
            WorkingDirectory = engineRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Environment[_sourceVariable] = sourceRoot;
        start.Environment[_outputVariable] = runRoot;
        start.Environment[_collectionVariable] = _suspended;
        output.WriteLine($"Speed-up acceptance evidence: {runRoot}");
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("The owned speed-up acceptance runtime did not start.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(10));
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
            await File.WriteAllTextAsync(Path.Combine(runRoot, _stdout), standardOutput);
            await File.WriteAllTextAsync(Path.Combine(runRoot, _stderr), standardError);
            output.WriteLine(standardOutput);
            output.WriteLine(standardError);
        }

        string resultPath = Path.Combine(runRoot, _result);
        string result = File.Exists(resultPath) ? await File.ReadAllTextAsync(resultPath) : string.Empty;
        output.WriteLine(result);
        Assert.True(process.ExitCode == 0, $"Bundled speed-up acceptance exited with code {process.ExitCode}. {result}");
        Assert.True(result.Length > 0, "The bundled engine exited without a speed-up acceptance result.");
        using JsonDocument document = JsonDocument.Parse(result);
        Assert.Equal(_passed, document.RootElement.GetProperty(_status).GetString());
        JsonElement cases = document.RootElement.GetProperty(_cases);
        Assert.NotEmpty(cases.EnumerateArray());
        Assert.All(cases.EnumerateArray(), item => Assert.Equal(_passed, item.GetProperty(_status).GetString()));
    }

    /// <summary>
    /// Locates the canonical source checkout from the built test assembly.
    /// </summary>
    /// <returns>The ancestor containing the source manifest.</returns>
    private static string FindSourceRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, _manifest)))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("The Ironmon source checkout was not found.");
    }
}
