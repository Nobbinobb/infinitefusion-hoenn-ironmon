namespace Ironmon.Tracker.AccessGenerator.App.WinUI;

/// <summary>
/// Connects the Windows application lifetime to the shared MAUI application.
/// </summary>
public partial class App : MauiWinUIApplication
{
    /// <summary>
    /// Initializes the Windows application object.
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Creates the shared Ironmon access-token generator MAUI application.
    /// </summary>
    /// <returns>The configured MAUI application.</returns>
    protected override MauiApp CreateMauiApp()
        => MauiProgram.CreateMauiApp();
}
