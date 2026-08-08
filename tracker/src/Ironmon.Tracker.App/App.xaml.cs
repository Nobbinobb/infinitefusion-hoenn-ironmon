namespace Ironmon.Tracker.App;

/// <summary>
/// Owns the Ironmon Tracker application lifetime and main window.
/// </summary>
public partial class App : Application
{
    private const double _defaultWindowHeight = 860;
    private const double _defaultWindowWidth = 500;
    private const string _windowHeightPreferenceKey = "tracker_window_height";
    private const string _windowWidthPreferenceKey = "tracker_window_width";
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
        double width = GetWindowDimension(_windowWidthPreferenceKey, _defaultWindowWidth, 360);
        double height = GetWindowDimension(_windowHeightPreferenceKey, _defaultWindowHeight, 520);
        Window window = new(new MainPage())
        {
            Title = "Ironmon Tracker",
            Width = width,
            Height = height,
            MinimumWidth = 360,
            MinimumHeight = 520
        };
        window.Destroying += HandleWindowDestroying;
        return window;
    }

    /// <summary>
    /// Gets a valid persisted window dimension or its first-run default.
    /// </summary>
    /// <param name="preferenceKey">The persisted dimension key.</param>
    /// <param name="defaultValue">The first-run dimension.</param>
    /// <param name="minimumValue">The minimum supported dimension.</param>
    /// <returns>The validated window dimension.</returns>
    private static double GetWindowDimension(string preferenceKey, double defaultValue, double minimumValue)
    {
        double value = Preferences.Default.Get(preferenceKey, defaultValue);
        return double.IsFinite(value) && value >= minimumValue ? value : defaultValue;
    }

    /// <summary>
    /// Stops the local listener when the native tracker window closes.
    /// </summary>
    /// <param name="sender">The window raising the event.</param>
    /// <param name="args">The window destruction event arguments.</param>
    private async void HandleWindowDestroying(object? sender, EventArgs args)
    {
        if (sender is Window window)
            SaveWindowSize(window);

        await _connectionService.StopAsync();
    }

    /// <summary>
    /// Saves a valid tracker window size for the next application launch.
    /// </summary>
    /// <param name="window">The tracker window being closed.</param>
    private static void SaveWindowSize(Window window)
    {
        if (double.IsFinite(window.Width) && window.Width >= window.MinimumWidth)
            Preferences.Default.Set(_windowWidthPreferenceKey, window.Width);

        if (double.IsFinite(window.Height) && window.Height >= window.MinimumHeight)
            Preferences.Default.Set(_windowHeightPreferenceKey, window.Height);
    }
}
