using Microsoft.UI.Windowing;

namespace Ironmon.Tracker.App;

/// <summary>
/// Temporarily expands the fixed tracker window to its current monitor and restores its compact placement.
/// </summary>
public sealed class TrackerWindowService
{
    private Window? _window;
    private OverlappedPresenter? _presenter;
    private double _normalHeight;
    private double _normalWidth;
    private double _normalX;
    private double _normalY;

    /// <summary>
    /// Initializes native tracker-window coordination.
    /// </summary>
    public TrackerWindowService()
    {
    }

    /// <summary>
    /// Gets whether the native tracker window currently occupies its monitor work area.
    /// </summary>
    public bool IsMaximized { get; private set; }

    /// <summary>
    /// Attaches the native window and keeps normal tracker resizing disabled.
    /// </summary>
    /// <param name="window">The MAUI tracker window.</param>
    /// <param name="presenter">The native overlapped-window presenter.</param>
    internal void Attach(Window window, OverlappedPresenter presenter)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(presenter);
        _window = window;
        _presenter = presenter;
        presenter.IsMaximizable = false;
        presenter.IsResizable = false;
    }

    /// <summary>
    /// Toggles the tracker between its compact placement and the current monitor work area.
    /// </summary>
    /// <returns>Whether the window is maximized after the operation.</returns>
    public Task<bool> ToggleMaximizedAsync()
        => MainThread.InvokeOnMainThreadAsync(ToggleMaximized);

    /// <summary>
    /// Restores the compact tracker window when it is currently maximized.
    /// </summary>
    /// <returns>A task representing the native window operation.</returns>
    public Task RestoreAsync()
        => MainThread.InvokeOnMainThreadAsync(Restore);

    /// <summary>
    /// Performs the native maximize or restore operation on the UI thread.
    /// </summary>
    /// <returns>Whether the window is maximized after the operation.</returns>
    private bool ToggleMaximized()
    {
        if (_window is null || _presenter is null)
            return false;

        if (IsMaximized)
        {
            Restore();
            return false;
        }

        _normalX = _window.X;
        _normalY = _window.Y;
        _normalWidth = _window.Width;
        _normalHeight = _window.Height;
        _window.MaximumWidth = double.PositiveInfinity;
        _window.MaximumHeight = double.PositiveInfinity;
        _presenter.IsResizable = true;
        _presenter.IsMaximizable = true;
        _presenter.Maximize();
        IsMaximized = true;
        return true;
    }

    /// <summary>
    /// Restores the captured compact placement and fixed-size policy on the UI thread.
    /// </summary>
    private void Restore()
    {
        if (!IsMaximized || _window is null || _presenter is null)
            return;

        _presenter.Restore();
        _window.X = _normalX;
        _window.Y = _normalY;
        _window.Width = _normalWidth;
        _window.Height = _normalHeight;
        _window.MinimumWidth = TrackerApplicationConstants.WindowWidth;
        _window.MinimumHeight = TrackerApplicationConstants.WindowHeight;
        _window.MaximumWidth = TrackerApplicationConstants.WindowWidth;
        _window.MaximumHeight = TrackerApplicationConstants.WindowHeight;
        _presenter.IsMaximizable = false;
        _presenter.IsResizable = false;
        IsMaximized = false;
    }
}
