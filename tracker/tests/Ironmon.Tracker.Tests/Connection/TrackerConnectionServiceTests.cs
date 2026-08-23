using System.Net;
using System.Net.Sockets;

namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies persistent loopback connection, handshake, recovery, and error behavior.
/// </summary>
public sealed class TrackerConnectionServiceTests
{
    /// <summary>
    /// Initializes the tracker connection service tests.
    /// </summary>
    public TrackerConnectionServiceTests()
    {
    }

    /// <summary>
    /// Verifies the duplex handshake and current-state recovery request.
    /// </summary>
    [Fact]
    public async Task ServiceCompletesHandshakeAndRecoversCurrentState()
    {
        TrackerConnectionState state = new();
        TrackerDiagnosticsStore diagnostics = new();
        TrackerRunState runState = new();
        TrackerKnowledgeStore knowledge = CreateKnowledgeStore();
        AreaDiscoveryStore areaDiscoveries = CreateAreaDiscoveryStore();
        CompletedRunArchive completedRuns = CreateCompletedRunArchive();
        TrackerConnectionOptions options = new(0, "0.1.0", true, TimeSpan.FromSeconds(2));
        options.AutoSelectStarter = true;
        options.FavoriteSpeciesIds = ["BULBASAUR:0"];
        await using TrackerConnectionService service = new(options, diagnostics, state, runState, knowledge, areaDiscoveries, completedRuns);
        service.Start();

        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, service.BoundPort);
        NetworkStream stream = client.GetStream();
        using TrackerMessageReader reader = new(stream, leaveOpen: true);
        await using TrackerMessageWriter writer = new(stream, leaveOpen: true);

        GameHandshakePayload game = new("6.8.0", "0.3.3", true, true, @"C:\Game", null, null);
        TrackerMessage gameHandshake = TrackerMessageFactory.CreateEvent("game_connected", 0, game);
        await writer.WriteAsync(gameHandshake);

        TrackerMessage? trackerHandshake = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        TrackerMessage? currentStateRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("tracker_connected", trackerHandshake?.Event);
        TrackerHandshakePayload trackerPayload = TrackerJson.DeserializePayload<TrackerHandshakePayload>(trackerHandshake!.Payload);
        Assert.True(trackerPayload.AutoSelectStarter);
        Assert.Equal("BULBASAUR:0", Assert.Single(trackerPayload.FavoriteSpeciesIds));
        Assert.Equal("current_state", currentStateRequest?.Command);
        string requestId = Assert.IsType<string>(currentStateRequest?.RequestId);

