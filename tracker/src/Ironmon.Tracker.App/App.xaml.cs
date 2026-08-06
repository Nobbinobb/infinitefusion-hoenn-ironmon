namespace Ironmon.Tracker.App;

/// <summary>
/// Owns the Ironmon Tracker application lifetime and main window.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Initializes the tracker application.
    /// </summary>
    public App()
    {
        InitializeComponent();
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
        return window;
    }
}
