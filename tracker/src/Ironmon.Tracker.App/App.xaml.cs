namespace Ironmon.Tracker.App;

/// <summary>
/// Owns the Ironmon Tracker application lifetime and main window.
/// </summary>
public partial class App : Application
{
    private readonly TrackerConnectionService _connectionService;
    private readonly TrackerGlobalShortcutService _shortcutService;
    private readonly IStringLocalizer<TrackerResources> _text;
    private readonly TrackerWindowService _windowService;

    /// <summary>
    /// Initializes the tracker application.
    /// </summary>
    /// <param name="connectionService">The local game connection service.</param>
    /// <param name="shortcutService">The foreground-safe global shortcut service.</param>
    /// <param name="text">The localized tracker text.</param>
    /// <param name="windowService">The native tracker-window coordinator.</param>
    public App(TrackerConnectionService connectionService, TrackerGlobalShortcutService shortcutService, IStringLocalizer<TrackerResources> text, TrackerWindowService windowService)
    {
        ArgumentNullException.ThrowIfNull(connectionService);
        ArgumentNullException.ThrowIfNull(shortcutService);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(windowService);
        _connectionService = connectionService;
        _shortcutService = shortcutService;
        _text = text;
        _windowService = windowService;
        InitializeComponent();
        _connectionService.Start();
        _shortcutService.Start();
    }

    /// <summary>
    /// Creates the compact fixed-size tracker window.
    /// </summary>
    /// <param name="activationState">The platform activation state when available.</param>
    /// <returns>The configured tracker window.</returns>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        Page page = WebView2Runtime.IsAvailable() ? new MainPage() : new WebView2MissingPage(_text);
        Window window = new(page)
        {
            Title = _text["App.Native.AppTitle"],
            Width = TrackerApplicationConstants.WindowWidth,
            Height = TrackerApplicationConstants.WindowHeight,
            MinimumWidth = TrackerApplicationConstants.WindowWidth,
            MinimumHeight = TrackerApplicationConstants.WindowHeight,
            MaximumWidth = TrackerApplicationConstants.WindowWidth,
            MaximumHeight = TrackerApplicationConstants.WindowHeight
        };
        window.HandlerChanged += HandleWindowHandlerChanged;
        window.Destroying += HandleWindowDestroying;
        return window;
    }

    /// <summary>
    /// Connects native window control once the Windows window is available.
    /// </summary>
    /// <param name="sender">The tracker window whose native handler changed.</param>
    /// <param name="args">The handler-change event arguments.</param>
    private void HandleWindowHandlerChanged(object? sender, EventArgs args)
    {
        if (sender is not Window window || window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window platformWindow)
            return;

        IntPtr windowHandle = Microsoft.Maui.Platform.WindowExtensions.GetWindowHandle(platformWindow);
        Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
        Microsoft.UI.Windowing.AppWindow appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            _windowService.Attach(window, presenter);

        window.HandlerChanged -= HandleWindowHandlerChanged;
    }

    /// <summary>
    /// Stops the local listener when the native tracker window closes.
    /// </summary>
    /// <param name="sender">The window raising the event.</param>
    /// <param name="args">The window destruction event arguments.</param>
    private async void HandleWindowDestroying(object? sender, EventArgs args)
    {
        await _shortcutService.StopAsync();
        await _connectionService.StopAsync();
    }
}
