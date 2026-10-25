using System.Windows.Threading;
using Ironmon.Tracker.Connection.Transport;

namespace Ironmon.Tracker.AchievementOverlay;

/// <summary>
/// Renders game-clocked notifications on a dedicated desktop dispatcher within the tracker process.
/// </summary>
public sealed class AchievementOverlayPresenter : IAchievementNotificationOverlay, IDisposable
{
    private const string _threadName = "Ironmon achievement overlay";
    private readonly Lock _startSync = new();
    private readonly TaskCompletionSource<Dispatcher> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<string, nint> _findGame;
    private readonly Func<nint, bool> _isForeground;
    private Thread? _thread;
    private DispatcherTimer? _timer;
    private OverlayWindow? _window;
    private OverlayArtwork? _artwork;
    private string? _presentationId;
    private string? _pendingPresentationId;
    private nint _gameWindow;
    private double _elapsed;
    private long _generation;
    private int _disposed;

    /// <summary>
    /// Creates the presenter without starting a thread or opening any window.
    /// </summary>
    public AchievementOverlayPresenter() : this(NativeGameWindow.Find, NativeGameWindow.IsForegroundGame)
    {
    }

    /// <summary>
    /// Creates a renderer with test-owned window discovery and visibility sources.
    /// </summary>
    /// <param name="findGame">Finds only the intended game window.</param>
    /// <param name="isForeground">Reads whether that game owns focus.</param>
    internal AchievementOverlayPresenter(Func<string, nint> findGame, Func<nint, bool> isForeground)
    {
        _findGame = findGame;
        _isForeground = isForeground;
    }

    /// <summary>
    /// Loads one authorized notification and creates its initially hidden overlay.
    /// </summary>
    /// <param name="presentation">The authorized title, installation and in-memory PNG buffers.</param>
    /// <param name="cancellationToken">Cancels superseded or disconnected preparation.</param>
    /// <returns>The available renderer and current foreground state.</returns>
    public async Task<AchievementOverlayState> PrepareAsync(AchievementOverlayPresentation presentation, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return new(false, false);

        long generation = Interlocked.Increment(ref _generation);
        Volatile.Write(ref _pendingPresentationId, presentation.PresentationId);
        OverlayArtwork artwork = await Task.Run(() => OverlayArtwork.FromPresentation(presentation), cancellationToken).ConfigureAwait(false);
        Dispatcher dispatcher = await GetDispatcherAsync(cancellationToken).ConfigureAwait(false);
        return await dispatcher.InvokeAsync(() =>
        {
            if (generation != Volatile.Read(ref _generation) || Volatile.Read(ref _disposed) != 0)
                return new AchievementOverlayState(false, false);

            Clear();
            _gameWindow = _findGame(presentation.GameRoot);
            _window = new OverlayWindow(_gameWindow);
            _artwork = artwork;
            _presentationId = presentation.PresentationId;
            _elapsed = 0;
            _timer!.Start();
            return new AchievementOverlayState(true, _isForeground(_gameWindow));
        }, DispatcherPriority.Normal, cancellationToken).Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Applies elapsed animation time from the game, including an unchanged value during F6 pause.
    /// </summary>
    /// <param name="presentationId">The matching presentation identity.</param>
    /// <param name="elapsedSeconds">The authoritative visible animation time in seconds.</param>
    /// <param name="cancellationToken">Cancels the bounded renderer update.</param>
    /// <returns>The current availability and foreground visibility.</returns>
    public async Task<AchievementOverlayState> UpdateAsync(string presentationId, double elapsedSeconds, CancellationToken cancellationToken)
    {
        if (!_ready.Task.IsCompletedSuccessfully || Volatile.Read(ref _disposed) != 0)
            return new(false, false);

        return await _ready.Task.Result.InvokeAsync(() =>
        {
            if (_presentationId != presentationId || _window is null || !NativeGameWindow.IsWindow(_gameWindow))
                return new AchievementOverlayState(false, false);

            _elapsed = elapsedSeconds;
            RenderCurrent();
            return new AchievementOverlayState(_window is not null, _isForeground(_gameWindow));
        }, DispatcherPriority.Normal, cancellationToken).Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Removes only the named presentation without disturbing a newer one.
    /// </summary>
    /// <param name="presentationId">The presentation to remove.</param>
    public void Stop(string presentationId)
    {
        if (Volatile.Read(ref _pendingPresentationId) == presentationId)
            Interlocked.Increment(ref _generation);

        if (_ready.Task.IsCompletedSuccessfully && !_ready.Task.Result.HasShutdownStarted)
            _ready.Task.Result.BeginInvoke(() => { if (_presentationId == presentationId) Clear(); });
    }

    /// <summary>
    /// Cancels pending preparation and releases current presentation resources on disconnect.
    /// </summary>
    public void Reset()
    {
        Interlocked.Increment(ref _generation);
        Volatile.Write(ref _pendingPresentationId, null);
        if (_ready.Task.IsCompletedSuccessfully && !_ready.Task.Result.HasShutdownStarted)
            _ready.Task.Result.BeginInvoke(Clear);
    }

    /// <summary>
    /// Stops the renderer thread and releases its native resources during tracker shutdown.
    /// </summary>
    /// <returns>A task completing after the owned dispatcher has been asked to stop.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Reset();
        if (_ready.Task.IsCompletedSuccessfully)
        {
            Dispatcher dispatcher = _ready.Task.Result;
            if (!dispatcher.HasShutdownStarted)
            {
                await dispatcher.InvokeAsync(Clear).Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            }
        }
    }

    /// <summary>
    /// Supports synchronous application-container cleanup after the normal asynchronous shutdown path.
    /// </summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>
    /// Starts the owned STA dispatcher only when a notification needs it.
    /// </summary>
    /// <param name="cancellationToken">Cancels waiting for renderer startup.</param>
    /// <returns>The initialized notification dispatcher.</returns>
    private Task<Dispatcher> GetDispatcherAsync(CancellationToken cancellationToken)
    {
        lock (_startSync)
        {
            if (_thread is null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = _threadName };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
            }
        }

        return _ready.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Pumps the dedicated notification dispatcher, independent of the tracker's main UI.
    /// </summary>
    private void Run()
    {
        try
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += HandleTick;
            _ready.TrySetResult(dispatcher);
            Dispatcher.Run();
        }
        catch (Exception error)
        {
            _ready.TrySetException(error);
        }
        finally
        {
            Clear();
        }
    }

