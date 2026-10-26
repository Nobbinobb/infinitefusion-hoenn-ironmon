using System.Diagnostics;
using System.Text.Json;
using Ironmon.Tracker.Collection;
using Ironmon.Tracker.Collection.Achievements;
using Ironmon.Tracker.Collection.Reduction;
using Xunit.Abstractions;

namespace Ironmon.Tracker.Tests.Runtime;

/// <summary>
/// Checks every current scripted main-rival and Wally encounter through the bundled collector and tracker.
/// </summary>
/// <remarks>
/// Initializes the test with its retained diagnostic sink.
/// </remarks>
/// <param name="output">The retained native acceptance diagnostics.</param>
public sealed class RivalAchievementRuntimeTests(ITestOutputHelper output)
{
    private const string _manifest = "src/load_order.json";
    private const string _outputDirectory = "data/rival-achievement-runtime";
    private const string _entryPoint = "tests/runtime/Rival-Achievements.rb";
    private const string _executable = "InfiniteFusion2.exe";
    private const string _gameIni = "Game.ini";
    private const string _dllPattern = "*.dll";
    private const string _configuration = "mkxp.json";
    private const string _gameRootVariable = "IRONMON_TEST_GAME_ROOT";
    private const string _sourceVariable = "IRONMON_SMOKE_SOURCE_ROOT";
    private const string _outputVariable = "IRONMON_SMOKE_OUTPUT_ROOT";
    private const string _collectionVariable = "IRONMON_GAMEPLAY_COLLECTION_TEST_MODE";
    private const string _suspended = "suspended";
    private const string _testOrganization = "IronmonRuntimeTests";
    private const string _result = "rival-achievements.json";
    private const string _stdout = "stdout.log";
    private const string _stderr = "stderr.log";
    private const string _idFormat = "N";
    private const string _category = "Category";
    private const string _gameCatalog = "GameCatalog";
    private const string _acceptance = "RivalAchievements";
    private const string _passed = "passed";
    private const string _status = "status";
    private const string _cases = "cases";
    private const string _kind = "kind";
    private const string _rival = "rival";
    private const string _wally = "wally";
    private const string _started = "started";
    private const string _ended = "ended";
    private const string _startEvent = "battle.started";
    private const string _endEvent = "battle.ended";
    private const string _profile = "11111111111111111111111111111111";
    private const string _epoch = "22222222222222222222222222222222";
    private const string _stream = "33333333333333333333333333333333";
    private const string _attempt = "55555555555555555555555555555555";
    private const string _branch = "66666666666666666666666666666666";
    private const string _known = "known";
    private const string _audioDriver = "ALSOFT_DRIVERS";
    private const string _nullDriver = "null";

    /// <summary>
    /// Replays captured victories with different display names for every current rival and Wally map entry.
    /// </summary>
    /// <returns>Completion after native identity checks and actual tracker persistence pass.</returns>
    [Fact]
    [Trait(_category, _acceptance)]
    [Trait(_category, _gameCatalog)]
    public async Task EveryScriptedRivalAndWallyIdentityFeedsItsOwnWinPopulation()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Rival achievement acceptance requires Infinite Fusion's Windows runtime.");

        string sourceRoot = FindSourceRoot();
        string gameRoot = Environment.GetEnvironmentVariable(_gameRootVariable) ?? Directory.GetParent(sourceRoot)!.FullName;
        string runRoot = Path.Combine(sourceRoot, _outputDirectory, Guid.NewGuid().ToString(_idFormat));
        Directory.CreateDirectory(runRoot);
        using NativeRuntimeFixture runtime = new(sourceRoot);
        foreach (string path in Directory.EnumerateFiles(gameRoot, _dllPattern).Concat([Path.Combine(gameRoot, _executable), Path.Combine(gameRoot, _gameIni)]))
            File.Copy(path, Path.Combine(runtime.DirectoryPath, Path.GetFileName(path)));

