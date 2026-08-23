using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies signed capability negotiation and live replacement over one game connection.
/// </summary>
public sealed class DiagnosticCapabilityNegotiationTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ironmon-negotiation-{Guid.NewGuid():N}");

    /// <summary>
    /// Initializes the diagnostic capability negotiation tests.
    /// </summary>
    public DiagnosticCapabilityNegotiationTests()
    {
    }

    /// <summary>
    /// Verifies the tracker sends only the supported grant and removes it live.
    /// </summary>
    [Fact]
    public async Task ServiceNegotiatesIntersectionAndSendsLiveRemoval()
    {
        using ECDsa privateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa publicKey = ECDsa.Create(privateKey.ExportParameters(false));
        using DiagnosticAccessSigningKey signingKey = DiagnosticAccessSigningKey.Import(privateKey.ExportPkcs8PrivateKeyPem(), "negotiation-private.pem");
        ECDsaSecurityKey verificationKey = new(publicKey) { KeyId = signingKey.KeyId };
        DiagnosticAccessTokenValidator validator = new(new DiagnosticAccessKeyring([verificationKey]));
        TrackerKnowledgeOptions storage = new(_root);
        using DiagnosticAccessService access = new(storage, validator);
        DiagnosticAccessTokenGenerationRequest tokenRequest = new("negotiation", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), null, [DiagnosticCapabilities.RunSeed, DiagnosticCapabilities.PokemonCurrentPlayer, DiagnosticCapabilities.EvolutionCandidates, DiagnosticCapabilities.EvolutionResults, DiagnosticCapabilities.TrackerRawState]);
        string token = new DiagnosticAccessTokenGenerator().Generate(signingKey, tokenRequest).Token;
        Assert.True((await access.ActivateAsync(token)).IsValid);

        TrackerConnectionState state = new();
        TrackerDiagnosticsStore diagnostics = new();
        TrackerRunState runState = new();
        TrackerKnowledgeStore knowledge = new(storage);
        AreaDiscoveryStore areaDiscoveries = new(storage);
        CompletedRunArchive completedRuns = new(storage);
        TrackerConnectionOptions options = new(0, "0.7.4", false, TimeSpan.FromSeconds(2));
        await using TrackerConnectionService service = new(options, diagnostics, state, runState, knowledge, areaDiscoveries, completedRuns, access);
        service.Start();

        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, service.BoundPort);
        NetworkStream stream = client.GetStream();
        using TrackerMessageReader reader = new(stream, leaveOpen: true);
        await using TrackerMessageWriter writer = new(stream, leaveOpen: true);
        GameHandshakePayload game = new("6.8.0", "0.7.4", true, false, @"C:\Game", "run-1", null, [DiagnosticCapabilities.RunSeed, DiagnosticCapabilities.PokemonCurrentPlayer, DiagnosticCapabilities.EvolutionCandidates, DiagnosticCapabilities.EvolutionResults]);
        await writer.WriteAsync(TrackerMessageFactory.CreateEvent(TrackerEvents.GameConnected, 0, game));

        TrackerMessage trackerHandshake = await ReadRequiredAsync(reader);
        TrackerHandshakePayload tracker = TrackerJson.DeserializePayload<TrackerHandshakePayload>(trackerHandshake.Payload);
        Assert.Equal([DiagnosticCapabilities.EvolutionCandidates, DiagnosticCapabilities.EvolutionResults, DiagnosticCapabilities.PokemonCurrentPlayer, DiagnosticCapabilities.RunSeed], tracker.DiagnosticCapabilities);
        TrackerMessage currentStateRequest = await ReadRequiredAsync(reader);
        GameCurrentStatePayload currentState = new(true, "run-1", null, 1);
        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(currentStateRequest.RequestId!, currentState, "run-1"));
        await WaitForConnectedStateAsync(state);
        Assert.True(service.Requests.HasDiagnosticCapability(DiagnosticCapabilities.RunSeed));
        Assert.False(service.Requests.HasDiagnosticCapability(DiagnosticCapabilities.TrackerRawState));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Requests.SearchDebugPokemonAsync("char"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Requests.SearchDebugEvolutionCandidatesAsync("CHARMANDER:0", EvolutionCandidateSide.Normal, string.Empty));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.Requests.InspectPokemonAsync(new DebugPokemonInspectionRequestPayload { Target = DebugPokemonTarget.Party, Section = PokemonLookupSection.Evolutions }));

        Task<EvolutionCandidateSearchResponsePayload> candidateTask = service.Requests.SearchDebugEvolutionCandidatesAsync("TAMPERED:0", EvolutionCandidateSide.Normal, string.Empty, target: DebugPokemonTarget.Player);
        TrackerMessage candidateRequest = await ReadRequiredAsync(reader);
        DebugEvolutionCandidateSearchRequestPayload candidatePayload = TrackerJson.DeserializePayload<DebugEvolutionCandidateSearchRequestPayload>(candidateRequest.Payload);
        Assert.Equal("TAMPERED:0", candidatePayload.SpeciesId);
        Assert.Equal(DebugPokemonTarget.Player, candidatePayload.Target);
        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(candidateRequest.RequestId!, new EvolutionCandidateSearchResponsePayload(), "run-1"));
        Assert.Empty((await candidateTask).Matches);

        Task<EvolutionPredecessorSearchResponsePayload> predecessorTask = service.Requests.SearchDebugEvolutionPredecessorsAsync("TAMPERED:0", target: DebugPokemonTarget.Player);
        TrackerMessage predecessorRequest = await ReadRequiredAsync(reader);
        DebugEvolutionPredecessorSearchRequestPayload predecessorPayload = TrackerJson.DeserializePayload<DebugEvolutionPredecessorSearchRequestPayload>(predecessorRequest.Payload);
        Assert.Equal(TrackerCommands.DebugEvolutionPredecessorSearch, predecessorRequest.Command);
        Assert.Equal("TAMPERED:0", predecessorPayload.SpeciesId);
        Assert.Equal(DebugPokemonTarget.Player, predecessorPayload.Target);
        Assert.Equal(TrackerProtocol.EvolutionPredecessorPageSize, predecessorPayload.Limit);
        EvolutionPredecessorSearchResponsePayload predecessorResponse = new() { Continuation = EvolutionPredecessorContinuation.Complete };
        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(predecessorRequest.RequestId!, predecessorResponse, "run-1"));
        Assert.Empty((await predecessorTask).Matches);

        DebugPokemonInspectionRequestPayload inspection = new() { Target = DebugPokemonTarget.Player, Section = PokemonLookupSection.Evolutions };
        Task<DebugPokemonInspectorSnapshot> inspectionTask = service.Requests.InspectPokemonAsync(inspection);
        TrackerMessage inspectionRequest = await ReadRequiredAsync(reader);
        DebugPokemonInspectorSnapshot inspectionResponse = new()
        {
            Section = PokemonLookupSection.Evolutions,
            Identity = new DebugPokemonIdentitySnapshot
            {
                PokemonId = "1",
                Nickname = "Charmander",
                SpeciesId = "CHARMANDER:0",
                SpeciesName = "Charmander",
                Gender = "male",
                ActiveAbilitySlot = "Normal 0",
                ActiveAbilityId = "BLAZE",
                ActiveAbilityName = "Blaze"
            },
            Lookup = new PokemonLookupSnapshot
            {
                Section = PokemonLookupSection.Evolutions,
                Identity = new PokemonLookupIdentitySnapshot { SpeciesId = "CHARMANDER:0", SpeciesName = "Charmander" },
                Evolutions = new PokemonLookupEvolutionsSnapshot()
            }
        };

        await writer.WriteAsync(TrackerMessageFactory.CreateResponse(inspectionRequest.RequestId!, inspectionResponse, "run-1"));
        Assert.NotNull((await inspectionTask).Lookup?.Evolutions);

        Assert.True(access.Remove());
        TrackerMessage changedMessage = await ReadRequiredAsync(reader);
        DiagnosticAccessChangedPayload changed = TrackerJson.DeserializePayload<DiagnosticAccessChangedPayload>(changedMessage.Payload);
        Assert.Equal(TrackerEvents.DiagnosticAccessChanged, changedMessage.Event);
        Assert.Empty(changed.DiagnosticCapabilities);
        Assert.False(service.Requests.HasDiagnosticCapability(DiagnosticCapabilities.RunSeed));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Requests.GetDebugRunDiagnosticsAsync());
    }

    /// <summary>
    /// Reads one required protocol message within the integration-test timeout.
    /// </summary>
    /// <param name="reader">The connected protocol reader.</param>
    /// <returns>The received non-null message.</returns>
    private static async Task<TrackerMessage> ReadRequiredAsync(TrackerMessageReader reader)
        => await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)) ?? throw new IOException("The game connection closed before the expected message arrived.");

    /// <summary>
    /// Waits until current-state recovery marks the service fully connected.
    /// </summary>
    /// <param name="state">The observed connection state.</param>
    /// <returns>A task representing the wait.</returns>
    /// <exception cref="TimeoutException">Thrown when recovery does not complete.</exception>
    private static async Task WaitForConnectedStateAsync(TrackerConnectionState state)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (state.Snapshot.CurrentState is not null)
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException("The tracker did not recover current game state.");
    }

    /// <summary>
    /// Performs no asynchronous setup.
    /// </summary>
    /// <returns>A completed task.</returns>
    public Task InitializeAsync()
        => Task.CompletedTask;

    /// <summary>
    /// Removes temporary tracker-owned persistence after each test.
    /// </summary>
    /// <returns>A completed task.</returns>
    public Task DisposeAsync()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);

        return Task.CompletedTask;
    }
}
