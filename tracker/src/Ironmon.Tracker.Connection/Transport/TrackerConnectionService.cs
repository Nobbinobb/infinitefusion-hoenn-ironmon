using System.Net;
using System.Net.Sockets;

namespace Ironmon.Tracker.Connection.Transport;

/// <summary>
/// Accepts one persistent game connection on the tracker loopback endpoint.
/// </summary>
public sealed class TrackerConnectionService : IAsyncDisposable
{
    private readonly Lock _lifecycleSync = new();
    private readonly CompletedRunArchive _completedRuns;
    private readonly AreaDiscoveryStore _areaDiscoveries;
    private readonly TrackerDiagnosticsStore _diagnostics;
    private readonly TrackerKnowledgeStore _knowledge;
    private readonly TrackerConnectionOptions _options;
    private readonly TrackerRequestSession _requests;
    private readonly TrackerRunState _runState;
    private readonly TrackerConnectionState _state;
    private CancellationTokenSource? _cancellation;
    private TcpListener? _listener;
    private Task? _runTask;
    private bool _disposed;

    /// <summary>
    /// Initializes the tracker connection service.
    /// </summary>
    /// <param name="options">The listener and handshake options.</param>
    /// <param name="diagnostics">The bounded connection and protocol diagnostic store.</param>
    /// <param name="state">The shared connection state.</param>
    /// <param name="runState">The shared live run state.</param>
    /// <param name="knowledge">The tracker-owned discovery and annotation store.</param>
    /// <param name="areaDiscoveries">The tracker-owned area discovery store.</param>
    /// <param name="completedRuns">The tracker-owned completed-run recipe archive.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public TrackerConnectionService(TrackerConnectionOptions options, TrackerDiagnosticsStore diagnostics, TrackerConnectionState state, TrackerRunState runState, TrackerKnowledgeStore knowledge, AreaDiscoveryStore areaDiscoveries, CompletedRunArchive completedRuns)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(runState);
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(areaDiscoveries);
        ArgumentNullException.ThrowIfNull(completedRuns);
        _options = options;
        _diagnostics = diagnostics;
        _requests = new TrackerRequestSession(diagnostics);
        Requests = new TrackerRequestClient(_requests, options, state, areaDiscoveries);
        _state = state;
        _runState = runState;
        _knowledge = knowledge;
        _areaDiscoveries = areaDiscoveries;
        _completedRuns = completedRuns;
    }

    /// <summary>
    /// Gets the actual bound loopback port after the listener starts.
    /// </summary>
    public int BoundPort { get; private set; }

    /// <summary>
    /// Gets the validated game request client attached to this connection.
    /// </summary>
    public TrackerRequestClient Requests { get; }

    /// <summary>
    /// Gets whether both the tracker launch mode and connected game authorize debug access.
    /// </summary>
    public bool DebugAuthorized => Requests.DebugAuthorized;

    /// <summary>
    /// Starts listening for the game without blocking the calling thread.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown after the service is disposed.</exception>
    public void Start()
    {
        lock (_lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runTask is not null)
                return;

            try
            {
                _cancellation = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Loopback, _options.Port);
                _listener.Start();
                BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _state.Publish(TrackerConnectionStatus.Waiting);
                _diagnostics.RecordLifecycle(TrackerDiagnosticConstants.Listening, $"{IPAddress.Loopback}:{BoundPort}");
                _runTask = RunAsync(_listener, _cancellation.Token);
            }
            catch (SocketException exception)
            {
                _cancellation?.Dispose();
                _cancellation = null;
                _listener = null;
                BoundPort = 0;
                _state.Publish(TrackerConnectionStatus.Error, lastError: exception.Message);
                _diagnostics.RecordError(exception);
            }
        }
    }

    /// <summary>
    /// Stops accepting and processing game connections.
    /// </summary>
    /// <returns>A task representing listener shutdown.</returns>
    public async ValueTask StopAsync()
    {
        Task? runTask;
        CancellationTokenSource? cancellation;
        lock (_lifecycleSync)
        {
            runTask = _runTask;
            cancellation = _cancellation;
            _runTask = null;
            _cancellation = null;
            cancellation?.Cancel();
            _listener?.Stop();
            _listener = null;
            BoundPort = 0;
        }

        if (runTask is null)
            return;

        await runTask.ConfigureAwait(false);
        cancellation?.Dispose();
        _state.Publish(TrackerConnectionStatus.Stopped);
        _diagnostics.RecordLifecycle(TrackerDiagnosticConstants.Stopped, "Tracker listener stopped.");
    }

    /// <summary>
    /// Stops the listener and releases lifecycle resources.
    /// </summary>
    /// <returns>A task representing asynchronous disposal.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await StopAsync().ConfigureAwait(false);
        _requests.Dispose();
        _disposed = true;
    }

    /// <summary>
    /// Accepts and processes game clients until listener shutdown.
    /// </summary>
    /// <param name="listener">The active loopback listener.</param>
    /// <param name="cancellationToken">The token that stops the listener.</param>
    /// <returns>A task representing the listener loop.</returns>
    private async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await HandleClientAsync(client, cancellationToken).ConfigureAwait(false);
                if (!cancellationToken.IsCancellationRequested)
                {
                    _state.Publish(TrackerConnectionStatus.Waiting);
                    _diagnostics.RecordLifecycle(TrackerDiagnosticConstants.Disconnected, "Game connection closed.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is IOException or SocketException or TrackerProtocolException)
            {
                _state.Publish(TrackerConnectionStatus.Error, lastError: exception.Message);
                _diagnostics.RecordError(exception);
            }
            finally
            {
                client.Dispose();
            }
        }
    }

    /// <summary>
    /// Completes the handshake and processes one persistent game connection.
    /// </summary>
    /// <param name="client">The accepted loopback game client.</param>
    /// <param name="cancellationToken">The token that stops the connection.</param>
    /// <returns>A task representing the client connection.</returns>
    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        client.NoDelay = true;
        _state.Publish(TrackerConnectionStatus.Handshaking);
        _diagnostics.RecordLifecycle(TrackerDiagnosticConstants.Handshaking, client.Client.RemoteEndPoint?.ToString() ?? "Loopback game client");
        NetworkStream stream = client.GetStream();
        using TrackerMessageReader reader = new(stream, leaveOpen: true);
        await using TrackerMessageWriter writer = new(stream, leaveOpen: true);

        TrackerMessage handshakeMessage = await ReadHandshakeAsync(reader, cancellationToken).ConfigureAwait(false);
        _diagnostics.RecordIncoming(handshakeMessage);
        GameHandshakePayload game = ValidateGameHandshake(handshakeMessage);
        _knowledge.SelectRun(game.RunId);
        TrackerHandshakePayload tracker = new(_options.TrackerVersion, _options.DebugRequested, _options.AutoSelectStarter, _options.FavoriteSpeciesIds);
        TrackerMessage trackerHandshake = TrackerMessageFactory.CreateEvent(TrackerEvents.TrackerConnected, TrackerProtocol.InitialEventSequence, tracker, game.RunId, game.BattleId);
        await writer.WriteAsync(trackerHandshake, cancellationToken).ConfigureAwait(false);
        _diagnostics.RecordOutgoing(trackerHandshake);

        string requestId = Guid.NewGuid().ToString(TrackerProtocol.RequestIdFormat);
        Dictionary<string, object?> emptyPayload = [];
        TrackerMessage request = TrackerMessageFactory.CreateRequest(requestId, TrackerCommands.CurrentState, emptyPayload, game.RunId, game.BattleId);
        await writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        _diagnostics.RecordOutgoing(request);
        _requests.Connect(writer);

        _state.Publish(TrackerConnectionStatus.Connected, game);
        _diagnostics.RecordLifecycle(TrackerDiagnosticConstants.Connected, $"Infinite Fusion {game.GameVersion} · Ironmon {game.IronmonVersion}");

        try
        {
            await ReadMessagesAsync(reader, requestId, game, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ClearRequestSession();
        }
    }

    /// <summary>
    /// Reads the first client message within the configured handshake timeout.
    /// </summary>
    /// <param name="reader">The persistent message reader.</param>
    /// <param name="cancellationToken">The token that stops the connection.</param>
    /// <returns>The non-null handshake message.</returns>
    /// <exception cref="TrackerProtocolException">Thrown when the client closes before its handshake.</exception>
    private async Task<TrackerMessage> ReadHandshakeAsync(TrackerMessageReader reader, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.HandshakeTimeout);
        try
        {
            TrackerMessage? message = await reader.ReadAsync(timeout.Token).ConfigureAwait(false);
            return message ?? throw new TrackerProtocolException("The game closed before sending its handshake.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TrackerProtocolException("The game handshake timed out.");
        }
    }

    /// <summary>
    /// Validates and deserializes the game handshake event.
    /// </summary>
    /// <param name="message">The first client message.</param>
    /// <returns>The validated game handshake.</returns>
    /// <exception cref="TrackerProtocolException">Thrown when the message is not a game handshake.</exception>
    private static GameHandshakePayload ValidateGameHandshake(TrackerMessage message)
    {
        if (message.Type != TrackerMessageType.Event || message.Event != TrackerEvents.GameConnected)
            throw new TrackerProtocolException("The first game message must be game_connected.");

        return TrackerJson.DeserializePayload<GameHandshakePayload>(message.Payload);
    }

    /// <summary>
    /// Reads messages until the game disconnects and applies connection-level responses.
    /// </summary>
    /// <param name="reader">The persistent message reader.</param>
    /// <param name="currentStateRequestId">The current-state correlation identifier.</param>
    /// <param name="game">The connected game's handshake.</param>
    /// <param name="cancellationToken">The token that stops the connection.</param>
    /// <returns>A task representing the persistent read loop.</returns>
    private async Task ReadMessagesAsync(TrackerMessageReader reader, string currentStateRequestId, GameHandshakePayload game, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TrackerMessage? message = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (message is null)
                return;

            _diagnostics.RecordIncoming(message);
            if (message.Type == TrackerMessageType.Event && message.Event == TrackerEvents.RunStarted)
            {
                GameCurrentStatePayload startedState = TrackerJson.DeserializePayload<GameCurrentStatePayload>(message.Payload);
                _knowledge.SelectRun(startedState.RunId);
                _state.Publish(TrackerConnectionStatus.Connected, game, startedState);
                _runState.Recover(startedState);
                ObserveRecoveredKnowledge(startedState);
                ObserveCompletedRun(startedState);
                continue;
            }

            if (message.Type == TrackerMessageType.Event && message.Event == TrackerEvents.RunCompleted)
            {
                _completedRuns.Store(TrackerJson.DeserializePayload<CompletedRunRecipePayload>(message.Payload));
                continue;
            }

            if (message.Type == TrackerMessageType.Event && message.Event == TrackerEvents.AreaDiscovery)
            {
                await PersistAndAcknowledgeAreaDiscoveryAsync(message, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (message.Type == TrackerMessageType.Event)
            {
                ApplyGameEvent(message);
                continue;
            }

            if (message.Type == TrackerMessageType.Response && message.RequestId != currentStateRequestId)
            {
                _requests.TryComplete(message);
                continue;
            }

            if (message.Type != TrackerMessageType.Response || message.RequestId != currentStateRequestId)
                continue;

            if (message.Success != true)
                throw new TrackerProtocolException(message.Error?.Message ?? "The current_state request failed.");

            GameCurrentStatePayload currentState = TrackerJson.DeserializePayload<GameCurrentStatePayload>(message.Payload);
            _knowledge.SelectRun(currentState.RunId);
            _state.Publish(TrackerConnectionStatus.Connected, game, currentState);
            _runState.Recover(currentState);
            ObserveRecoveredKnowledge(currentState);
            ObserveCompletedRun(currentState);
        }
    }

    /// <summary>
    /// Persists one idempotent area discovery before acknowledging it to the game.
    /// </summary>
    /// <param name="message">The received discovery event.</param>
    /// <param name="cancellationToken">The token that stops the connection.</param>
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
        await _requests.SendEventAsync(TrackerEvents.AreaDiscoveryAcknowledged, acknowledgment, runId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Clears the active request writer and fails outstanding requests after disconnection.
    /// </summary>
    private void ClearRequestSession()
    {
        Requests.Disconnect();
    }

    /// <summary>
    /// Applies one supported live game event to the tracker run-state store.
    /// </summary>
    /// <param name="message">The validated game event.</param>
    private void ApplyGameEvent(TrackerMessage message)
    {
        _knowledge.SelectRun(message.RunId);
        Action apply = message.Event switch
        {
            TrackerEvents.BattleStarted => () => _runState.StartBattle(TrackerJson.DeserializePayload<BattleSnapshot>(message.Payload)),
            TrackerEvents.BattleEnded => _runState.EndBattle,
            TrackerEvents.StarterSelectionChanged => () => _runState.UpdateStarterSelection(TrackerJson.DeserializePayload<StarterSelectionSnapshot>(message.Payload)),
            TrackerEvents.PlayerSentOut or TrackerEvents.PlayerStateChanged => () => ApplyPlayerUpdate(message),
            TrackerEvents.PlayerMoveMenuOpened => () => _runState.OpenPlayerMoveMenu(TrackerJson.DeserializePayload<PlayerMoveMenuOpenedPayload>(message.Payload)),
            TrackerEvents.EnemySentOut or TrackerEvents.EnemyStateChanged => () => ApplyEnemyUpdate(message),
            TrackerEvents.EnemyMoveUsed => () =>
                _knowledge.ObserveEnemyMove(TrackerJson.DeserializePayload<EnemyMoveUsedPayload>(message.Payload)),
            TrackerEvents.EnemyAbilityRevealed => () =>
                _knowledge.ObserveEnemyAbility(TrackerJson.DeserializePayload<EnemyAbilityRevealedPayload>(message.Payload)),
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
    private void ApplyEnemyUpdate(TrackerMessage message)
    {
        EnemyPokemonSnapshot enemy = TrackerJson.DeserializePayload<EnemyPokemonSnapshot>(message.Payload);
        _runState.UpdateEnemy(enemy);
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