        await WaitForSnapshotAsync(state, snapshot => snapshot.Game is not null);
        Task<DebugRunDiagnosticsSnapshot> reconnectDiagnosticsTask = service.Requests.GetDebugRunDiagnosticsAsync();
        TrackerMessage? reconnectDiagnosticsRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("debug_run_diagnostics", reconnectDiagnosticsRequest?.Command);
        Assert.Null(reconnectDiagnosticsRequest?.RunId);
        DebugRunDiagnosticsSnapshot reconnectDiagnostics = new()
        {
            Runtime = CreateRuntimeDiagnostics(),
            Configuration = CreateConfiguration(),
            SpeciesGenerator = new SpeciesGeneratorRecipePayload { Version = 1, PoolFingerprint = "species" },
            AbilityGenerator = new AbilityGeneratorRecipePayload { Version = 3, PoolSize = 10, PoolFingerprint = "abilities" },
            PlayerFusionGenerator = new PlayerFusionGeneratorRecipePayload { Version = 2, PoolSize = 10, PoolFingerprint = "fusions" },
            Mappings = new DebugMappingDiagnosticsSnapshot()
        };

        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(reconnectDiagnosticsRequest!.RequestId!, reconnectDiagnostics));
        Assert.Equal("mixed", (await reconnectDiagnosticsTask).Configuration!.WildPolicy);

        Task<DebugRunDiagnosticsSnapshot> rejectedDiagnosticsTask = service.Requests.GetDebugRunDiagnosticsAsync();
        TrackerMessage? rejectedDiagnosticsRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        TrackerProtocolError rejectedError = new("debug_failure", "The debug diagnostic request failed.");
        await writer.WriteAsync(TrackerMessageFactory.CreateErrorResponse(rejectedDiagnosticsRequest!.RequestId!, rejectedError));
        TrackerProtocolException rejectedException = await Assert.ThrowsAsync<TrackerProtocolException>(() => rejectedDiagnosticsTask);
        Assert.Equal(rejectedError.Code, rejectedException.ErrorCode);
        Assert.Equal(rejectedError.Message, rejectedException.Message);
        Assert.Equal($"TrackerProtocolException: {rejectedError.Message}", diagnostics.LastProtocolError);

        Task<DebugPokemonInspectorSnapshot> staleInspectionTask = service.Requests.InspectPokemonAsync(new DebugPokemonInspectionRequestPayload { Target = DebugPokemonTarget.Player });
        TrackerMessage? staleInspectionRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        TrackerProtocolError staleInspectionError = new(TrackerErrorCodes.PokemonNotFound, "The requested Pokemon is not available for inspection.");
        await writer.WriteAsync(TrackerMessageFactory.CreateErrorResponse(staleInspectionRequest!.RequestId!, staleInspectionError));
        TrackerProtocolException staleInspectionException = await Assert.ThrowsAsync<TrackerProtocolException>(() => staleInspectionTask);
        Assert.Equal(TrackerErrorCodes.PokemonNotFound, staleInspectionException.ErrorCode);
        Assert.Equal($"TrackerProtocolException: {staleInspectionError.Message}", diagnostics.LastProtocolError);

        GameCurrentStatePayload currentState = new(true, "run-1", null, 7);
        TrackerMessage response = TrackerMessageFactory.CreateResponse(requestId, currentState, "run-1");
        await writer.WriteAsync(response);

        TrackerConnectionSnapshot connected = await WaitForSnapshotAsync(state, snapshot => snapshot.CurrentState is not null);
        Assert.Equal(TrackerConnectionStatus.Connected, connected.Status);
        Assert.Equal("6.8.0", connected.Game?.GameVersion);
        Assert.Equal(7, connected.CurrentState?.Sequence);
        Assert.True(service.DebugAuthorized);
        Assert.Contains(diagnostics.Entries, entry => entry.Direction == TrackerDiagnosticDirection.Incoming && entry.Name == "game_connected");
        Assert.Contains(diagnostics.Entries, entry => entry.Direction == TrackerDiagnosticDirection.Outgoing && entry.Name == "current_state");

        Task<AreaLookupSummaryResponsePayload> areaSummaryTask = service.Requests.GetAreaSummariesAsync(AreaContentCategory.Trainer);
        TrackerMessage? areaSummaryRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(TrackerCommands.AreaLookupSummary, areaSummaryRequest?.Command);
        AreaLookupSummaryRequestPayload areaSummaryPayload = TrackerJson.DeserializePayload<AreaLookupSummaryRequestPayload>(areaSummaryRequest!.Payload);
        Assert.Null(areaSummaryPayload.Recipe);
        Assert.Equal(AreaContentCategory.Trainer, areaSummaryPayload.Category);
        AreaLookupSummaryResponsePayload areaSummaryResponse = new()
        {
            Revision = 0,
            Areas =
            [
                new AreaSummaryPayload
                {
                    AreaId = "area:4",
                    Name = "Route 1",
                    MapIds = [4],
                    TrainerTotal = 2,
                    TrainerDefeated = 1
                }
            ]
        };

        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(areaSummaryRequest.RequestId!, areaSummaryResponse, "run-1"));
        AreaLookupSummaryResponsePayload receivedAreaSummary = await areaSummaryTask;
        Assert.Equal("Route 1", Assert.Single(receivedAreaSummary.Areas).Name);
        Assert.Equal(1, Assert.Single(receivedAreaSummary.Areas).TrainerDefeated);
        Assert.Same(receivedAreaSummary, await service.Requests.GetAreaSummariesAsync(AreaContentCategory.Trainer));

        Task<AreaLookupDetailResponsePayload> areaDetailTask = service.Requests.GetAreaDetailsAsync("area:4", AreaContentCategory.Trainer);
        TrackerMessage? areaDetailRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(TrackerCommands.AreaLookupDetail, areaDetailRequest?.Command);
        AreaLookupDetailRequestPayload areaDetailPayload = TrackerJson.DeserializePayload<AreaLookupDetailRequestPayload>(areaDetailRequest!.Payload);
        Assert.Equal("area:4", areaDetailPayload.AreaId);
        Assert.Equal(AreaContentCategory.Trainer, areaDetailPayload.Category);
        Assert.Null(areaDetailPayload.Recipe);
        AreaLookupDetailResponsePayload areaDetailResponse = new()
        {
            AreaId = "area:4",
            Name = "Route 1",
            Category = AreaContentCategory.Trainer,
            Trainers =
            [
                new AreaTrainerEntryPayload
                {
                    EntryId = "trainer:4:8",
                    MapId = 4,
                    TrainerType = "Youngster",
                    TrainerName = "Ben",
                    PartySize = 2
                }
            ]
        };

        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(areaDetailRequest.RequestId!, areaDetailResponse, "run-1"));
        AreaLookupDetailResponsePayload receivedAreaDetail = await areaDetailTask;
        Assert.Equal("Ben", Assert.Single(receivedAreaDetail.Trainers).TrainerName);
        Assert.Same(receivedAreaDetail, await service.Requests.GetAreaDetailsAsync("area:4", AreaContentCategory.Trainer));

        AreaDiscoveryPackagePayload discovery = new()
        {
            PackageId = "discovery-1",
            AreaId = "area:4",
            Category = AreaContentCategory.Trainer,
            EntryKeys = ["trainer:4:8"]
        };

        await writer.WriteAsync(TrackerMessageFactory.CreateEvent(TrackerEvents.AreaDiscovery, 1, discovery, "run-1"));
        TrackerMessage? acknowledgment = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(TrackerEvents.AreaDiscoveryAcknowledged, acknowledgment?.Event);
        Assert.Equal("discovery-1", TrackerJson.DeserializePayload<AreaDiscoveryAcknowledgmentPayload>(acknowledgment!.Payload).PackageId);
        Assert.Equal("trainer:4:8", Assert.Single(areaDiscoveries.GetKeys("run-1", "area:4", AreaContentCategory.Trainer)));

        await writer.WriteAsync(TrackerMessageFactory.CreateEvent(TrackerEvents.AreaDiscovery, 2, discovery, "run-1"));
        TrackerMessage? duplicateAcknowledgment = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(TrackerEvents.AreaDiscoveryAcknowledged, duplicateAcknowledgment?.Event);
        Assert.Equal("discovery-1", TrackerJson.DeserializePayload<AreaDiscoveryAcknowledgmentPayload>(duplicateAcknowledgment!.Payload).PackageId);
        Assert.Equal(1, areaDiscoveries.GetRevision("run-1"));

        Task<AreaLookupSummaryResponsePayload> refreshedAreaSummaryTask = service.Requests.GetAreaSummariesAsync(AreaContentCategory.Trainer);
        TrackerMessage? refreshedAreaSummaryRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(TrackerCommands.AreaLookupSummary, refreshedAreaSummaryRequest?.Command);
        AreaLookupSummaryResponsePayload refreshedAreaSummaryResponse = new()
        {
            Revision = 1,
            Areas =
            [
                new AreaSummaryPayload
                {
                    AreaId = "area:4",
                    Name = "Route 1",
                    MapIds = [4],
                    TrainerTotal = 2,
                    TrainerDefeated = 2
                }
            ]
        };

        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(refreshedAreaSummaryRequest!.RequestId!, refreshedAreaSummaryResponse, "run-1"));
        Assert.Equal(2, Assert.Single((await refreshedAreaSummaryTask).Areas).TrainerDefeated);

        TrackerSettingsPayload changedSettings = new() { AutoSelectStarter = false, FavoriteSpeciesIds = ["SQUIRTLE:0"] };
        Task<TrackerSettingsPayload> settingsTask = service.Requests.UpdateSettingsAsync(changedSettings);
        TrackerMessage? settingsRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(TrackerCommands.UpdateSettings, settingsRequest?.Command);
        TrackerSettingsPayload requestedSettings = TrackerJson.DeserializePayload<TrackerSettingsPayload>(settingsRequest!.Payload);
        Assert.False(requestedSettings.AutoSelectStarter);
        Assert.Equal("SQUIRTLE:0", Assert.Single(requestedSettings.FavoriteSpeciesIds));
        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(settingsRequest.RequestId!, requestedSettings, "run-1"));
        Assert.False((await settingsTask).AutoSelectStarter);
        Assert.False(options.AutoSelectStarter);
        Assert.Equal("SQUIRTLE:0", Assert.Single(options.FavoriteSpeciesIds));

        Task<ResetRunResponsePayload> resetTask = service.Requests.ResetRunAsync();
        TrackerMessage? resetRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(TrackerCommands.ResetRun, resetRequest?.Command);
        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(resetRequest!.RequestId!, new ResetRunResponsePayload { Accepted = true }, "run-1"));
        Assert.True((await resetTask).Accepted);

        Task<SeededRunExportPayload> exportTask = service.Requests.ExportSeededRunAsync();
        TrackerMessage? exportRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(TrackerCommands.ExportSeededRun, exportRequest?.Command);
        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(exportRequest!.RequestId!, CreateRecipe("run-1"), "run-1"));
        SeededRunExportPayload exportedRun = await exportTask;
        Assert.Equal(12345, exportedRun.Seed);
        Assert.Equal("species", exportedRun.SpeciesGenerator.PoolFingerprint);

        SeededRunImportRequestPayload importPayload = new()
        {
            TokenId = "seed-token-42",
            Seed = 42,
            GameVersion = "6.8.0",
            IronmonVersion = "0.7.7",
            DataMode = "classic",
            Configuration = new RunConfigurationPayload
            {
                SchemaVersion = 3,
                WildPolicy = "mixed",
                TrainerPolicy = "normal_only",
                UnfusionSetting = "player_choice",
                AutomaticReset = false
            },
            CompatibilityFingerprint = new string('a', 64)
        };

        Task<SeededRunImportStatusPayload> importTask = service.Requests.ImportSeededRunAsync(importPayload);
        TrackerMessage? importRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(TrackerCommands.ImportSeededRun, importRequest?.Command);
        Assert.Equal(importPayload.TokenId, TrackerJson.DeserializePayload<SeededRunImportRequestPayload>(importRequest!.Payload).TokenId);
        SeededRunImportStatusPayload acceptedImport = new()
        {
            TokenId = importPayload.TokenId,
            Status = SeededRunImportStatus.Accepted,
            Message = "Accepted."
        };

        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(importRequest.RequestId!, acceptedImport, "run-1"));
        Assert.Equal(SeededRunImportStatus.Accepted, (await importTask).Status);

        TaskCompletionSource<SeededRunImportStatusPayload> importStatusSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Requests.SeededRunImportStatusChanged += status => importStatusSource.TrySetResult(status);
        SeededRunImportStatusPayload queuedImport = new()
        {
            TokenId = importPayload.TokenId,
            Status = SeededRunImportStatus.Queued,
            Message = "Queued."
        };

        await writer.WriteAsync(TrackerMessageFactory.CreateEvent(TrackerEvents.SeededRunImportStatus, 3, queuedImport, "run-1"));
        SeededRunImportStatusPayload publishedImport = await importStatusSource.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(importPayload.TokenId, publishedImport.TokenId);
        Assert.Equal(SeededRunImportStatus.Queued, publishedImport.Status);

        Task<PokemonSearchResponsePayload> favoriteSearchTask = service.Requests.SearchFavoritePokemonAsync("squirt");
        TrackerMessage? favoriteSearchRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(TrackerCommands.FavoritePokemonSearch, favoriteSearchRequest?.Command);
        DebugPokemonSearchRequestPayload favoriteSearchPayload = TrackerJson.DeserializePayload<DebugPokemonSearchRequestPayload>(favoriteSearchRequest!.Payload);
        Assert.True(favoriteSearchPayload.NormalOnly);
        PokemonSearchResponsePayload favoriteSearchResponse = new()
        {
            Matches = [new PokemonSearchMatch { SpeciesId = "SQUIRTLE:0", SpeciesName = "Squirtle" }],
            Total = 1
        };

        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(favoriteSearchRequest.RequestId!, favoriteSearchResponse, "run-1"));
        Assert.Equal("SQUIRTLE:0", Assert.Single((await favoriteSearchTask).Matches).SpeciesId);

        CompletedRunRecipePayload recipe = CreateRecipe("run-1");
        Task<AreaLookupSummaryResponsePayload> archivedAreaSummaryTask = service.Requests.GetAreaSummariesAsync(AreaContentCategory.Trainer, recipe);
        TrackerMessage? archivedAreaSummaryRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(TrackerCommands.AreaLookupSummary, archivedAreaSummaryRequest?.Command);
        AreaLookupSummaryRequestPayload archivedAreaSummaryPayload = TrackerJson.DeserializePayload<AreaLookupSummaryRequestPayload>(archivedAreaSummaryRequest!.Payload);
        Assert.Equal("run-1", archivedAreaSummaryPayload.Recipe?.RunId);
        AreaLookupSummaryResponsePayload archivedAreaSummaryResponse = new()
        {
            Revision = 1,
            Areas =
            [
                new AreaSummaryPayload
                {
                    AreaId = "area:4",
                    Name = "Archived Route 1",
                    MapIds = [4],
                    TrainerTotal = 2,
                    TrainerDefeated = 1
                }
            ]
        };

        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(archivedAreaSummaryRequest.RequestId!, archivedAreaSummaryResponse, "run-1"));
        Assert.Equal("Archived Route 1", Assert.Single((await archivedAreaSummaryTask).Areas).Name);
        Assert.Equal(2, Assert.Single((await service.Requests.GetAreaSummariesAsync(AreaContentCategory.Trainer)).Areas).TrainerDefeated);

        Task<AreaLookupDetailResponsePayload> activeRevisionDetailTask = service.Requests.GetAreaDetailsAsync("area:4", AreaContentCategory.Trainer);
        TrackerMessage? activeRevisionDetailRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        AreaLookupDetailResponsePayload activeRevisionDetailResponse = new()
        {
            AreaId = "area:4",
            Name = "Active Route 1",
            Category = AreaContentCategory.Trainer,
            Revision = 1,
            Trainers =
            [
                new AreaTrainerEntryPayload
                {
                    EntryId = "trainer:4:8",
                    MapId = 4,
                    TrainerType = "Youngster",
                    TrainerName = "Active Ben",
                    PartySize = 2
                }
            ]
        };

        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(activeRevisionDetailRequest!.RequestId!, activeRevisionDetailResponse, "run-1"));
        Assert.Equal("Active Ben", Assert.Single((await activeRevisionDetailTask).Trainers).TrainerName);

        Task<AreaLookupDetailResponsePayload> archivedAreaDetailTask = service.Requests.GetAreaDetailsAsync("area:4", AreaContentCategory.Trainer, recipe);
        TrackerMessage? archivedAreaDetailRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        AreaLookupDetailResponsePayload archivedAreaDetailResponse = new()
        {
            AreaId = "area:4",
            Name = "Archived Route 1",
            Category = AreaContentCategory.Trainer,
            Revision = 1,
            Trainers =
            [
                new AreaTrainerEntryPayload
                {
                    EntryId = "trainer:4:8",
                    MapId = 4,
                    TrainerType = "Youngster",
                    TrainerName = "Archived Ben",
                    PartySize = 2
                }
            ]
        };

        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(archivedAreaDetailRequest!.RequestId!, archivedAreaDetailResponse, "run-1"));
        Assert.Equal("Archived Ben", Assert.Single((await archivedAreaDetailTask).Trainers).TrainerName);
        Assert.Equal("Active Ben", Assert.Single((await service.Requests.GetAreaDetailsAsync("area:4", AreaContentCategory.Trainer)).Trainers).TrainerName);

        TrackerMessage runCompleted = TrackerMessageFactory.CreateEvent("run_completed", 1, recipe, "run-1");
        await writer.WriteAsync(runCompleted);
        await WaitForRecipeAsync(completedRuns, "run-1");
        Assert.Equal("run-1", Assert.Single(completedRuns.Recipes).RunId);

        Task<PokemonSearchResponsePayload> searchTask = service.Requests.SearchPokemonAsync(recipe, "char");
        TrackerMessage? searchRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("pokemon_search", searchRequest?.Command);
        PokemonSearchRequestPayload searchPayload = TrackerJson.DeserializePayload<PokemonSearchRequestPayload>(searchRequest!.Payload);
        Assert.Equal("char", searchPayload.Query);
        Assert.Equal(0, searchPayload.Offset);
        Assert.Equal(20, searchPayload.Limit);
        Assert.False(searchPayload.NormalOnly);
        PokemonSearchResponsePayload searchResponse = new()
        {
            Matches = [new PokemonSearchMatch { SpeciesId = "CHARMANDER:0", SpeciesName = "Charmander" }],
            Total = 1
        };

        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(searchRequest.RequestId!, searchResponse, "run-1"));
        PokemonSearchResponsePayload receivedSearch = await searchTask;
        Assert.Equal("CHARMANDER:0", Assert.Single(receivedSearch.Matches).SpeciesId);
        Assert.Same(receivedSearch, await service.Requests.SearchPokemonAsync(recipe, "char"));

        Task<PokemonLookupSnapshot> lookupTask = service.Requests.LookupPokemonAsync(recipe, "CHARMANDER:0");
        TrackerMessage? lookupRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("pokemon_lookup", lookupRequest?.Command);
        PokemonLookupRequestPayload lookupPayload = TrackerJson.DeserializePayload<PokemonLookupRequestPayload>(lookupRequest!.Payload);
        Assert.Equal("CHARMANDER:0", lookupPayload.SpeciesId);
        Assert.Equal(100, lookupPayload.Level);
        Assert.Equal(PokemonLookupSection.Overview, lookupPayload.Section);
        PokemonLookupSnapshot lookupResponse = new()
        {
            Identity = new PokemonLookupIdentitySnapshot { SpeciesId = "CHARMANDER:0", SpeciesName = "Charmander", Types = ["FIRE"] },
            Overview = new PokemonLookupOverviewSnapshot
            {
                WildOccurrences = new WildOccurrenceSearchResponsePayload
                {
                    Matches =
                    [
                        new WildPokemonOccurrenceSnapshot
                        {
                            MapId = 4,
                            RouteName = "Route 1",
                            Mode = "Classic",
                            EncounterType = "Land",
                            Slot = 2,
                            MinimumLevel = 4,
                            MaximumLevel = 6,
                            SourceSpeciesId = "RATTATA:0",
                            SourceSpeciesName = "Rattata",
                            ChancePercent = 20m
                        }
                    ],
                    Total = 1
                },
                TrainerOccurrences = new TrainerOccurrenceSearchResponsePayload
                {
                    Matches =
                    [
                        new TrainerPokemonOccurrenceSnapshot
                        {
                            TrainerId = "YOUNGSTER_BEN_0",
                            TrainerName = "Ben",
                            TrainerType = "Youngster",
                            Slot = 1,
                            Level = 7,
                            MapId = 4,
                            RouteName = "Route 1",
                            SourceSpeciesId = "PIDGEY:0",
                            SourceSpeciesName = "Pidgey"
                        }
                    ],
                    Total = 1
                }
            },
            Stats = new PokemonLookupStatsSnapshot
            {
                Generated = new BaseStatsSnapshot { Hp = 39, Attack = 52, Defense = 43, SpecialAttack = 60, SpecialDefense = 50, Speed = 65 },
                GeneratedTotal = 309
            },
            Abilities = new PokemonLookupAbilitiesSnapshot
            {
                Slots =
                [
                    new DebugAbilitySlotSnapshot
                    {
                        Group = DebugAbilitySlotGroup.Generated,
                        Kind = DebugAbilitySlotKind.Normal,
                        Index = 0,
                        AbilityId = "BLAZE",
                        AbilityName = "Blaze",
                        OriginalAbilityId = "BLAZE",
                        OriginalAbilityName = "Blaze",
                        Eligibility = DebugAbilityEligibility.Universal
                    }
                ],
                Generator = new GeneratorDiagnosticsSnapshot
                {
                    Enabled = true,
                    Entries =
                    [
                        new GeneratorDiagnosticEntrySnapshot { Key = "schema", Value = "3" },
                        new GeneratorDiagnosticEntrySnapshot { Key = "pool_fingerprint", Value = "abilities" }
                    ]
                }
            },
            Evolutions = new PokemonLookupEvolutionsSnapshot
            {
                CurrentStageLevel = 1,
                NativeTargets =
                [
                    new PokemonRelationSnapshot { SpeciesId = "CHARMELEON:0", SpeciesName = "Charmeleon", Label = "Level 16" }
                ],
                GeneratedTargets =
                [
                    new EvolutionTargetSnapshot
                    {
                        SpeciesId = "PYUKUMUKU:0",
                        SpeciesName = "Pyukumuku",
                        SpritePath = "Graphics/Battlers/pyukumuku.png",
                        BaseStatTotal = 410,
                        StageLevel = 2,
                        ComponentSide = EvolutionCandidateSide.Head,
                        EffectiveMethods = ["Level 25", "Moon Stone"]
                    }
                ],
                GeneratedPredecessors = []
            }
        };

        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(lookupRequest.RequestId!, lookupResponse, "run-1"));
        PokemonLookupSnapshot receivedLookup = await lookupTask;
        Assert.Equal(309, receivedLookup.Stats!.GeneratedTotal);
        DebugAbilitySlotSnapshot receivedAbilitySlot = Assert.Single(receivedLookup.Abilities!.Slots);
        Assert.Equal(DebugAbilitySlotGroup.Generated, receivedAbilitySlot.Group);
        Assert.Equal("BLAZE", receivedAbilitySlot.AbilityId);
        Assert.Equal(DebugAbilityEligibility.Universal, receivedAbilitySlot.Eligibility);
        Assert.Equal("3", receivedLookup.Abilities.Generator.Entries[0].Value);
        Assert.Equal("abilities", receivedLookup.Abilities.Generator.Entries[1].Value);
        WildPokemonOccurrenceSnapshot receivedWild = Assert.Single(receivedLookup.Overview!.WildOccurrences.Matches);
        Assert.Equal("Route 1", receivedWild.RouteName);
        Assert.Equal(4, receivedWild.MinimumLevel);
        Assert.Equal(6, receivedWild.MaximumLevel);
        TrainerPokemonOccurrenceSnapshot receivedTrainer = Assert.Single(receivedLookup.Overview.TrainerOccurrences.Matches);
        Assert.Equal("Ben", receivedTrainer.TrainerName);
        Assert.Equal(7, receivedTrainer.Level);
        Assert.Equal("Route 1", receivedTrainer.RouteName);
        EvolutionTargetSnapshot receivedTarget = Assert.Single(receivedLookup.Evolutions!.GeneratedTargets);
        Assert.Equal(1, receivedLookup.Evolutions.CurrentStageLevel);
        Assert.Equal("PYUKUMUKU:0", receivedTarget.SpeciesId);
        Assert.Equal(410, receivedTarget.BaseStatTotal);
        Assert.Equal(2, receivedTarget.StageLevel);
        Assert.Equal(EvolutionCandidateSide.Head, receivedTarget.ComponentSide);
        Assert.Equal(["Level 25", "Moon Stone"], receivedTarget.EffectiveMethods);
        Assert.Empty(receivedLookup.Evolutions.GeneratedPredecessors);
        Assert.Same(receivedLookup, await service.Requests.LookupPokemonAsync(recipe, "CHARMANDER:0"));

        Task<EvolutionCandidateSearchResponsePayload> candidateTask = service.Requests.SearchEvolutionCandidatesAsync(recipe, "CHARMANDER:0", EvolutionCandidateSide.Normal, "saur");
        TrackerMessage? candidateRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("evolution_candidate_search", candidateRequest?.Command);
        EvolutionCandidateSearchRequestPayload candidatePayload = TrackerJson.DeserializePayload<EvolutionCandidateSearchRequestPayload>(candidateRequest!.Payload);
        Assert.Equal("CHARMANDER:0", candidatePayload.SpeciesId);
        Assert.Equal(EvolutionCandidateSide.Normal, candidatePayload.Side);
        Assert.Equal("saur", candidatePayload.Query);
        Assert.Equal(50, candidatePayload.Limit);
        EvolutionCandidateSearchResponsePayload candidateResponse = new()
        {
            Matches = [new EvolutionCandidateSnapshot { SpeciesId = "BULBASAUR:0", SpeciesName = "Bulbasaur", BaseStatTotal = 318 }],
            Total = 1
        };

        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(candidateRequest.RequestId!, candidateResponse, "run-1"));
        EvolutionCandidateSearchResponsePayload receivedCandidates = await candidateTask;
        Assert.Equal("BULBASAUR:0", Assert.Single(receivedCandidates.Matches).SpeciesId);
        Assert.Same(receivedCandidates, await service.Requests.SearchEvolutionCandidatesAsync(recipe, "CHARMANDER:0", EvolutionCandidateSide.Normal, "saur"));

        Task<EvolutionPredecessorSearchResponsePayload> predecessorTask = service.Requests.SearchEvolutionPredecessorsAsync(recipe, "CHARMANDER:0");
        TrackerMessage? predecessorRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(TrackerCommands.EvolutionPredecessorSearch, predecessorRequest?.Command);
        EvolutionPredecessorSearchRequestPayload predecessorPayload = TrackerJson.DeserializePayload<EvolutionPredecessorSearchRequestPayload>(predecessorRequest!.Payload);
        Assert.Equal("CHARMANDER:0", predecessorPayload.SpeciesId);
        Assert.Equal(0, predecessorPayload.Offset);
        Assert.Equal(TrackerProtocol.EvolutionPredecessorPageSize, predecessorPayload.Limit);
        EvolutionPredecessorSearchResponsePayload predecessorResponse = new()
        {
            Matches = [new EvolutionTargetSnapshot { SpeciesId = "CYNDAQUIL:0", SpeciesName = "Cyndaquil", BaseStatTotal = 309, StageLevel = 1, ComponentSide = EvolutionCandidateSide.Body, EffectiveMethods = ["Level 16"] }],
            Offset = 0,
            Limit = TrackerProtocol.EvolutionPredecessorPageSize,
            Continuation = EvolutionPredecessorContinuation.Unknown,
            NextOffset = TrackerProtocol.EvolutionPredecessorPageSize
        };

        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(predecessorRequest.RequestId!, predecessorResponse, "run-1"));
        EvolutionPredecessorSearchResponsePayload receivedPredecessors = await predecessorTask;
        Assert.Equal(EvolutionPredecessorContinuation.Unknown, receivedPredecessors.Continuation);
        EvolutionTargetSnapshot receivedPredecessor = Assert.Single(receivedPredecessors.Matches);
        Assert.Equal("CYNDAQUIL:0", receivedPredecessor.SpeciesId);
        Assert.Equal(1, receivedPredecessor.StageLevel);
        Assert.Equal(EvolutionCandidateSide.Body, receivedPredecessor.ComponentSide);
        Assert.Same(receivedPredecessors, await service.Requests.SearchEvolutionPredecessorsAsync(recipe, "CHARMANDER:0"));

        Task<FusionMaterialSearchResponsePayload> materialTask = service.Requests.SearchFusionMaterialsAsync(recipe, "B445H175:0", 50);
        TrackerMessage? materialRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("fusion_material_search", materialRequest?.Command);
        FusionMaterialSearchRequestPayload materialPayload = TrackerJson.DeserializePayload<FusionMaterialSearchRequestPayload>(materialRequest!.Payload);
        Assert.Equal("B445H175:0", materialPayload.SpeciesId);
        Assert.Equal(50, materialPayload.Offset);
        Assert.Equal(10, materialPayload.Limit);
        FusionMaterialSearchResponsePayload materialResponse = new()
        {
            Matches =
            [
                new FusionMaterialPairSnapshot
                {
                    Body = new PokemonRelationSnapshot { SpeciesId = "MUDKIP:0", SpeciesName = "Mudkip", Label = "Body material" },
                    Head = new PokemonRelationSnapshot { SpeciesId = "TOGEPI:0", SpeciesName = "Togepi", Label = "Head material" }
                }
            ],
            Total = 5_000
        };

        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(materialRequest.RequestId!, materialResponse, "run-1"));
        FusionMaterialSearchResponsePayload receivedMaterials = await materialTask;
        Assert.Equal(5_000, receivedMaterials.Total);
        Assert.Equal("MUDKIP:0", Assert.Single(receivedMaterials.Matches).Body.SpeciesId);
        Assert.Same(receivedMaterials, await service.Requests.SearchFusionMaterialsAsync(recipe, "B445H175:0", 50));

        Task<WildOccurrenceSearchResponsePayload> wildOccurrenceTask = service.Requests.SearchWildOccurrencesAsync(recipe, "B310H310:0", 50);
        TrackerMessage? wildOccurrenceRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("wild_occurrence_search", wildOccurrenceRequest?.Command);
        WildOccurrenceSearchRequestPayload wildOccurrencePayload = TrackerJson.DeserializePayload<WildOccurrenceSearchRequestPayload>(wildOccurrenceRequest!.Payload);
        Assert.Equal(50, wildOccurrencePayload.Offset);
        Assert.Equal(TrackerProtocol.OccurrencePageSize, wildOccurrencePayload.Limit);
        WildOccurrenceSearchResponsePayload wildOccurrenceResponse = new() { Matches = lookupResponse.Overview!.WildOccurrences.Matches, Total = 12_000 };
        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(wildOccurrenceRequest.RequestId!, wildOccurrenceResponse, "run-1"));
        Assert.Equal(12_000, (await wildOccurrenceTask).Total);

        Task<TrainerOccurrenceSearchResponsePayload> trainerOccurrenceTask = service.Requests.SearchTrainerOccurrencesAsync(recipe, "B310H310:0", 100);
        TrackerMessage? trainerOccurrenceRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("trainer_occurrence_search", trainerOccurrenceRequest?.Command);
        TrainerOccurrenceSearchRequestPayload trainerOccurrencePayload = TrackerJson.DeserializePayload<TrainerOccurrenceSearchRequestPayload>(trainerOccurrenceRequest!.Payload);
        Assert.Equal(100, trainerOccurrencePayload.Offset);
        Assert.Equal(TrackerProtocol.OccurrencePageSize, trainerOccurrencePayload.Limit);
        TrainerOccurrenceSearchResponsePayload trainerOccurrenceResponse = new() { Matches = lookupResponse.Overview.TrainerOccurrences.Matches, Total = 2_000 };
        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(trainerOccurrenceRequest.RequestId!, trainerOccurrenceResponse, "run-1"));
        Assert.Equal(2_000, (await trainerOccurrenceTask).Total);

        Task<FusionPreviewResponsePayload> fusionTask = service.Requests.PreviewFusionAsync(recipe, "CHARMANDER:0", "ALTARIA:0");
        TrackerMessage? fusionRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("fusion_preview", fusionRequest?.Command);
        FusionPreviewRequestPayload fusionPayload = TrackerJson.DeserializePayload<FusionPreviewRequestPayload>(fusionRequest!.Payload);
        Assert.Equal("CHARMANDER:0", fusionPayload.FirstSpeciesId);
        Assert.Equal("ALTARIA:0", fusionPayload.SecondSpeciesId);
        PokemonRelationSnapshot body = new() { SpeciesId = "CHARMANDER:0", SpeciesName = "Charmander", Label = "Body material" };
        PokemonRelationSnapshot head = new() { SpeciesId = "ALTARIA:0", SpeciesName = "Altaria", Label = "Head material" };
        PokemonRelationSnapshot result = new() { SpeciesId = "B6H334:0", SpeciesName = "Charia", Label = "Ironmon result" };
        FusionPreviewResponsePayload fusionResponse = new() { Outcomes = [new FusionOutcomeSnapshot { Body = body, Head = head, Result = result }] };
        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(fusionRequest.RequestId!, fusionResponse, "run-1"));
        FusionPreviewResponsePayload receivedFusion = await fusionTask;
        Assert.Equal("B6H334:0", Assert.Single(receivedFusion.Outcomes).Result.SpeciesId);
        Assert.Same(receivedFusion, await service.Requests.PreviewFusionAsync(recipe, "CHARMANDER:0", "ALTARIA:0"));

        DebugPokemonInspectionRequestPayload inspectPayload = new() { Target = DebugPokemonTarget.Player };
        Task<DebugPokemonInspectorSnapshot> inspectTask = service.Requests.InspectPokemonAsync(inspectPayload);
        TrackerMessage? inspectRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("debug_inspect_pokemon", inspectRequest?.Command);
        DebugPokemonInspectionRequestPayload receivedInspectPayload = TrackerJson.DeserializePayload<DebugPokemonInspectionRequestPayload>(inspectRequest!.Payload);
        Assert.Equal(DebugPokemonTarget.Player, receivedInspectPayload.Target);
        Assert.Equal(PokemonLookupSection.Overview, receivedInspectPayload.Section);
        DebugPokemonInspectorSnapshot inspectResponse = new()
        {
            Identity = new DebugPokemonIdentitySnapshot
            {
                PokemonId = "1234",
                Nickname = "Charmander",
                SpeciesId = "CHARMANDER:0",
                SpeciesName = "Charmander",
                Level = 5,
                Gender = "male",
                ActiveAbilitySlot = "Normal 0",
                ActiveAbilityId = "BLAZE",
                ActiveAbilityName = "Blaze"
            }
        };

        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(inspectRequest.RequestId!, inspectResponse, "run-1"));
        Assert.Equal("BLAZE", (await inspectTask).Identity.ActiveAbilityId);

        Task<DebugRunDiagnosticsSnapshot> diagnosticsTask = service.Requests.GetDebugRunDiagnosticsAsync();
        TrackerMessage? diagnosticsRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("debug_run_diagnostics", diagnosticsRequest?.Command);
        DebugRunDiagnosticsSnapshot diagnosticsResponse = new()
        {
            Runtime = CreateRuntimeDiagnostics(),
            Configuration = CreateConfiguration(),
            SpeciesGenerator = new SpeciesGeneratorRecipePayload { Version = 1, PoolFingerprint = "species" },
            AbilityGenerator = new AbilityGeneratorRecipePayload { Version = 3, PoolSize = 10, PoolFingerprint = "abilities" },
            PlayerFusionGenerator = new PlayerFusionGeneratorRecipePayload { Version = 2, PoolSize = 10, PoolFingerprint = "fusions" },
            Mappings = new DebugMappingDiagnosticsSnapshot()
        };

        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(diagnosticsRequest!.RequestId!, diagnosticsResponse, "run-1"));
        Assert.Equal("mixed", (await diagnosticsTask).Configuration!.WildPolicy);

        Task<PokemonSearchResponsePayload> debugSearchTask = service.Requests.SearchDebugPokemonAsync("char");
        TrackerMessage? debugSearchRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("debug_pokemon_search", debugSearchRequest?.Command);
        DebugPokemonSearchRequestPayload debugSearchPayload = TrackerJson.DeserializePayload<DebugPokemonSearchRequestPayload>(debugSearchRequest!.Payload);
        Assert.Equal("char", debugSearchPayload.Query);
        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(debugSearchRequest.RequestId!, searchResponse, "run-1"));
        Assert.Equal("CHARMANDER:0", Assert.Single((await debugSearchTask).Matches).SpeciesId);

        Task<PokemonLookupSnapshot> debugLookupTask = service.Requests.LookupDebugPokemonAsync("CHARMANDER:0");
        TrackerMessage? debugLookupRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("debug_pokemon_lookup", debugLookupRequest?.Command);
        DebugPokemonLookupRequestPayload debugLookupPayload = TrackerJson.DeserializePayload<DebugPokemonLookupRequestPayload>(debugLookupRequest!.Payload);
        Assert.Equal(PokemonLookupSection.Overview, debugLookupPayload.Section);
        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(debugLookupRequest!.RequestId!, lookupResponse, "run-1"));
        PokemonLookupSnapshot receivedDebugLookup = await debugLookupTask;
        Assert.Equal(309, receivedDebugLookup.Stats!.GeneratedTotal);
        Assert.Equal("BLAZE", Assert.Single(receivedDebugLookup.Abilities!.Slots).AbilityId);

        Task<EvolutionCandidateSearchResponsePayload> debugCandidateTask = service.Requests.SearchDebugEvolutionCandidatesAsync("CHARMANDER:0", EvolutionCandidateSide.Normal, string.Empty);
        TrackerMessage? debugCandidateRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("debug_evolution_candidate_search", debugCandidateRequest?.Command);
        DebugEvolutionCandidateSearchRequestPayload debugCandidatePayload = TrackerJson.DeserializePayload<DebugEvolutionCandidateSearchRequestPayload>(debugCandidateRequest!.Payload);
        Assert.Equal(EvolutionCandidateSide.Normal, debugCandidatePayload.Side);
        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(debugCandidateRequest.RequestId!, candidateResponse, "run-1"));
        Assert.Equal("BULBASAUR:0", Assert.Single((await debugCandidateTask).Matches).SpeciesId);

        Task<EvolutionPredecessorSearchResponsePayload> debugPredecessorTask = service.Requests.SearchDebugEvolutionPredecessorsAsync("CHARMANDER:0", target: DebugPokemonTarget.Player);
        TrackerMessage? debugPredecessorRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(TrackerCommands.DebugEvolutionPredecessorSearch, debugPredecessorRequest?.Command);
        DebugEvolutionPredecessorSearchRequestPayload debugPredecessorPayload = TrackerJson.DeserializePayload<DebugEvolutionPredecessorSearchRequestPayload>(debugPredecessorRequest!.Payload);
        Assert.Equal(DebugPokemonTarget.Player, debugPredecessorPayload.Target);
        Assert.Equal(TrackerProtocol.EvolutionPredecessorPageSize, debugPredecessorPayload.Limit);
        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(debugPredecessorRequest.RequestId!, predecessorResponse, "run-1"));
        Assert.Equal("CYNDAQUIL:0", Assert.Single((await debugPredecessorTask).Matches).SpeciesId);

        Task<FusionMaterialSearchResponsePayload> debugMaterialTask = service.Requests.SearchDebugFusionMaterialsAsync("B445H175:0", 100);
        TrackerMessage? debugMaterialRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("debug_fusion_material_search", debugMaterialRequest?.Command);
        DebugFusionMaterialSearchRequestPayload debugMaterialPayload = TrackerJson.DeserializePayload<DebugFusionMaterialSearchRequestPayload>(debugMaterialRequest!.Payload);
        Assert.Equal(100, debugMaterialPayload.Offset);
        Assert.Equal(10, debugMaterialPayload.Limit);
        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(debugMaterialRequest.RequestId!, materialResponse, "run-1"));
        Assert.Equal(5_000, (await debugMaterialTask).Total);

        Task<WildOccurrenceSearchResponsePayload> debugWildOccurrenceTask = service.Requests.SearchDebugWildOccurrencesAsync("B310H310:0", 150);
        TrackerMessage? debugWildOccurrenceRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("debug_wild_occurrence_search", debugWildOccurrenceRequest?.Command);
        DebugWildOccurrenceSearchRequestPayload debugWildOccurrencePayload = TrackerJson.DeserializePayload<DebugWildOccurrenceSearchRequestPayload>(debugWildOccurrenceRequest!.Payload);
        Assert.Equal(150, debugWildOccurrencePayload.Offset);
        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(debugWildOccurrenceRequest.RequestId!, wildOccurrenceResponse, "run-1"));
        Assert.Equal(12_000, (await debugWildOccurrenceTask).Total);

        Task<TrainerOccurrenceSearchResponsePayload> debugTrainerOccurrenceTask = service.Requests.SearchDebugTrainerOccurrencesAsync("B310H310:0", 200);
        TrackerMessage? debugTrainerOccurrenceRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("debug_trainer_occurrence_search", debugTrainerOccurrenceRequest?.Command);
        DebugTrainerOccurrenceSearchRequestPayload debugTrainerOccurrencePayload = TrackerJson.DeserializePayload<DebugTrainerOccurrenceSearchRequestPayload>(debugTrainerOccurrenceRequest!.Payload);
        Assert.Equal(200, debugTrainerOccurrencePayload.Offset);
        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(debugTrainerOccurrenceRequest.RequestId!, trainerOccurrenceResponse, "run-1"));
        Assert.Equal(2_000, (await debugTrainerOccurrenceTask).Total);

        Task<FusionPreviewResponsePayload> debugFusionTask = service.Requests.PreviewDebugFusionAsync("CHARMANDER:0", "ALTARIA:0");
        TrackerMessage? debugFusionRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("debug_fusion_preview", debugFusionRequest?.Command);
        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(debugFusionRequest!.RequestId!, fusionResponse, "run-1"));
        Assert.Equal("B6H334:0", Assert.Single((await debugFusionTask).Outcomes).Result.SpeciesId);

        GameCurrentStatePayload startedState = new(true, "run-2", null, 1);
        TrackerMessage runStarted = TrackerMessageFactory.CreateEvent("run_started", 1, startedState, "run-2");
        await writer.WriteAsync(runStarted);
        TrackerConnectionSnapshot newRun = await WaitForSnapshotAsync(state, snapshot => snapshot.CurrentState?.RunId == "run-2");
        Assert.Equal(1, newRun.CurrentState?.Sequence);
        knowledge.SelectRun(null);

        StarterSelectionSnapshot starterSelection = new()
        {
            Active = true,
            RandomPickIndex = 2,
            Choices =
            [
                new StarterChoiceSnapshot { Index = 0 },
                new StarterChoiceSnapshot { Index = 1 },
                new StarterChoiceSnapshot { Index = 2 }
            ]
        };

        TrackerMessage starterSelectionChanged = TrackerMessageFactory.CreateEvent(TrackerEvents.StarterSelectionChanged, 2, starterSelection, "run-2");
        await writer.WriteAsync(starterSelectionChanged);
        TrackerRunStateSnapshot starterState = await WaitForRunSnapshotAsync(runState, snapshot => snapshot.StarterSelection is not null);
        Assert.Equal(2, starterState.StarterSelection?.RandomPickIndex);

        TrackerMessage starterSelectionEnded = TrackerMessageFactory.CreateEvent(TrackerEvents.StarterSelectionChanged, 3, new StarterSelectionSnapshot(), "run-2");
        await writer.WriteAsync(starterSelectionEnded);
        await WaitForRunSnapshotAsync(runState, snapshot => snapshot.StarterSelection is null);

        BattleSnapshot battle = new() { BattleId = "battle-1" };
        TrackerMessage battleStarted = TrackerMessageFactory.CreateEvent("battle_started", 2, battle, "run-2", "battle-1");
        await writer.WriteAsync(battleStarted);
        await WaitForRunSnapshotAsync(runState, snapshot => snapshot.Battle?.BattleId == "battle-1");
        Assert.Equal("run-2", knowledge.RunId);

        EnemyPokemonSnapshot enemy = new()
        {
            EnemyId = "enemy-1",
            Position = 1,
            SpeciesId = "BELLOSSOM:0",
            SpeciesName = "Bellossom",
            Level = 5,
            Types = ["GRASS"],
            BaseStatTotal = 490,
            LastAbility = new AbilitySnapshot
            {
                Id = "CHLOROPHYLL",
                Name = "Chlorophyll",
                Description = "Boosts Speed in sunshine."
            },
            LastMove = new ObservedMoveSnapshot
            {
                Id = "STUNSPORE",
                Name = "Stun Spore",
                LearnedLevel = 0,
                LearnOrder = 0,
                Source = "unknown",
                Origin = "enemy_use",
                Type = "GRASS",
                Power = 0,
                Accuracy = 75,
                TotalPp = 30,
                PpAfterUse = 29
            }
        };

        TrackerMessage enemySentOut = TrackerMessageFactory.CreateEvent("enemy_sent_out", 3, enemy, "run-2", "battle-1");
        await writer.WriteAsync(enemySentOut);
        TrackerRunStateSnapshot enemyState = await WaitForRunSnapshotAsync(runState, snapshot => snapshot.Enemies.Count == 1);
        Assert.Equal("Bellossom", enemyState.Enemies[0].SpeciesName);
        Assert.Equal(5, knowledge.GetHighestLevel("BELLOSSOM:0"));
        Assert.Equal("CHLOROPHYLL", Assert.Single(knowledge.GetAbilities("BELLOSSOM:0")).Id);
        await WaitForKnowledgeAsync(knowledge, "BELLOSSOM:0", 5);
        Assert.Equal("STUNSPORE", Assert.Single(knowledge.GetDisplayedMoves("BELLOSSOM:0", 5)).Id);

        EnemyMoveUsedPayload moveUsed = new()
        {
            EnemyId = "enemy-1",
            SpeciesId = "BELLOSSOM:0",
            EnemyLevel = 5,
            Move = new ObservedMoveSnapshot
            {
                Id = "ABSORB",
                Name = "Absorb",
                LearnedLevel = 0,
                LearnOrder = 0,
                Source = "unknown",
                Origin = "enemy_use",
                Type = "GRASS",
                Power = 20,
                Accuracy = 100,
                TotalPp = 25,
                PpAfterUse = 24
            }
        };
        TrackerMessage enemyMoveUsed = TrackerMessageFactory.CreateEvent("enemy_move_used", 4, moveUsed, "run-2", "battle-1");
        await writer.WriteAsync(enemyMoveUsed);
        await WaitForKnowledgeMoveAsync(knowledge, "BELLOSSOM:0", 5, "ABSORB");
        Assert.Contains(knowledge.GetDisplayedMoves("BELLOSSOM:0", 5), move => move.Id == "ABSORB");

        PlayerPokemonSnapshot player = CreatePlayerSnapshot(24, 24, 2);
        TrackerMessage sentOut = TrackerMessageFactory.CreateEvent("player_sent_out", 5, player, "run-2", "battle-1");
        await writer.WriteAsync(sentOut);
        TrackerRunStateSnapshot playerState = await WaitForRunSnapshotAsync(runState, snapshot => snapshot.Player is not null);
        Assert.Equal("Espeon", playerState.Player?.SpeciesName);
        Assert.Equal(2, playerState.Player?.Healing.ItemCount);

        BattleItemUseRequestPayload battleItem = new() { ItemId = "POTION", TargetPosition = 1 };
        Task<BattleItemUseResponsePayload> battleItemTask = service.Requests.UseBattleItemAsync(battleItem, "battle-1");
        TrackerMessage? battleItemRequest = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(TrackerCommands.UseBattleItem, battleItemRequest?.Command);
        Assert.Equal("run-2", battleItemRequest?.RunId);
        Assert.Equal("battle-1", battleItemRequest?.BattleId);
        BattleItemUseRequestPayload receivedBattleItem = TrackerJson.DeserializePayload<BattleItemUseRequestPayload>(battleItemRequest!.Payload);
        Assert.Equal("POTION", receivedBattleItem.ItemId);
        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(battleItemRequest.RequestId!, new BattleItemUseResponsePayload { Accepted = true, Message = "Potion selected." }, "run-2", "battle-1"));
        Assert.True((await battleItemTask).Accepted);

        PlayerMoveMenuOpenedPayload moveMenu = new() { PokemonId = "1234" };
        TrackerMessage moveMenuOpened = TrackerMessageFactory.CreateEvent("player_move_menu_opened", 6, moveMenu, "run-2", "battle-1");
        await writer.WriteAsync(moveMenuOpened);
        TrackerRunStateSnapshot moveMenuState = await WaitForRunSnapshotAsync(runState, snapshot => snapshot.MoveMenuPokemonId == "1234");
        Assert.Equal("1234", moveMenuState.MoveMenuPokemonId);

        PlayerPokemonSnapshot damaged = CreatePlayerSnapshot(12, 24, 1);
        TrackerMessage changed = TrackerMessageFactory.CreateEvent("player_state_changed", 6, damaged, "run-2", "battle-1");
        await writer.WriteAsync(changed);
        TrackerRunStateSnapshot damagedState = await WaitForRunSnapshotAsync(runState, snapshot => snapshot.Player?.CurrentHp == 12);
        Assert.Equal(50, damagedState.Player?.Healing.Percentage);

        TrackerMessage battleEnded = TrackerMessageFactory.CreateEvent("battle_ended", 7, battle, "run-2", "battle-1");
        await writer.WriteAsync(battleEnded);
        TrackerRunStateSnapshot endedState = await WaitForRunSnapshotAsync(runState, snapshot => snapshot.Battle is null);
        Assert.NotNull(endedState.Player);
        Assert.Empty(endedState.Enemies);

        client.Dispose();
        TrackerConnectionSnapshot waiting = await WaitForSnapshotAsync(state, snapshot => snapshot.Status == TrackerConnectionStatus.Waiting);
        Assert.Null(waiting.Game);
    }

    /// <summary>
    /// Verifies that tracker launch authorization is required before a debug request is sent.
    /// </summary>
    [Fact]
    public async Task ServiceRejectsUnauthorizedDebugInspection()
    {
        TrackerConnectionState state = new();
        TrackerRunState runState = new();
        TrackerKnowledgeStore knowledge = CreateKnowledgeStore();
        AreaDiscoveryStore areaDiscoveries = CreateAreaDiscoveryStore();
        CompletedRunArchive completedRuns = CreateCompletedRunArchive();
        TrackerConnectionOptions options = new(0, "0.1.0", false, TimeSpan.FromSeconds(2));
        await using TrackerConnectionService service = new(options, new TrackerDiagnosticsStore(), state, runState, knowledge, areaDiscoveries, completedRuns);
        DebugPokemonInspectionRequestPayload request = new() { Target = DebugPokemonTarget.Player };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Requests.InspectPokemonAsync(request));
    }

    /// <summary>
    /// Verifies that a non-handshake first message is rejected without stopping the listener.
    /// </summary>
    [Fact]
    public async Task ServiceRejectsInvalidFirstMessageAndKeepsListening()
    {
        TrackerConnectionState state = new();
        TrackerRunState runState = new();
        TrackerKnowledgeStore knowledge = CreateKnowledgeStore();
        TrackerConnectionOptions options = new(0, "0.1.0", false, TimeSpan.FromSeconds(2));
        await using TrackerConnectionService service = new(options, new TrackerDiagnosticsStore(), state, runState, knowledge, CreateAreaDiscoveryStore(), CreateCompletedRunArchive());
        service.Start();

        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, service.BoundPort);
        await using TrackerMessageWriter writer = new(client.GetStream());
        Dictionary<string, object?> payload = [];
        TrackerMessage invalid = TrackerMessageFactory.CreateEvent("run_started", 0, payload);
        await writer.WriteAsync(invalid);

        TrackerConnectionSnapshot error = await WaitForSnapshotAsync(state, snapshot => snapshot.Status == TrackerConnectionStatus.Error);
        Assert.Contains("game_connected", error.LastError, StringComparison.Ordinal);
        Assert.True(service.BoundPort > 0);
    }

    /// <summary>
    /// Verifies that a silent client times out without stopping the listener.
    /// </summary>
    [Fact]
    public async Task ServiceRejectsHandshakeTimeoutAndKeepsListening()
    {
        TrackerConnectionState state = new();
        TrackerRunState runState = new();
        TrackerKnowledgeStore knowledge = CreateKnowledgeStore();
        TrackerConnectionOptions options = new(0, "0.1.0", false, TimeSpan.FromMilliseconds(50));
        await using TrackerConnectionService service = new(options, new TrackerDiagnosticsStore(), state, runState, knowledge, CreateAreaDiscoveryStore(), CreateCompletedRunArchive());
        service.Start();

        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, service.BoundPort);

        TrackerConnectionSnapshot error = await WaitForSnapshotAsync(state, snapshot => snapshot.Status == TrackerConnectionStatus.Error);
        Assert.Contains("timed out", error.LastError, StringComparison.Ordinal);
        Assert.True(service.BoundPort > 0);
    }

    /// <summary>
    /// Waits for the connection state to satisfy an integration-test condition.
    /// </summary>
    /// <param name="state">The connection state being observed.</param>
    /// <param name="condition">The condition that completes the wait.</param>
    /// <returns>The first matching connection snapshot.</returns>
    /// <exception cref="TimeoutException">Thrown when no matching snapshot arrives.</exception>
    private static async Task<TrackerConnectionSnapshot> WaitForSnapshotAsync(TrackerConnectionState state, Func<TrackerConnectionSnapshot, bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (DateTimeOffset.UtcNow < deadline)
        {
            TrackerConnectionSnapshot snapshot = state.Snapshot;
            if (condition(snapshot))
                return snapshot;

            await Task.Delay(10);
        }

        throw new TimeoutException("The expected tracker connection state was not published.");
    }

    /// <summary>
    /// Waits for live run state to satisfy an integration-test condition.
    /// </summary>
    /// <param name="state">The live run state being observed.</param>
    /// <param name="condition">The condition that completes the wait.</param>
    /// <returns>The first matching run-state snapshot.</returns>
    /// <exception cref="TimeoutException">Thrown when no matching snapshot arrives.</exception>
    private static async Task<TrackerRunStateSnapshot> WaitForRunSnapshotAsync(TrackerRunState state, Func<TrackerRunStateSnapshot, bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (DateTimeOffset.UtcNow < deadline)
        {
            TrackerRunStateSnapshot snapshot = state.Snapshot;
            if (condition(snapshot))
                return snapshot;

            await Task.Delay(10);
        }

        throw new TimeoutException("The expected tracker run state was not published.");
    }

    /// <summary>
    /// Waits for a received enemy move to enter tracker-owned knowledge.
    /// </summary>
    /// <param name="knowledge">The knowledge store being observed.</param>
    /// <param name="speciesId">The observed enemy species and form.</param>
    /// <param name="level">The visible enemy level.</param>
    /// <returns>A task representing the wait.</returns>
    /// <exception cref="TimeoutException">Thrown when no displayed move arrives.</exception>
    private static async Task WaitForKnowledgeAsync(TrackerKnowledgeStore knowledge, string speciesId, int level)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (knowledge.GetDisplayedMoves(speciesId, level).Count > 0)
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException("The expected enemy move was not remembered.");
    }

    /// <summary>
    /// Waits for a specific received enemy move to enter tracker-owned knowledge.
    /// </summary>
    /// <param name="knowledge">The knowledge store being observed.</param>
    /// <param name="speciesId">The observed enemy species and form.</param>
    /// <param name="level">The visible enemy level.</param>
    /// <param name="moveId">The expected move identifier.</param>
    /// <returns>A task representing the wait.</returns>
    /// <exception cref="TimeoutException">Thrown when the move does not arrive.</exception>
    private static async Task WaitForKnowledgeMoveAsync(TrackerKnowledgeStore knowledge, string speciesId, int level, string moveId)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (knowledge.GetDisplayedMoves(speciesId, level).Any(move => move.Id == moveId))
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException("The expected specific enemy move was not remembered.");
    }

    /// <summary>
    /// Creates a complete player snapshot for connection integration tests.
    /// </summary>
    /// <param name="currentHp">The current HP.</param>
    /// <param name="maximumHp">The maximum HP.</param>
    /// <param name="healingItems">The number of healing items.</param>
    /// <returns>The test player snapshot.</returns>
    private static PlayerPokemonSnapshot CreatePlayerSnapshot(int currentHp, int maximumHp, int healingItems)
    {
        PlayerMoveSnapshot move = new()
        {
            Id = "PSYCHIC",
            Name = "Psychic",
            Type = "PSYCHIC",
            CurrentPp = 10,
            TotalPp = 10,
            Power = 90,
            Accuracy = 100
        };

        HealingInventorySnapshot healing = new()
        {
            ItemCount = healingItems,
            PotentialHp = 12,
            Percentage = 50
        };

        return new PlayerPokemonSnapshot
        {
            PokemonId = "1234",
            SpeciesId = "ESPEON:0",
            Nickname = "Espeon",
            SpeciesName = "Espeon",
            Gender = "female",
            Level = 5,
            CurrentHp = currentHp,
            MaximumHp = maximumHp,
            Status = "NONE",
            Types = ["PSYCHIC"],
            Ability = "Synchronize",
            Attack = 14,
            Defense = 16,
            SpecialAttack = 21,
            SpecialDefense = 18,
            Speed = 15,
            BaseStatTotal = 525,
            Nature = "Hardy",
            Moves = [move],
            Healing = healing
        };
    }

    /// <summary>
    /// Creates an isolated tracker knowledge store for a connection test.
    /// </summary>
    /// <returns>The isolated tracker knowledge store.</returns>
    private static TrackerKnowledgeStore CreateKnowledgeStore()
    {
        string path = Path.Combine(Path.GetTempPath(), "IronmonTrackerTests", Guid.NewGuid().ToString("N"));
        return new TrackerKnowledgeStore(new TrackerKnowledgeOptions(path));
    }

    /// <summary>
    /// Creates an isolated tracker-owned area discovery store for a connection test.
    /// </summary>
    /// <returns>The isolated area discovery store.</returns>
    private static AreaDiscoveryStore CreateAreaDiscoveryStore()
    {
        string path = Path.Combine(Path.GetTempPath(), "IronmonTrackerTests", Guid.NewGuid().ToString("N"));
        return new AreaDiscoveryStore(new TrackerKnowledgeOptions(path));
    }

    /// <summary>
    /// Creates an isolated completed-run archive for a connection test.
    /// </summary>
    /// <returns>The isolated completed-run archive.</returns>
    private static CompletedRunArchive CreateCompletedRunArchive()
    {
        string path = Path.Combine(Path.GetTempPath(), "IronmonTrackerTests", Guid.NewGuid().ToString("N"));
        return new CompletedRunArchive(new TrackerKnowledgeOptions(path));
    }

    /// <summary>
    /// Creates a valid completed-run recipe for connection tests.
    /// </summary>
    /// <param name="runId">The stable test run identifier.</param>
    /// <returns>The completed-run recipe.</returns>
    private static CompletedRunRecipePayload CreateRecipe(string runId) => new()
    {
        RunId = runId,
        Seed = 12345,
        Result = "lost",
        GameVersion = "6.8.0",
        IronmonVersion = "0.3.3",
        Configuration = CreateConfiguration(),
        SpeciesGenerator = new SpeciesGeneratorRecipePayload { Version = 1, PoolFingerprint = "species" },
        AbilityGenerator = new AbilityGeneratorRecipePayload { Version = 3, PoolSize = 10, PoolFingerprint = "abilities" },
        PlayerFusionGenerator = new PlayerFusionGeneratorRecipePayload { Version = 2, PoolSize = 10, PoolFingerprint = "fusions" }
    };

    /// <summary>
    /// Creates a valid typed configuration for connection tests.
    /// </summary>
    /// <returns>The test configuration.</returns>
    private static RunConfigurationPayload CreateConfiguration() => new()
    {
        SchemaVersion = 1,
        WildPolicy = "mixed",
        TrainerPolicy = "mixed",
        UnfusionSetting = "random_component"
    };

    /// <summary>
    /// Creates valid runtime diagnostics for connection tests.
    /// </summary>
    /// <returns>The test runtime diagnostics.</returns>
    private static DebugRuntimeDiagnosticsSnapshot CreateRuntimeDiagnostics() => new()
    {
        GameVersion = "6.8.0",
        IronmonVersion = "0.3.3",
        ProtocolVersion = TrackerProtocol.CurrentSchemaVersion
    };

    /// <summary>
    /// Waits for a completed-run recipe to enter tracker-owned persistence.
    /// </summary>
    /// <param name="archive">The completed-run archive being observed.</param>
    /// <param name="runId">The expected stable run identifier.</param>
    /// <returns>A task representing the wait.</returns>
    /// <exception cref="TimeoutException">Thrown when the recipe does not arrive.</exception>
    private static async Task WaitForRecipeAsync(CompletedRunArchive archive, string runId)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (archive.Recipes.Any(recipe => recipe.RunId == runId))
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException("The expected completed-run recipe was not persisted.");
    }
}
