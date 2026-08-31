using Ironmon.Tracker.Connection.Transport;
using Ironmon.Tracker.Connection.Obtainability;

namespace Ironmon.Tracker.Connection.Access;

/// <summary>
/// Validates and sends authorized diagnostic requests for the active run.
/// </summary>
internal sealed class TrackerDiagnosticRequestClient
{
    private static readonly TimeSpan _occurrencePollInterval = TimeSpan.FromMilliseconds(50);
    private readonly TrackerDiagnosticAuthorizer _authorization;
    private readonly PlayerFusionMappingCoordinator _fusionMappings;
    private readonly TrackerRequestSession _session;

    /// <summary>
    /// Initializes active diagnostic requests for one tracker connection.
    /// </summary>
    /// <param name="session">The correlated request session.</param>
    /// <param name="authorization">The diagnostic authorization policy.</param>
    /// <param name="fusionMappings">The shared tracker-side material-mapping coordinator.</param>
    internal TrackerDiagnosticRequestClient(TrackerRequestSession session, TrackerDiagnosticAuthorizer authorization, PlayerFusionMappingCoordinator fusionMappings)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(fusionMappings);
        _session = session;
        _authorization = authorization;
        _fusionMappings = fusionMappings;
    }

    /// <summary>
    /// Requests the game-owned Ironmon inspector data for one current Pokémon.
    /// </summary>
    /// <param name="request">The Pokémon source selection.</param>
    /// <param name="runId">The active run identifier when available.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The complete authorized inspector snapshot.</returns>
    internal Task<DebugPokemonInspectorSnapshot> InspectPokemonAsync(DebugPokemonInspectionRequestPayload request, string? runId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        _authorization.EnsurePokemonSource(request.Target);
        _authorization.EnsurePokemonInformation(request.Section);
        return _session.SendAsync<DebugPokemonInspectionRequestPayload, DebugPokemonInspectorSnapshot>(TrackerCommands.DebugInspectPokemon, request, runId, cancellationToken);
    }

    /// <summary>
    /// Requests game-owned run and randomizer diagnostics.
    /// </summary>
    /// <param name="runId">The active run identifier when available.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The authorized run diagnostics.</returns>
    internal Task<DebugRunDiagnosticsSnapshot> GetRunDiagnosticsAsync(string? runId, CancellationToken cancellationToken)
    {
        _authorization.EnsureAny(DiagnosticCapabilities.RunConfiguration, DiagnosticCapabilities.RunSeed, DiagnosticCapabilities.RunGeneratorManifests, DiagnosticCapabilities.EvolutionGeneratorDetails);
        DebugRunDiagnosticsRequestPayload request = new();
        return _session.SendAsync<DebugRunDiagnosticsRequestPayload, DebugRunDiagnosticsSnapshot>(TrackerCommands.DebugRunDiagnostics, request, runId, cancellationToken);
    }

    /// <summary>
    /// Searches generated Pokémon in the active run through the authorized debug channel.
    /// </summary>
    /// <param name="query">The name fragment entered by the user.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="limit">The maximum number of matches to return.</param>
    /// <param name="normalOnly">Whether to restrict matches to normal species.</param>
    /// <param name="runId">The active run identifier when available.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The matching active-run Pokémon identifiers.</returns>
    internal Task<PokemonSearchResponsePayload> SearchPokemonAsync(string query, int offset, int limit, bool normalOnly, string? runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, TrackerProtocol.MinimumSearchPageSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, TrackerProtocol.MaximumSearchPageSize);
        _authorization.EnsureAll(DiagnosticCapabilities.PokemonAllActive);
        DebugPokemonSearchRequestPayload request = new() { Query = query.Trim(), Offset = offset, Limit = limit, NormalOnly = normalOnly };
        return _session.SendAsync<DebugPokemonSearchRequestPayload, PokemonSearchResponsePayload>(TrackerCommands.DebugPokemonSearch, request, runId, cancellationToken);
    }

    /// <summary>
    /// Requests generated information for one Pokémon in the active debug run.
    /// </summary>
    /// <param name="speciesId">The selected stable species and form identifier.</param>
    /// <param name="section">The independently requested information section.</param>
    /// <param name="runId">The active run identifier when available.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The active-run generated Pokémon information.</returns>
    internal Task<PokemonLookupSnapshot> LookupPokemonAsync(string speciesId, PokemonLookupSection section, string? runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        _authorization.EnsurePokemonLookup(section);
        IReadOnlyList<EvolutionTargetAssignmentPayload>? evolutionAssignments = section == PokemonLookupSection.Evolutions
            ? _fusionMappings.GetActiveEvolutionTargetAssignments(runId, speciesId)
            : null;

        DebugPokemonLookupRequestPayload request = new() { SpeciesId = speciesId, Section = section, EvolutionAssignments = evolutionAssignments };
        return _session.SendAsync<DebugPokemonLookupRequestPayload, PokemonLookupSnapshot>(TrackerCommands.DebugPokemonLookup, request, runId, cancellationToken);
    }

    /// <summary>
    /// Advances and reads the shared obtainability calculation for the authorized active run.
    /// </summary>
    /// <param name="speciesId">The optional species whose witness should be returned.</param>
    /// <param name="speciesIds">The bounded identifiers whose proven membership should be returned.</param>
    /// <param name="evolutionEdgeKeys">The bounded evolution connections whose possible membership should be returned.</param>
    /// <param name="foreground">Whether the game should temporarily prioritize this run's calculation.</param>
    /// <param name="progress">The optional observer for intermediate calculation responses.</param>
    /// <param name="runId">The active run identifier when available.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The current shared calculation state.</returns>
    internal async Task<PokemonObtainabilityResponsePayload> AdvanceObtainabilityAsync(string? speciesId, IReadOnlyList<string>? speciesIds, IReadOnlyList<string>? evolutionEdgeKeys, bool foreground, Action<PokemonObtainabilityResponsePayload>? progress, string? runId, CancellationToken cancellationToken)
    {
        _authorization.EnsureAll(DiagnosticCapabilities.EvolutionResults, DiagnosticCapabilities.FusionMaterialPairs, DiagnosticCapabilities.PokemonAllActive, DiagnosticCapabilities.WorldItems, DiagnosticCapabilities.WorldWildEncounters);
        DebugPokemonObtainabilityRequestPayload request = new() { SpeciesId = speciesId, SpeciesIds = speciesIds ?? [], EvolutionEdgeKeys = evolutionEdgeKeys ?? [], Foreground = foreground };
        PokemonObtainabilityResponsePayload response = await _session.SendAsync<DebugPokemonObtainabilityRequestPayload, PokemonObtainabilityResponsePayload>(TrackerCommands.DebugPokemonObtainability, request, runId, cancellationToken);
        progress?.Invoke(response);
        while (response.FusionClosureWork is not null)
        {
            PlayerFusionClosureResultPayload? result = await _fusionMappings.CreateResultAsync(response.FusionClosureWork, cancellationToken, runId).ConfigureAwait(false);
            request = result is null
                ? new DebugPokemonObtainabilityRequestPayload { SpeciesId = speciesId, SpeciesIds = speciesIds ?? [], EvolutionEdgeKeys = evolutionEdgeKeys ?? [], Foreground = foreground, FusionClosureWorkerUnavailable = true }
                : new DebugPokemonObtainabilityRequestPayload { SpeciesId = speciesId, SpeciesIds = speciesIds ?? [], EvolutionEdgeKeys = evolutionEdgeKeys ?? [], Foreground = foreground, FusionClosureResult = result };

            response = await _session.SendAsync<DebugPokemonObtainabilityRequestPayload, PokemonObtainabilityResponsePayload>(TrackerCommands.DebugPokemonObtainability, request, runId, cancellationToken);
            progress?.Invoke(response);
            if (!foreground)
                break;
        }

        return response;
    }

    /// <summary>
    /// Requests one filtered page of valid evolution candidates from the active debug run.
    /// </summary>
    /// <param name="speciesId">The source species identifier.</param>
    /// <param name="side">The normal or component-specific candidate list.</param>
    /// <param name="query">The optional candidate name filter.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="target">The optional live source that securely supplies the represented species.</param>
    /// <param name="enemyPosition">The enemy battler position when the live source is an enemy.</param>
    /// <param name="runId">The active run identifier when available.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested candidate page.</returns>
    internal Task<EvolutionCandidateSearchResponsePayload> SearchEvolutionCandidatesAsync(string speciesId, EvolutionCandidateSide side, string query, int offset, DebugPokemonTarget? target, int? enemyPosition, string? runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        _authorization.EnsurePokemonSource(target);
        _authorization.EnsureAll(DiagnosticCapabilities.EvolutionCandidates);
        byte[]? candidateAssignments = target is null
            ? _fusionMappings.GetActiveEvolutionCandidateAssignments(runId, speciesId, side)
            : null;

        DebugEvolutionCandidateSearchRequestPayload request = new() { SpeciesId = speciesId, Target = target, EnemyPosition = enemyPosition, Side = side, Query = query.Trim(), Offset = offset, PackedCandidateAssignments = candidateAssignments };
        return _session.SendAsync<DebugEvolutionCandidateSearchRequestPayload, EvolutionCandidateSearchResponsePayload>(TrackerCommands.DebugEvolutionCandidateSearch, request, runId, cancellationToken);
    }

    /// <summary>
    /// Requests one progressive page of generated evolution predecessors from the active debug run.
    /// </summary>
    /// <param name="speciesId">The target species identifier.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="limit">The maximum number of predecessors to return.</param>
    /// <param name="target">The optional live source that securely supplies the represented species.</param>
    /// <param name="enemyPosition">The enemy battler position when the live source is an enemy.</param>
    /// <param name="runId">The active run identifier when available.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested predecessor page.</returns>
    internal async Task<EvolutionPredecessorSearchResponsePayload> SearchEvolutionPredecessorsAsync(string speciesId, int offset, int limit, DebugPokemonTarget? target, int? enemyPosition, string? runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, TrackerProtocol.MinimumSearchPageSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, TrackerProtocol.MaximumSearchPageSize);
        _authorization.EnsurePokemonSource(target);
        _authorization.EnsureAll(DiagnosticCapabilities.EvolutionResults);
        IReadOnlyList<EvolutionPredecessorAssignmentPayload>? predecessorAssignments = await _fusionMappings.GetActiveEvolutionPredecessorAssignmentsAsync(runId, speciesId, cancellationToken).ConfigureAwait(false);
        int requestLimit = predecessorAssignments is null
            ? limit
            : Math.Min(TrackerProtocol.MaximumSearchPageSize, Math.Max(limit, predecessorAssignments.Count - offset));

        DebugEvolutionPredecessorSearchRequestPayload request = new() { SpeciesId = speciesId, Target = target, EnemyPosition = enemyPosition, Offset = offset, Limit = requestLimit, PredecessorAssignments = predecessorAssignments };
        return await _session.SendAsync<DebugEvolutionPredecessorSearchRequestPayload, EvolutionPredecessorSearchResponsePayload>(TrackerCommands.DebugEvolutionPredecessorSearch, request, runId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Requests one page of fusion-material pairs from the authorized active run.
    /// </summary>
    /// <param name="speciesId">The fusion species identifier.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="target">The optional live source that securely supplies the represented species.</param>
    /// <param name="enemyPosition">The enemy battler position when the live source is an enemy.</param>
    /// <param name="runId">The active run identifier when available.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested material-pair page.</returns>
    internal async Task<FusionMaterialSearchResponsePayload> SearchFusionMaterialsAsync(string speciesId, int offset, DebugPokemonTarget? target, int? enemyPosition, string? runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        _authorization.EnsurePokemonSource(target);
        _authorization.EnsureAll(DiagnosticCapabilities.FusionMaterialPairs);
        PlayerFusionMaterialPage? assignments = await _fusionMappings.GetActiveFusionMaterialAssignmentsAsync(runId, speciesId, offset, TrackerProtocol.FusionMaterialPageSize, cancellationToken).ConfigureAwait(false);

        DebugFusionMaterialSearchRequestPayload request = new()
        {
            SpeciesId = speciesId,
            Target = target,
            EnemyPosition = enemyPosition,
            Offset = offset,
            MaterialAssignments = assignments?.Assignments,
            MaterialAssignmentTotal = assignments?.Total
        };

        return await _session.SendAsync<DebugFusionMaterialSearchRequestPayload, FusionMaterialSearchResponsePayload>(TrackerCommands.DebugFusionMaterialSearch, request, runId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Requests one page of wild occurrences from the authorized active run.
    /// </summary>
    /// <param name="speciesId">The generated species identifier.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="target">The optional live source that securely supplies the represented species.</param>
    /// <param name="enemyPosition">The enemy battler position when the live source is an enemy.</param>
    /// <param name="runId">The active run identifier when available.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested wild-occurrence page.</returns>
    internal async Task<WildOccurrenceSearchResponsePayload> SearchWildOccurrencesAsync(string speciesId, int offset, DebugPokemonTarget? target, int? enemyPosition, string? runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        _authorization.EnsurePokemonSource(target);
        _authorization.EnsureAll(DiagnosticCapabilities.WorldWildEncounters);
        byte[]? membership = await _fusionMappings.GetOccurrenceFusionMaterialsAsync(runId, null, speciesId, cancellationToken);
        DebugWildOccurrenceSearchRequestPayload request = new() { SpeciesId = speciesId, Target = target, EnemyPosition = enemyPosition, Offset = offset, FusionMaterialMembership = membership };
        WildOccurrenceSearchResponsePayload response;
        do
        {
            _authorization.EnsurePokemonSource(target);
            _authorization.EnsureAll(DiagnosticCapabilities.WorldWildEncounters);
            response = await _session.SendAsync<DebugWildOccurrenceSearchRequestPayload, WildOccurrenceSearchResponsePayload>(TrackerCommands.DebugWildOccurrenceSearch, request, runId, cancellationToken);
            if (response.Pending)
                await Task.Delay(_occurrencePollInterval, cancellationToken);

        } while (response.Pending);

        return response;
    }

    /// <summary>
    /// Requests one page of trainer occurrences from the authorized active run.
    /// </summary>
    /// <param name="speciesId">The generated species identifier.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="target">The optional live source that securely supplies the represented species.</param>
    /// <param name="enemyPosition">The enemy battler position when the live source is an enemy.</param>
    /// <param name="runId">The active run identifier when available.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested trainer-occurrence page.</returns>
    internal Task<TrainerOccurrenceSearchResponsePayload> SearchTrainerOccurrencesAsync(string speciesId, int offset, DebugPokemonTarget? target, int? enemyPosition, string? runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        _authorization.EnsurePokemonSource(target);
        _authorization.EnsureAll(DiagnosticCapabilities.WorldTrainerParties);
        DebugTrainerOccurrenceSearchRequestPayload request = new() { SpeciesId = speciesId, Target = target, EnemyPosition = enemyPosition, Offset = offset };
        return _session.SendAsync<DebugTrainerOccurrenceSearchRequestPayload, TrainerOccurrenceSearchResponsePayload>(TrackerCommands.DebugTrainerOccurrenceSearch, request, runId, cancellationToken);
    }

    /// <summary>
    /// Requests both generated fusion orientations for the active debug run.
    /// </summary>
    /// <param name="firstSpeciesId">The first normal fusion material.</param>
    /// <param name="secondSpeciesId">The second normal fusion material.</param>
    /// <param name="runId">The active run identifier when available.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The active-run fusion outcomes.</returns>
    internal Task<FusionPreviewResponsePayload> PreviewFusionAsync(string firstSpeciesId, string secondSpeciesId, string? runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstSpeciesId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secondSpeciesId);
        _authorization.EnsureAll(DiagnosticCapabilities.FusionPreviewResults);
        DebugFusionPreviewRequestPayload request = new() { FirstSpeciesId = firstSpeciesId, SecondSpeciesId = secondSpeciesId };
        return _session.SendAsync<DebugFusionPreviewRequestPayload, FusionPreviewResponsePayload>(TrackerCommands.DebugFusionPreview, request, runId, cancellationToken);
    }
}
