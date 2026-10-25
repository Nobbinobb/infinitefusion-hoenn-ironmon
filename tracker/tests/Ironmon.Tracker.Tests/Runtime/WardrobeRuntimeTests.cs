using Ironmon.Tracker.Collection;
using Ironmon.Tracker.Connection.Wardrobe;
using Ironmon.Tracker.Protocol.Wardrobe;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Ironmon.Tracker.Tests.Runtime;

/// <summary>
/// Exercises the real wardrobe session and native renderer against the tracker transport.
/// </summary>
/// <remarks>
/// Initializes the acceptance test with its retained diagnostic output.
/// </remarks>
/// <param name="output">The test diagnostic sink.</param>
public sealed class WardrobeRuntimeTests(ITestOutputHelper output)
{
    private const string _manifest = "src/load_order.json";
    private const string _outputDirectory = "data/wardrobe-runtime";
    private const string _entryPoint = "tests/runtime/Wardrobe-Acceptance.rb";
    private const string _executable = "InfiniteFusion2.exe";
    private const string _gameIni = "Game.ini";
    private const string _dllPattern = "*.dll";
    private const string _configuration = "mkxp.json";
    private const string _gameRootVariable = "IRONMON_TEST_GAME_ROOT";
    private const string _sourceVariable = "IRONMON_SMOKE_SOURCE_ROOT";
    private const string _outputVariable = "IRONMON_SMOKE_OUTPUT_ROOT";
    private const string _portVariable = "IRONMON_WARDROBE_TRACKER_PORT";
    private const string _collectionVariable = "IRONMON_GAMEPLAY_COLLECTION_TEST_MODE";
    private const string _suspended = "suspended";
    private const string _testOrganization = "IronmonRuntimeTests";
    private const string _result = "wardrobe-result.txt";
    private const string _stdout = "stdout.log";
    private const string _stderr = "stderr.log";
    private const string _diagnostics = "tracker-diagnostics.json";
    private const string _idFormat = "N";
    private const string _trackerVersion = "0.9.0";
    private const string _category = "Category";
    private const string _acceptance = "WardrobeAcceptance";
    private const string _passed = "Passed";