        await File.WriteAllTextAsync(Path.Combine(runtime.DirectoryPath, _configuration), JsonSerializer.Serialize(new { gameFolder = gameRoot, customScript = Path.Combine(sourceRoot, _entryPoint), vsync = false, frameSkip = false, dataPathOrg = _testOrganization, dataPathApp = Path.GetFileName(runRoot) }));
        ProcessStartInfo start = new(Path.Combine(runtime.DirectoryPath, _executable)) { WorkingDirectory = runtime.DirectoryPath, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment[_sourceVariable] = sourceRoot;
        start.Environment[_outputVariable] = runRoot;
        start.Environment[_collectionVariable] = _suspended;
        start.Environment[_audioDriver] = _nullDriver;
        output.WriteLine(runRoot);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("The owned rival-achievement runtime did not start.");
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

            await File.WriteAllTextAsync(Path.Combine(runRoot, _stdout), await stdout);
            await File.WriteAllTextAsync(Path.Combine(runRoot, _stderr), await stderr);
        }

        string text = await File.ReadAllTextAsync(Path.Combine(runRoot, _result));
        output.WriteLine(text);
        Assert.Equal(0, process.ExitCode);
        using JsonDocument result = JsonDocument.Parse(text);
        Assert.Equal(_passed, result.RootElement.GetProperty(_status).GetString());
        JsonElement[] cases = [.. result.RootElement.GetProperty(_cases).EnumerateArray()];
        Assert.Contains(cases, item => item.GetProperty(_kind).GetString() == _rival);
        Assert.Contains(cases, item => item.GetProperty(_kind).GetString() == _wally);
        AchievementPack pack = new();
        HashSet<Guid> all = [.. pack.Nodes.Select(node => node.Id)];
        AchievementRule wally = pack.Nodes.Select(node => pack.Open(node.Id, all)).Single(rule => rule.Number == 41);
        foreach (JsonElement encounter in cases)
        {
            bool mainRival = encounter.GetProperty(_kind).GetString() == _rival;
            JsonElement[] events = [.. Enumerable.Range(0, 5).SelectMany(index => new[] { Occurrence(_startEvent, encounter.GetProperty(_started), index * 2 + 1, index), Occurrence(_endEvent, encounter.GetProperty(_ended), index * 2 + 2, index) })];
            await using GameplayCollectionStore store = new(Path.Combine(runRoot, Guid.NewGuid().ToString(_idFormat)));
            JsonElement batch = JsonSerializer.SerializeToElement(new { schema_version = 1, profile_id = _profile, epoch_id = _epoch, stream_id = _stream, predecessor_stream_id = (string?)null, predecessor_final_sequence = (string?)null, events });
            await store.AcceptBatchAsync(batch);
            var earned = await store.GetAchievementsAsync();
            Assert.Contains(earned, item => item.Number == 45);
            Assert.Equal(mainRival, earned.Any(item => item.Number == 31));
            CollectionEventReducer reducer = new(CollectionCatalogue.LoadDefault());
            List<AchievementEvidence> evidence = [];
            foreach (JsonElement source in events)
            {
                foreach (CollectionDirectProjection row in reducer.ReduceDirect(source).Where(row => row.GroupId == wally.Group))
                {
                    AchievementEvidence value = new();
                    foreach (CollectionDimensionValue dimension in row.Dimensions.Where(item => item.Coverage == _known))
                        value.Dimensions[dimension.Key] = dimension.Value;

                    foreach (CollectionMeasureUpdate measure in row.Measures.Where(item => item.Coverage == _known && item.Scalar.HasValue))
                        value.Scalars[measure.Key] = measure.Scalar!.Value;

                    evidence.Add(value);
                }
            }

            Assert.Equal(!mainRival, AchievementEvaluator.Satisfied(wally, evidence));
        }
    }

    /// <summary>
    /// Assigns contiguous delivery identities to one unmodified native event payload.
    /// </summary>
    /// <param name="type">The actual captured source event type.</param>
    /// <param name="payload">The unmodified native collector payload.</param>
    /// <param name="sequence">The contiguous delivery sequence.</param>
    /// <param name="battle">The independent replay battle number.</param>
    /// <returns>The immutable tracker wire envelope.</returns>
    private static JsonElement Occurrence(string type, JsonElement payload, int sequence, int battle)
        => JsonSerializer.SerializeToElement(new { sequence = sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), type, attempt_id = _attempt, branch_id = _branch, battle_id = battle.ToString(System.Globalization.CultureInfo.InvariantCulture), occurred_at_us = sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), context = new { }, payload });

    /// <summary>
    /// Locates the checkout from the normal test output directory.
    /// </summary>
    /// <returns>The ancestor containing the canonical source manifest.</returns>
    private static string FindSourceRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, _manifest)))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("The canonical Ironmon source checkout could not be located.");
    }
}
