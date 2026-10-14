using System.Text.Json;
using Ironmon.Tracker.Connection.Transport;

namespace Ironmon.Tracker.Connection.Obtainability;

/// <summary>
/// Runs the shared active-run calculation without granting access to diagnostic lookup results.
/// </summary>
/// <remarks>
/// Initializes calculation transport and the shared native worker.
/// </remarks>
/// <param name="session">The correlated request session.</param>
/// <param name="fusionMappings">The shared tracker-side mapping coordinator.</param>
/// <param name="state">The current peer and recovered active-run recipe.</param>
internal sealed class TrackerRunPreparationClient(TrackerRequestSession session, PlayerFusionMappingCoordinator fusionMappings, TrackerConnectionState state)
{
    private const int RequestEnvelopeAllowance = 1024;

    /// <summary>
    /// Advances preparation and completes any tracker-owned calculation jobs returned by the game.
    /// </summary>
    /// <param name="foreground">Whether the user explicitly requested priority calculation.</param>
    /// <param name="progress">The observer receiving calculation progress.</param>
    /// <param name="runId">The active run identifier.</param>
    /// <param name="cancellationToken">The token canceling this request.</param>
    /// <returns>Calculation progress without diagnostic target or location queries.</returns>
    internal async Task<PokemonObtainabilityResponsePayload> AdvanceAsync(bool foreground, Action<PokemonObtainabilityResponsePayload> progress, string? runId, CancellationToken cancellationToken)
    {
        RunLookupPreparationRequestPayload request = new() { Foreground = foreground, UseNativeMaterialPairPreparation = true };
        PokemonObtainabilityResponsePayload response = await session.SendAsync<RunLookupPreparationRequestPayload, PokemonObtainabilityResponsePayload>(TrackerCommands.PrepareRunLookup, request, runId, cancellationToken).ConfigureAwait(false);
        progress(response);
        if (response.PlayerFusionPairingReady == false)
        {
            GameplayFusionPairingPayload? pairing = await CreateGameplayPairingAsync(runId, cancellationToken).ConfigureAwait(false);
            if (pairing is not null)
            {
                request = new RunLookupPreparationRequestPayload { Foreground = foreground, UseNativeMaterialPairPreparation = true, PlayerFusionPairing = pairing };
                int requestBytes = JsonSerializer.SerializeToUtf8Bytes(request, TrackerJson.Options).Length;
                if (pairing.BaseStats is not null && requestBytes > TrackerProtocol.MaximumMessageCharacters - RequestEnvelopeAllowance)
                {
                    pairing = new GameplayFusionPairingPayload { SessionId = pairing.SessionId, Recipe = pairing.Recipe, PackedPairs = pairing.PackedPairs };
                    request = new RunLookupPreparationRequestPayload { Foreground = foreground, UseNativeMaterialPairPreparation = true, PlayerFusionPairing = pairing };
                    requestBytes = JsonSerializer.SerializeToUtf8Bytes(request, TrackerJson.Options).Length;
                }

                if (requestBytes <= TrackerProtocol.MaximumMessageCharacters - RequestEnvelopeAllowance)
                {
                    response = await session.SendAsync<RunLookupPreparationRequestPayload, PokemonObtainabilityResponsePayload>(TrackerCommands.PrepareRunLookup, request, runId, cancellationToken).ConfigureAwait(false);
                    progress(response);
                }
            }
        }

        while (response.FusionClosureWork is not null)
        {
            PlayerFusionClosureResultPayload? result = await fusionMappings.CreateResultAsync(response.FusionClosureWork, cancellationToken, runId).ConfigureAwait(false);
            request = new RunLookupPreparationRequestPayload { Foreground = foreground, UseNativeMaterialPairPreparation = true, FusionClosureResult = result, FusionClosureWorkerUnavailable = result is null };
            response = await session.SendAsync<RunLookupPreparationRequestPayload, PokemonObtainabilityResponsePayload>(TrackerCommands.PrepareRunLookup, request, runId, cancellationToken).ConfigureAwait(false);
            progress(response);
            if (!foreground)
                break;
        }

        return response;
    }

    /// <summary>
    /// Creates an optional assignment for a negotiated peer and rechecks its full run context after native work.
    /// </summary>
    /// <remarks>
    /// Native construction failures leave the existing run preparation path available. Cancellation and stale-context failures propagate.
    /// </remarks>
    /// <param name="runId">The active run receiving preparation.</param>
    /// <param name="cancellationToken">The token cancelling this request's wait.</param>
    /// <returns>The complete compatible assignment, or null when native sharing is unavailable.</returns>
    /// <exception cref="IOException">Thrown when the destination process or deterministic run context changes during preparation.</exception>
    private async Task<GameplayFusionPairingPayload?> CreateGameplayPairingAsync(string? runId, CancellationToken cancellationToken)
    {
        TrackerConnectionSnapshot snapshot = state.Snapshot;
        GameHandshakePayload? game = snapshot.Game;
        GameCurrentStatePayload? current = snapshot.CurrentState;
        if (game?.GameplayFusionPairingVersion != GameplayFusionPairingPayload.CurrentVersion || string.IsNullOrEmpty(game.SessionId)
            || current is null || !current.IronmonActive || !TrackerRequestClient.IsActiveFusionAssignmentPreparationEligible(current) || current.RunId != runId)
            return null;

        FusionAssignmentRecipePayload recipe = current.FusionAssignments!;
        string? packedPairs;
        IReadOnlyDictionary<string, string>? baseStats;
        try
        {
            packedPairs = await fusionMappings.ExportGameplayPairingAsync(recipe, cancellationToken).ConfigureAwait(false);
            baseStats = game.GameplayBaseStatVersion == GameplayFusionPairingPayload.BaseStatVersion ? fusionMappings.ExportGameplayBaseStats(recipe) : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return null;
        }

        if (packedPairs is null)
            return null;

        TrackerConnectionSnapshot latest = state.Snapshot;
        GameCurrentStatePayload? latestCurrent = latest.CurrentState;
        if (latest.Game?.SessionId != game.SessionId || latest.Game?.GameRoot != game.GameRoot || latest.Game?.GameplayFusionPairingVersion != GameplayFusionPairingPayload.CurrentVersion
            || latest.Game?.GameplayBaseStatVersion != game.GameplayBaseStatVersion
            || latestCurrent is null || !latestCurrent.IronmonActive || latestCurrent.RunId != runId || !latestCurrent.ActiveRunPreparationReady
            || JsonSerializer.Serialize(latestCurrent.FusionAssignments, TrackerJson.Options) != JsonSerializer.Serialize(recipe, TrackerJson.Options))
        {
            throw new IOException("The active game or fusion recipe changed during gameplay pairing preparation.");
        }

        return new GameplayFusionPairingPayload { SessionId = game.SessionId, Recipe = recipe, PackedPairs = packedPairs, BaseStatsVersion = baseStats is null ? 0 : GameplayFusionPairingPayload.BaseStatVersion, BaseStats = baseStats };
    }
}
