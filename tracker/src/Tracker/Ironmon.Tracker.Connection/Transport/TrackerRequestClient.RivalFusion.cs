namespace Ironmon.Tracker.Connection.Transport;

/// <summary>
/// Delivers exact single-pair rival assignments through the shared native mapping worker.
/// </summary>
public sealed partial class TrackerRequestClient
{
    /// <summary>
    /// Starts one bounded native rival operation without holding up the inbound game event loop.
    /// </summary>
    /// <param name="message">The preparation or single-pair event.</param>
    /// <param name="game">The connected process identity.</param>
    /// <param name="cancellationToken">The connection lifetime token.</param>
    internal void HandleRivalFusion(TrackerMessage message, GameHandshakePayload game, CancellationToken cancellationToken)
    {
        RivalFusionWorkPayload work = TrackerJson.DeserializePayload<RivalFusionWorkPayload>(message.Payload);
        bool requested = message.Event == TrackerEvents.RivalFusionRequested;
        if (!IsCurrentRivalFusion(work, message.RunId, game) || requested != !string.IsNullOrWhiteSpace(work.RequestId))
            return;

        _ = CompleteRivalFusionAsync(work, message.RunId!, game, requested, cancellationToken);
    }

    /// <summary>
    /// Rejects work or replies belonging to a previous process or run.
    /// </summary>
    /// <param name="work">The originating work description.</param>
    /// <param name="runId">The event's run identity.</param>
    /// <param name="game">The originating game connection.</param>
    /// <returns>Whether this operation still belongs to the connected active run.</returns>
    private bool IsCurrentRivalFusion(RivalFusionWorkPayload work, string? runId, GameHandshakePayload game)
    {
        TrackerConnectionSnapshot current = _state.Snapshot;
        return current.Status == TrackerConnectionStatus.Connected && !string.IsNullOrWhiteSpace(runId)
            && !string.IsNullOrWhiteSpace(work.SessionId) && work.SessionId == game.SessionId
            && current.Game?.SessionId == game.SessionId && current.Game?.GameRoot == game.GameRoot
            && current.CurrentState?.RunId == runId && current.CurrentState.IronmonActive;
    }

    /// <summary>
    /// Calculates one exact assignment and delivers it only to its originating run.
    /// </summary>
    /// <param name="work">The pinned recipe and optional materials.</param>
    /// <param name="runId">The originating run.</param>
    /// <param name="game">The originating connection identity.</param>
    /// <param name="requested">Whether a correlated result is required.</param>
    /// <param name="cancellationToken">The connection lifetime token.</param>
    /// <returns>A task completing after preparation and any reply, with failures contained within this background operation.</returns>
    private async Task CompleteRivalFusionAsync(RivalFusionWorkPayload work, string runId, GameHandshakePayload game, bool requested, CancellationToken cancellationToken)
    {
        int? species = null;
        string? error = null;
        try
        {
            if (requested && (work.BodyId is null || work.HeadId is null))
                throw new ArgumentException("The rival fusion request is missing its materials.");

            species = await _fusionMappings.MapRivalFusionAsync(work.Recipe, requested ? work.BodyId!.Value : 1, requested ? work.HeadId!.Value : 1, cancellationToken).ConfigureAwait(false);
            if (species is null)
                error = "The tracker cannot reproduce this run's fusion recipe. Restart with the matching tracker build.";
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }

        if (!requested || !IsCurrentRivalFusion(work, runId, game))
            return;

        RivalFusionResultPayload result = new()
        {
            SessionId = work.SessionId,
            RequestId = work.RequestId!,
            BodyId = work.BodyId ?? 0,
            HeadId = work.HeadId ?? 0,
            SpeciesNumber = species,
            Error = error
        };
        try
        {
            await _session.SendEventAsync(TrackerEvents.RivalFusionResult, result, runId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A disconnected game will reject this request; the next connection uses a fresh request identity.
        }
    }
}
