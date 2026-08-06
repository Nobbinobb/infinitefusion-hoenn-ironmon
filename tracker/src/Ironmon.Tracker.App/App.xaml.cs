using Ironmon.Tracker.Connection;

namespace Ironmon.Tracker.App;

/// <summary>
/// Owns the Ironmon Tracker application lifetime and main window.
/// </summary>
public partial class App : Application
{
    private readonly TrackerConnectionService _connectionService;

    /// <summary>
    /// Initializes the tracker application.
    /// </summary>
    /// <param name="connectionService">The local game connection service.</param>
    public App(TrackerConnectionService connectionService)
    {
        ArgumentNullException.ThrowIfNull(connectionService);
        _connectionService = connectionService;
        InitializeComponent();
        _connectionService.Start();
    }

    /// <summary>
    /// Creates the compact resizable tracker window.
    /// </summary>
    /// <param name="activationState">The platform activation state when available.</param>
    /// <returns>The configured tracker window.</returns>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        Window window = new(new MainPage())
        {
            Title = "Ironmon Tracker",
            Width = 440,
            Height = 680,
            MinimumWidth = 360,
            MinimumHeight = 520
        };
        window.Destroying += HandleWindowDestroying;
        return window;
    }

    /// <summary>
    /// Stops the local listener when the native tracker window closes.
    /// </summary>
    /// <param name="sender">The window raising the event.</param>
    /// <param name="args">The window destruction event arguments.</param>
    private async void HandleWindowDestroying(object? sender, EventArgs args) => await _connectionService.StopAsync();
}
