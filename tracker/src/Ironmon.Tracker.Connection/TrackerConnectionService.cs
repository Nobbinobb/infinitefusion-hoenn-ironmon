using System.Net;
using System.Net.Sockets;
using Ironmon.Tracker.Protocol;

namespace Ironmon.Tracker.Connection;

/// <summary>
/// Accepts one persistent game connection on the tracker loopback endpoint.
/// </summary>
public sealed class TrackerConnectionService : IAsyncDisposable
{
    private readonly object _lifecycleSync = new();
    private readonly TrackerKnowledgeStore _knowledge;
    private readonly TrackerConnectionOptions _options;
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
    /// <param name="state">The shared connection state.</param>
    /// <param name="runState">The shared live run state.</param>
    /// <param name="knowledge">The tracker-owned discovery and annotation store.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public TrackerConnectionService(TrackerConnectionOptions options, TrackerConnectionState state, TrackerRunState runState, TrackerKnowledgeStore knowledge)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(runState);
        ArgumentNullException.ThrowIfNull(knowledge);
        _options = options;
        _state = state;
        _runState = runState;
        _knowledge = knowledge;
    }

    /// <summary>
    /// Gets the actual bound loopback port after the listener starts.
    /// </summary>
    public int BoundPort { get; private set; }

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
                _runTask = RunAsync(_listener, _cancellation.Token);
            }
            catch (SocketException exception)
            {
                _cancellation?.Dispose();
                _cancellation = null;
                _listener = null;
                BoundPort = 0;
                _state.Publish(TrackerConnectionStatus.Error, lastError: exception.Message);
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
                    _state.Publish(TrackerConnectionStatus.Waiting);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is IOException or SocketException or TrackerProtocolException)
            {
                _state.Publish(TrackerConnectionStatus.Error, lastError: exception.Message);
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
        NetworkStream stream = client.GetStream();
        using TrackerMessageReader reader = new(stream, leaveOpen: true);
        await using TrackerMessageWriter writer = new(stream, leaveOpen: true);

        TrackerMessage handshakeMessage = await ReadHandshakeAsync(reader, cancellationToken).ConfigureAwait(false);
        GameHandshakePayload game = ValidateGameHandshake(handshakeMessage);
        _knowledge.SelectRun(game.RunId);
        TrackerHandshakePayload tracker = new(_options.TrackerVersion, _options.DebugRequested);
        TrackerMessage trackerHandshake = TrackerMessageFactory.CreateEvent("tracker_connected", 0, tracker, game.RunId, game.BattleId);
        await writer.WriteAsync(trackerHandshake, cancellationToken).ConfigureAwait(false);

        string requestId = Guid.NewGuid().ToString("N");
        Dictionary<string, object?> emptyPayload = [];
        TrackerMessage request = TrackerMessageFactory.CreateRequest(requestId, "current_state", emptyPayload, game.RunId, game.BattleId);
        await writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        _state.Publish(TrackerConnectionStatus.Connected, game);

        await ReadMessagesAsync(reader, requestId, game, cancellationToken).ConfigureAwait(false);
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
        if (message.Type != TrackerMessageType.Event || message.Event != "game_connected")
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

            if (message.Type == TrackerMessageType.Event && message.Event == "run_started")
            {
                GameCurrentStatePayload startedState = TrackerJson.DeserializePayload<GameCurrentStatePayload>(message.Payload);
                _knowledge.SelectRun(startedState.RunId);
                _state.Publish(TrackerConnectionStatus.Connected, game, startedState);
                _runState.Recover(startedState);
                ObserveRecoveredKnowledge(startedState);
                continue;
            }

            if (message.Type == TrackerMessageType.Event)
            {
                ApplyGameEvent(message);
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
        }
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
            "battle_started" => () => _runState.StartBattle(TrackerJson.DeserializePayload<BattleSnapshot>(message.Payload)),
            "battle_ended" => _runState.EndBattle,
            "player_sent_out" or "player_state_changed" => () => ApplyPlayerUpdate(message),
            "player_move_menu_opened" => () => _runState.OpenPlayerMoveMenu(TrackerJson.DeserializePayload<PlayerMoveMenuOpenedPayload>(message.Payload)),
            "enemy_sent_out" or "enemy_state_changed" => () => ApplyEnemyUpdate(message),
            "enemy_move_used" => () =>
                _knowledge.ObserveEnemyMove(TrackerJson.DeserializePayload<EnemyMoveUsedPayload>(message.Payload)),
            "enemy_ability_revealed" => () =>
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
