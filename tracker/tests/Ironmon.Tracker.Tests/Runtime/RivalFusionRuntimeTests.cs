using System.Diagnostics;
using Xunit.Abstractions;

namespace Ironmon.Tracker.Tests.Runtime;

/// <summary>
/// Measures the real bundled-game rival request and verifies native animation completion against a live tracker worker.
/// </summary>
/// <remarks>
/// Initializes the acceptance test with its retained diagnostic output.
/// </remarks>
/// <param name="output">The test diagnostic sink.</param>
public sealed class RivalFusionRuntimeTests(ITestOutputHelper output)
{
    private const string _manifest = "src/load_order.json";
    private const string _outputDirectory = "data/rival-fusion-runtime";
    private const string _entryPoint = "tests/runtime/Starter-Rival-Acceptance.rb";
    private const string _executable = "InfiniteFusion2.exe";
    private const string _gameIni = "Game.ini";
    private const string _dllPattern = "*.dll";
    private const string _configuration = "mkxp.json";
    private const string _gameRootVariable = "IRONMON_TEST_GAME_ROOT";
    private const string _sourceVariable = "IRONMON_SMOKE_SOURCE_ROOT";
    private const string _outputVariable = "IRONMON_SMOKE_OUTPUT_ROOT";
    private const string _portVariable = "IRONMON_RIVAL_TRACKER_PORT";
    private const string _collectionVariable = "IRONMON_GAMEPLAY_COLLECTION_TEST_MODE";
    private const string _suspended = "suspended";
    private const string _testOrganization = "IronmonRuntimeTests";
    private const string _result = "starter-rival-result.txt";
    private const string _workerTiming = "fusion-worker-timing.txt";
    private const string _animationTiming = "fusion-animation-timing.txt";
    private const string _stdout = "stdout.log";
    private const string _stderr = "stderr.log";
    private const string _diagnostics = "tracker-diagnostics.json";
    private const string _idFormat = "N";
    private const string _trackerVersion = "0.9.0";
    private const string _category = "Category";
    private const string _acceptance = "RivalAcceptance";
    private const string _passed = "Passed";

    /// <summary>
    /// Verifies subsecond cold and warm game requests and that fusion returns to a completed lab exit.
    /// </summary>
    /// <returns>Completion after the owned native process and tracker listener have stopped.</returns>
    [Fact]
    [Trait(_category, _acceptance)]
    public async Task RivalFusionRoundTripAndAnimationCompleteInBundledGame()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Rival acceptance requires Infinite Fusion's Windows runtime.");

        string sourceRoot = FindSourceRoot();
        string gameRoot = Environment.GetEnvironmentVariable(_gameRootVariable) ?? Directory.GetParent(sourceRoot)!.FullName;
        string runRoot = Path.Combine(sourceRoot, _outputDirectory, Guid.NewGuid().ToString(_idFormat));
        using NativeRuntimeFixture runtime = new(sourceRoot);
        string engineRoot = runtime.DirectoryPath;
        Directory.CreateDirectory(runRoot);
        foreach (string path in Directory.EnumerateFiles(gameRoot, _dllPattern).Concat([Path.Combine(gameRoot, _executable), Path.Combine(gameRoot, _gameIni)]))
            File.Copy(path, Path.Combine(engineRoot, Path.GetFileName(path)));

        string configuration = JsonSerializer.Serialize(new { gameFolder = gameRoot, customScript = Path.Combine(sourceRoot, _entryPoint), vsync = false, frameSkip = false, dataPathOrg = _testOrganization, dataPathApp = Path.GetFileName(runRoot) });
        await File.WriteAllTextAsync(Path.Combine(engineRoot, _configuration), configuration);
        TrackerKnowledgeOptions storage = new(runRoot);
        TrackerDiagnosticsStore diagnostics = new();
        TrackerConnectionState state = new();
        TrackerConnectionOptions options = new(0, _trackerVersion, false, TimeSpan.FromSeconds(5));
        await using TrackerConnectionService service = new(options, diagnostics, state, new TrackerRunState(), new TrackerKnowledgeStore(storage), new AreaDiscoveryStore(storage), new CompletedRunArchive(storage));
        service.Start();
        Assert.True(service.BoundPort > 0);
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
        start.Environment[_portVariable] = service.BoundPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment[_collectionVariable] = _suspended;
        output.WriteLine($"Rival acceptance evidence: {runRoot}");
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("The owned rival acceptance runtime did not start.");
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
                await process.WaitForExitAsync();
            }

            string standardOutput = await stdout;
            string standardError = await stderr;
            await File.WriteAllTextAsync(Path.Combine(runRoot, _stdout), standardOutput);
            await File.WriteAllTextAsync(Path.Combine(runRoot, _stderr), standardError);
            output.WriteLine(standardOutput);
            output.WriteLine(standardError);
            await File.WriteAllTextAsync(Path.Combine(runRoot, _diagnostics), JsonSerializer.Serialize(diagnostics.Entries));
        }

        string result = File.Exists(Path.Combine(runRoot, _result)) ? await File.ReadAllTextAsync(Path.Combine(runRoot, _result)) : string.Empty;
        output.WriteLine(result);
        Assert.True(process.ExitCode == 0, result);
        Assert.StartsWith(_passed, result);
        output.WriteLine(await File.ReadAllTextAsync(Path.Combine(runRoot, _workerTiming)));
        output.WriteLine(await File.ReadAllTextAsync(Path.Combine(runRoot, _animationTiming)));
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
