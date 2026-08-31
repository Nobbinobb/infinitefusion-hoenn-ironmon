using Ironmon.Tracker.Connection.Transport;

namespace Ironmon.Tracker.Connection.Obtainability;

/// <summary>
/// Runs the shared active-run calculation without granting access to diagnostic lookup results.
/// </summary>
/// <remarks>Initializes calculation transport and the shared native worker.</remarks>
/// <param name="session">The correlated request session.</param>
/// <param name="fusionMappings">The shared tracker-side mapping coordinator.</param>
internal sealed class TrackerRunPreparationClient(TrackerRequestSession session, PlayerFusionMappingCoordinator fusionMappings)
{
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
        RunLookupPreparationRequestPayload request = new() { Foreground = foreground };
        PokemonObtainabilityResponsePayload response = await session.SendAsync<RunLookupPreparationRequestPayload, PokemonObtainabilityResponsePayload>(TrackerCommands.PrepareRunLookup, request, runId, cancellationToken).ConfigureAwait(false);
        progress(response);
        while (response.FusionClosureWork is not null)
        {
            PlayerFusionClosureResultPayload? result = await fusionMappings.CreateResultAsync(response.FusionClosureWork, cancellationToken, runId).ConfigureAwait(false);
            request = new RunLookupPreparationRequestPayload { Foreground = foreground, FusionClosureResult = result, FusionClosureWorkerUnavailable = result is null };
            response = await session.SendAsync<RunLookupPreparationRequestPayload, PokemonObtainabilityResponsePayload>(TrackerCommands.PrepareRunLookup, request, runId, cancellationToken).ConfigureAwait(false);
            progress(response);
            if (!foreground)
                break;
        }

        return response;
    }
}
