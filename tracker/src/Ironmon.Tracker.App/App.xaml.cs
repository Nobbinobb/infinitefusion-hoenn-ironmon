namespace Ironmon.Tracker.App;

/// <summary>
/// Owns the Ironmon Tracker application lifetime and main window.
/// </summary>
public partial class App : Application
{
    private readonly TrackerConnectionService _connectionService;
    private readonly TrackerGlobalShortcutService _shortcutService;
    private readonly IStringLocalizer<TrackerResources> _text;

    /// <summary>
    /// Initializes the tracker application.
    /// </summary>
    /// <param name="connectionService">The local game connection service.</param>
    /// <param name="shortcutService">The foreground-safe global shortcut service.</param>
    /// <param name="text">The localized tracker text.</param>
    public App(TrackerConnectionService connectionService, TrackerGlobalShortcutService shortcutService, IStringLocalizer<TrackerResources> text)
    {
        ArgumentNullException.ThrowIfNull(connectionService);
        ArgumentNullException.ThrowIfNull(shortcutService);
        ArgumentNullException.ThrowIfNull(text);
        _connectionService = connectionService;
        _shortcutService = shortcutService;
        _text = text;
        InitializeComponent();
        _connectionService.Start();
        _shortcutService.Start();
    }

    /// <summary>
    /// Creates the compact resizable tracker window.
    /// </summary>
    /// <param name="activationState">The platform activation state when available.</param>
    /// <returns>The configured tracker window.</returns>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        double width = GetWindowDimension(TrackerApplicationConstants.WindowWidthPreferenceKey, TrackerApplicationConstants.DefaultWindowWidth, TrackerApplicationConstants.MinimumWindowWidth);
        double height = GetWindowDimension(TrackerApplicationConstants.WindowHeightPreferenceKey, TrackerApplicationConstants.DefaultWindowHeight, TrackerApplicationConstants.MinimumWindowHeight);
        Page page = WebView2Runtime.IsAvailable() ? new MainPage() : new WebView2MissingPage(_text);
        Window window = new(page)
        {
            Title = _text["App.Native.AppTitle"],
            Width = width,
            Height = height,
            MinimumWidth = TrackerApplicationConstants.MinimumWindowWidth,
            MinimumHeight = TrackerApplicationConstants.MinimumWindowHeight
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

        await _shortcutService.StopAsync();
        await _connectionService.StopAsync();
    }

    /// <summary>
    /// Saves a valid tracker window size for the next application launch.
    /// </summary>
    /// <param name="window">The tracker window being closed.</param>
    private static void SaveWindowSize(Window window)
    {
        if (double.IsFinite(window.Width) && window.Width >= window.MinimumWidth)
            Preferences.Default.Set(TrackerApplicationConstants.WindowWidthPreferenceKey, window.Width);

        if (double.IsFinite(window.Height) && window.Height >= window.MinimumHeight)
            Preferences.Default.Set(TrackerApplicationConstants.WindowHeightPreferenceKey, window.Height);
    }
}