    /// <summary>
    /// Verifies game-owned opening, draft edits, permanent purchases, saved profiles and native exit.
    /// </summary>
    /// <returns>Completion after the owned native process and tracker listener have stopped.</returns>
    [Fact]
    [Trait(_category, _acceptance)]
    public async Task GameOnlyWardrobeSupportsNativePreviewsPurchasesAndSavedOutfits()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Wardrobe acceptance requires Infinite Fusion's Windows runtime.");

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
        await using GameplayCollectionStore rewardStore = new(Path.Combine(runRoot, _rewardStoreDirectory));
        await using TrackerConnectionService service = new(options, diagnostics, state, new TrackerRunState(), new TrackerKnowledgeStore(storage), new AreaDiscoveryStore(storage), new CompletedRunArchive(storage), collection: rewardStore);
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
        output.WriteLine($"Wardrobe acceptance evidence: {runRoot}");
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("The owned wardrobe acceptance runtime did not start.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try
        {
            Task drive = DriveWardrobeAsync(service.Requests.Wardrobe, rewardStore, runRoot);
            Task exit = process.WaitForExitAsync();
            Task completed = await Task.WhenAny(drive, exit).WaitAsync(TimeSpan.FromSeconds(90));
            if (completed == drive)
                await drive;
            else if (process.ExitCode == 0)
                await drive.WaitAsync(TimeSpan.FromSeconds(2));

            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
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

    }

    private const string _firstProfile = "11111111111111111111111111111111";
    private const string _secondProfile = "22222222222222222222222222222222";
    private const string _profileName = "Evening";
    private const string _renamedProfile = "Travel";
    private const string _secondName = "Second outfit";
    private const string _disconnectMarker = "disconnect.request";
    private const string _previewPattern = "native-{0}.png";
    private const string _rewardStoreDirectory = "reward-store";
    private const string _rewardPollMarker = "achievement-poll.request";
    private const string _dropRewardAcknowledgementMarker = "skip-reward-ack.request";
    private const string _rewardBatchJson = """
    {"schema_version":1,"profile_id":"11111111111111111111111111111111","epoch_id":"22222222222222222222222222222222","stream_id":"33333333333333333333333333333333","predecessor_stream_id":null,"predecessor_final_sequence":null,"events":[
      {"sequence":"1","type":"battle.started","attempt_id":"44444444444444444444444444444444","branch_id":"55555555555555555555555555555555","battle_id":"66666666666666666666666666666666","occurred_at_us":"1","context":{},"payload":{"battle_kind":"trainer","encounter":{"trainer_class_ids":["RIVAL1"],"authored_entry_ids":[]}}},
      {"sequence":"2","type":"battle.ended","attempt_id":"44444444444444444444444444444444","branch_id":"55555555555555555555555555555555","battle_id":"66666666666666666666666666666666","occurred_at_us":"2","context":{},"payload":{"outcome":"win"}}
    ]}
    """;

    /// <summary>
    /// Drives the real tracker API while the native game owns its paused wardrobe loop.
    /// </summary>
    /// <param name="client">The live negotiated wardrobe client.</param>
    /// <param name="rewardStore">The owned real achievement store supplying future unlock rewards.</param>
    /// <param name="root">The owned evidence directory.</param>
    /// <returns>Completion after all three native wardrobe sessions have been acknowledged.</returns>
    private async Task DriveWardrobeAsync(TrackerWardrobeClient client, GameplayCollectionStore rewardStore, string root)
    {
        await WaitUntilAsync(() => client.Snapshot is { Open: true });
        WardrobeSnapshotPayload first = client.Snapshot!;
        Assert.True(first.InitialSelection);
        Assert.Empty(await rewardStore.GetWardrobeRewardsAsync());
        using (JsonDocument rewardBatch = JsonDocument.Parse(_rewardBatchJson))
            await rewardStore.AcceptBatchAsync(rewardBatch.RootElement);

        IReadOnlyList<Guid> rewards = await rewardStore.GetWardrobeRewardsAsync();
        Assert.NotEmpty(rewards);
        int creditedPoints = first.Points + rewards.Count * 100;
        await File.WriteAllTextAsync(Path.Combine(root, _dropRewardAcknowledgementMarker), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(root, _rewardPollMarker), string.Empty);
        await WaitUntilAsync(() => client.Snapshot?.Points == creditedPoints);
        Assert.Equal(rewards.Count, (await rewardStore.GetWardrobeRewardsAsync()).Count);
        await File.WriteAllTextAsync(Path.Combine(root, _rewardPollMarker), string.Empty);
        using (CancellationTokenSource rewardTimeout = new(TimeSpan.FromSeconds(10)))
        {
            while ((await rewardStore.GetWardrobeRewardsAsync()).Count > 0)
                await Task.Delay(20, rewardTimeout.Token);
        }

        Assert.Equal(creditedPoints, client.Snapshot!.Points);
        first = client.Snapshot;
        output.WriteLine($"Credited {rewards.Count} future achievements once despite a lost acknowledgement.");
        Assert.True(client.Catalog.Count > 100);
        Assert.Null(first.EquippedAppearance.Hat2);
        WardrobeCatalogItemPayload outfit = client.Catalog.First(item => item.Category == WardrobeProtocol.Clothes && item.Available && !first.OwnedKeys.Contains(item.Key));
        WardrobeAppearancePayload free = first.Appearance with { Clothes = outfit.Id, HairColor = 137, Hat = null, Hat2 = null };
        await client.ExecuteAsync(WardrobeAction.Change, free);
        Assert.Equal(first.EquippedAppearance, client.Snapshot!.EquippedAppearance);
        foreach (string mode in new[] { WardrobeProtocol.Portrait, WardrobeProtocol.Front, WardrobeProtocol.Back, WardrobeProtocol.Left, WardrobeProtocol.Right, WardrobeProtocol.Bike })
        {
            Stopwatch timer = Stopwatch.StartNew();
            WardrobePreviewPayload preview = await client.PreviewAsync(mode);
            Assert.Equal(36, preview.DyeColors.Count);
            Assert.NotEmpty(preview.BaseColors);
            Assert.InRange(preview.Width, 1, 512);
            Assert.InRange(preview.Height, 1, 512);
            await File.WriteAllBytesAsync(Path.Combine(root, string.Format(System.Globalization.CultureInfo.InvariantCulture, _previewPattern, mode)), Convert.FromBase64String(preview.PngBase64));
            output.WriteLine($"Native {mode} round trip: {timer.Elapsed.TotalMilliseconds:F1} ms");
        }

        WardrobePreviewPayload head = await client.PreviewAsync(WardrobeProtocol.Portrait, headOnly: true);
        Assert.True(head.Height < (await client.PreviewAsync(WardrobeProtocol.Portrait)).Height);
        await Assert.ThrowsAsync<TrackerProtocolException>(() => client.ExecuteAsync(WardrobeAction.SaveProfile, profileId: _firstProfile, name: _profileName));
        WardrobeSnapshotPayload claimed = await client.ExecuteAsync(WardrobeAction.Confirm);
        Assert.False(claimed.Open);
        Assert.True(claimed.Confirmed);
        Assert.Equal(first.Points, claimed.Points);
        Assert.Contains(outfit.Key, claimed.OwnedKeys);
        await WaitUntilAsync(() => client.Snapshot is { Open: true } next && next.WardrobeId != first.WardrobeId);
        WardrobeSnapshotPayload second = client.Snapshot!;
        Assert.False(second.InitialSelection);
        WardrobeCatalogItemPayload paid = client.Catalog.First(item => item.Category == WardrobeProtocol.Clothes && item.Available && !second.OwnedKeys.Contains(item.Key));
        await client.ExecuteAsync(WardrobeAction.Change, second.Appearance with { Clothes = paid.Id });
        WardrobeSnapshotPayload purchase = await client.ExecuteAsync(WardrobeAction.Purchase, pieceKey: paid.Key);
        Assert.Equal(second.Points - paid.Points, purchase.Points);
        Assert.Equal(purchase.Points, (await client.ExecuteAsync(WardrobeAction.Purchase, pieceKey: paid.Key)).Points);
        await client.ExecuteAsync(WardrobeAction.Cancel);
        await WaitUntilAsync(() => client.Snapshot is { Open: true } next && next.WardrobeId != second.WardrobeId);
        WardrobeSnapshotPayload third = client.Snapshot!;
        Assert.Equal(free, third.EquippedAppearance);
        Assert.Equal(purchase.Points, third.Points);
        Assert.Contains(paid.Key, third.OwnedKeys);
        await client.ExecuteAsync(WardrobeAction.SaveProfile, profileId: _firstProfile, name: _profileName);
        await client.ExecuteAsync(WardrobeAction.RenameProfile, profileId: _firstProfile, name: _renamedProfile);
        WardrobeAppearancePayload changed = third.Appearance with { Clothes = paid.Id, HairColor = 220, SkinTone = 6 };
        await client.ExecuteAsync(WardrobeAction.Change, changed);
        await client.ExecuteAsync(WardrobeAction.ReplaceProfile, profileId: _firstProfile, name: _renamedProfile);
        await client.ExecuteAsync(WardrobeAction.SaveProfile, profileId: _secondProfile, name: _secondName);
        await Assert.ThrowsAsync<TrackerProtocolException>(() => client.ExecuteAsync(WardrobeAction.RenameProfile, profileId: _secondProfile, name: _renamedProfile));
        await client.ExecuteAsync(WardrobeAction.Change, changed with { HairColor = 15, SkinTone = 2 });
        WardrobeSnapshotPayload loaded = await client.ExecuteAsync(WardrobeAction.LoadProfile, profileId: _firstProfile);
        Assert.Equal(220, loaded.Appearance.HairColor);
        Assert.Equal(2, loaded.Appearance.SkinTone);
        await File.WriteAllTextAsync(Path.Combine(root, _disconnectMarker), string.Empty);
        await WaitUntilAsync(() => !client.Connected);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ExecuteAsync(WardrobeAction.Confirm));
        await WaitUntilAsync(() => client.Connected && client.Snapshot is { Open: true });
        WardrobeSnapshotPayload recovered = await client.RefreshAsync();
        Assert.Equal(loaded.Appearance, recovered.Appearance);
        Assert.Equal(loaded.WardrobeId, recovered.WardrobeId);
        await client.ExecuteAsync(WardrobeAction.DeleteProfile, profileId: _secondProfile);
        WardrobeSnapshotPayload equipped = await client.ExecuteAsync(WardrobeAction.Confirm);
        Assert.Equal(loaded.Appearance, equipped.EquippedAppearance);
        Assert.Single(equipped.OutfitProfiles);
        Assert.False(equipped.Open);
    }

    /// <summary>
    /// Waits for a native state transition with a bounded timeout.
    /// </summary>
    /// <param name="condition">The live condition to observe.</param>
    /// <returns>Completion when the condition holds.</returns>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(35));
        while (!condition())
            await Task.Delay(20, timeout.Token);
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