    /// <summary>
    /// Tracks game movement, visibility and physical pixel size without advancing notification time.
    /// </summary>
    /// <param name="sender">The render timer.</param>
    /// <param name="args">The timer tick.</param>
    private void HandleTick(object? sender, EventArgs args)
    {
        try
        {
            RenderCurrent();
        }
        catch
        {
            Clear();
        }
    }

    /// <summary>
    /// Draws only the most recent game-supplied phase at the current physical window size.
    /// </summary>
    private void RenderCurrent()
    {
        if (_window is null || _artwork is null)
            return;

        if (!NativeGameWindow.IsWindow(_gameWindow))
        {
            Clear();
            return;
        }

        if (!_isForeground(_gameWindow))
        {
            _window.Hide();
            return;
        }

        OverlayPlacement? placement = NativeGameWindow.Measure(_gameWindow);
        if (placement is not null)
            _window.ShowAligned(_artwork, placement, false, SlideFraction(_elapsed));
    }

    /// <summary>
    /// Calculates the approved quick entry, hold and exit animation from game time.
    /// </summary>
    /// <param name="elapsedSeconds">Elapsed visible notification time in seconds.</param>
    /// <returns>The visible slide fraction between zero and one.</returns>
    internal static double SlideFraction(double elapsedSeconds)
        => elapsedSeconds < 0.18 ? 1 - Math.Pow(1 - Math.Clamp(elapsedSeconds / 0.18, 0, 1), 3) : elapsedSeconds < 2.78 ? 1 : Math.Pow(1 - Math.Clamp((elapsedSeconds - 2.78) / 0.18, 0, 1), 3);

    /// <summary>
    /// Closes only the renderer-owned window and releases the current images.
    /// </summary>
    private void Clear()
    {
        _timer?.Stop();
        _window?.Close();
        _window = null;
        _artwork = null;
        _presentationId = null;
        _gameWindow = 0;
    }
}
