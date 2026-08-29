namespace Ironmon.Tracker.Connection.Transport;

/// <summary>
/// Applies inbound game events and recovered state to tracker-owned stores.
/// </summary>
internal sealed class TrackerGameEventProcessor
{
    private readonly AreaDiscoveryStore _areaDiscoveries;
    private readonly CompletedRunArchive _completedRuns;
    private readonly TrackerKnowledgeStore _knowledge;
    private readonly TrackerRequestClient _requestClient;
    private readonly TrackerRequestSession _requestSession;
    private readonly TrackerRunState _runState;
    private readonly TrackerConnectionState _state;

    /// <summary>
    /// Initializes the inbound game event processor.
    /// </summary>
    /// <param name="state">The shared connection state.</param>
    /// <param name="runState">The shared live run state.</param>
    /// <param name="knowledge">The tracker-owned discovery and annotation store.</param>
    /// <param name="areaDiscoveries">The tracker-owned area discovery store.</param>
    /// <param name="completedRuns">The tracker-owned completed-run recipe archive.</param>
    /// <param name="requestSession">The active protocol request session.</param>
    /// <param name="requestClient">The public request client receiving game-owned transitions.</param>
    internal TrackerGameEventProcessor(TrackerConnectionState state, TrackerRunState runState, TrackerKnowledgeStore knowledge, AreaDiscoveryStore areaDiscoveries, CompletedRunArchive completedRuns, TrackerRequestSession requestSession, TrackerRequestClient requestClient)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(runState);
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(areaDiscoveries);
        ArgumentNullException.ThrowIfNull(completedRuns);
        ArgumentNullException.ThrowIfNull(requestSession);
        ArgumentNullException.ThrowIfNull(requestClient);
        _state = state;
        _runState = runState;
        _knowledge = knowledge;
        _areaDiscoveries = areaDiscoveries;
        _completedRuns = completedRuns;
        _requestSession = requestSession;
        _requestClient = requestClient;
    }

    /// <summary>
    /// Selects tracker-owned knowledge for the run declared by a completed handshake.
    /// </summary>
    /// <param name="runId">The connected run identifier, or null outside a run.</param>
    internal void SelectRun(string? runId)
        => _knowledge.SelectRun(runId);

    /// <summary>
    /// Applies a complete current-state snapshot recovered after connection or run start.
    /// </summary>
    /// <param name="game">The connected game handshake.</param>
    /// <param name="state">The authoritative current state.</param>
    internal void RecoverCurrentState(GameHandshakePayload game, GameCurrentStatePayload state)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(state);
        if (!state.ActiveRunPreparationReady)
            _requestClient.ObtainabilityProgress.Reset();

        _requestClient.PrepareActiveFusionAssignments(state);
        _knowledge.SelectRun(state.RunId);
        _state.Publish(TrackerConnectionStatus.Connected, game, state);
        _runState.Recover(state);
        ObserveRecoveredKnowledge(state);
        ObserveCompletedRun(state);
    }

    /// <summary>
    /// Applies one inbound game event in protocol order.
    /// </summary>
    /// <param name="message">The inbound event message.</param>
    /// <param name="game">The connected game handshake.</param>
    /// <param name="cancellationToken">The token that cancels an acknowledgment write.</param>
    /// <returns>A task representing event processing.</returns>
    /// <exception cref="ArgumentException">Thrown when the message is not an event.</exception>
    internal async Task ProcessAsync(TrackerMessage message, GameHandshakePayload game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(game);
        if (message.Type != TrackerMessageType.Event)
            throw new ArgumentException("The game event processor requires an event message.", nameof(message));

        if (message.Event is TrackerEvents.RunStarted or TrackerEvents.ActiveRunPreparationReady)
        {
            RecoverCurrentState(game, TrackerJson.DeserializePayload<GameCurrentStatePayload>(message.Payload));
            return;
        }

        if (message.Event == TrackerEvents.RunCompleted)
        {
            RunCompletedEventPayload completion = TrackerJson.DeserializePayload<RunCompletedEventPayload>(message.Payload);
            _completedRuns.Store(completion.Recipe, completion.RequestArchiveSelection);
            PublishCompletedRunState(message, game, completion.Recipe);
            return;
        }

        if(message.Event == TrackerEvents.AreaDiscovery)
        {
            await PersistAndAcknowledgeAreaDiscoveryAsync(message, cancellationToken).ConfigureAwait(false);
            return;
        }

        ApplyLiveEvent(message);
    }

    /// <summary>
    /// Publishes final attempt totals immediately when the connected run completes.
    /// </summary>
    /// <param name="message">The completion event envelope.</param>
    /// <param name="game">The connected game handshake.</param>
    /// <param name="recipe">The completed run recipe.</param>
    private void PublishCompletedRunState(TrackerMessage message, GameHandshakePayload game, CompletedRunRecipePayload recipe)
    {
        GameCurrentStatePayload? current = _state.Snapshot.CurrentState;
        if (current is null || !string.Equals(current.RunId, recipe.RunId, StringComparison.Ordinal))
            return;

        RunStatisticsPayload? statistics = recipe.Statistics ?? current.AttemptStatistics;
        GameCurrentStatePayload completed = new(current.IronmonActive, current.RunId, current.BattleId, message.Sequence ?? current.Sequence, current.Battle, current.Player, current.Enemies, current.StarterSelection, statistics, recipe, current.TypeCoverage, current.FusionAssignments, current.ActiveRunPreparationReady);
        _state.Publish(TrackerConnectionStatus.Connected, game, completed);
    }

    /// <summary>
    /// Persists one idempotent area discovery before acknowledging it to the game.
    /// </summary>
    /// <param name="message">The received discovery event.</param>
    /// <param name="cancellationToken">The token that stops the acknowledgment write.</param>
    /// <returns>A task representing the acknowledgment write.</returns>
    private async Task PersistAndAcknowledgeAreaDiscoveryAsync(TrackerMessage message, CancellationToken cancellationToken)
    {
        string runId = message.RunId ?? throw new TrackerProtocolException("An area discovery requires a run identifier.");
        AreaDiscoveryPackagePayload package = TrackerJson.DeserializePayload<AreaDiscoveryPackagePayload>(message.Payload);
        bool persisted;
        try
        {
            persisted = _areaDiscoveries.RecordPackage(runId, package);
        }
        catch (ArgumentException exception)
        {
            throw new TrackerProtocolException("The area discovery package is invalid.", exception);
        }

        if (!persisted)
            return;

        AreaDiscoveryAcknowledgmentPayload acknowledgment = new() { PackageId = package.PackageId };
        await _requestSession.SendEventAsync(TrackerEvents.AreaDiscoveryAcknowledged, acknowledgment, runId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies one supported live game event to the tracker run-state store.
    /// </summary>
    /// <param name="message">The validated game event.</param>
    private void ApplyLiveEvent(TrackerMessage message)
    {
        _knowledge.SelectRun(message.RunId);
        Action apply = message.Event switch
        {
            TrackerEvents.BattleStarted => () => _runState.StartBattle(TrackerJson.DeserializePayload<BattleSnapshot>(message.Payload)),
            TrackerEvents.BattleEnded => _runState.EndBattle,
            TrackerEvents.StarterSelectionChanged => () => _runState.UpdateStarterSelection(TrackerJson.DeserializePayload<StarterSelectionSnapshot>(message.Payload)),
            TrackerEvents.SeededRunImportStatus => () => _requestClient.PublishSeededRunImportStatus(TrackerJson.DeserializePayload<SeededRunImportStatusPayload>(message.Payload)),
            TrackerEvents.PlayerSentOut or TrackerEvents.PlayerStateChanged => () => ApplyPlayerUpdate(message),
            TrackerEvents.PlayerMoveMenuOpened => () => _runState.OpenPlayerMoveMenu(TrackerJson.DeserializePayload<PlayerMoveMenuOpenedPayload>(message.Payload)),
            TrackerEvents.EnemySentOut => () => ApplyEnemyUpdate(message, true),
            TrackerEvents.EnemyStateChanged => () => ApplyEnemyUpdate(message, false),
            TrackerEvents.EnemyMoveUsed => () => _knowledge.ObserveEnemyMove(TrackerJson.DeserializePayload<EnemyMoveUsedPayload>(message.Payload)),
            TrackerEvents.EnemyAbilityRevealed => () => _knowledge.ObserveEnemyAbility(TrackerJson.DeserializePayload<EnemyAbilityRevealedPayload>(message.Payload)),
            _ => () => { }
        };

        apply();
    }

    /// <summary>
    /// Applies a complete player update and its legal move discoveries.
    /// </summary>
    /// <param name="message">The player update event.</param>
    private void ApplyPlayerUpdate(TrackerMessage message)
    {
        PlayerPokemonSnapshot player = TrackerJson.DeserializePayload<PlayerPokemonSnapshot>(message.Payload);
        _runState.UpdatePlayer(player);
        _knowledge.ObservePlayer(player);
    }

    /// <summary>
    /// Applies an enemy update and remembers its most recently observed move.
    /// </summary>
    /// <param name="message">The enemy state event.</param>
    /// <param name="sentOut">Whether the event announces a newly sent-out opponent.</param>
    private void ApplyEnemyUpdate(TrackerMessage message, bool sentOut)
    {
        EnemyPokemonSnapshot enemy = TrackerJson.DeserializePayload<EnemyPokemonSnapshot>(message.Payload);
        _runState.UpdateEnemy(enemy, sentOut);
        _knowledge.ObserveEnemy(enemy);
        ObserveEnemyMove(enemy);
    }

    /// <summary>
    /// Restores legally observable knowledge included in a complete game-state snapshot.
    /// </summary>
    /// <param name="state">The recovered game state.</param>
    private void ObserveRecoveredKnowledge(GameCurrentStatePayload state)
    {
        if (state.Player is not null)
            _knowledge.ObservePlayer(state.Player);

        foreach (EnemyPokemonSnapshot enemy in state.Enemies)
        {
            _knowledge.ObserveEnemy(enemy);
            ObserveEnemyMove(enemy);
        }
    }

    /// <summary>
    /// Persists the completed-run recipe included in recovered game state.
    /// </summary>
    /// <param name="state">The recovered game state.</param>
    private void ObserveCompletedRun(GameCurrentStatePayload state)
    {
        if (state.CompletedRun is not null)
            _completedRuns.Store(state.CompletedRun);
    }

    /// <summary>
    /// Remembers a move included in an enemy snapshot when one is present.
    /// </summary>
    /// <param name="enemy">The complete legal enemy snapshot.</param>
    private void ObserveEnemyMove(EnemyPokemonSnapshot enemy)
    {
        if (enemy.LastMove is null)
            return;

        EnemyMoveUsedPayload observation = new()
        {
            EnemyId = enemy.EnemyId,
            SpeciesId = enemy.SpeciesId,
            EnemyLevel = enemy.Level,
            Move = enemy.LastMove
        };

        _knowledge.ObserveEnemyMove(observation);
    }
}
