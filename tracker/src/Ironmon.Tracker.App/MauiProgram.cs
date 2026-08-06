namespace Ironmon.Tracker.App;

/// <summary>
/// Configures and creates the Ironmon Tracker desktop application.
/// </summary>
public static class MauiProgram
{
    /// <summary>
    /// Creates the configured MAUI application.
    /// </summary>
    /// <returns>The configured desktop application.</returns>
    public static MauiApp CreateMauiApp()
    {
        MauiAppBuilder builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().ConfigureFonts(ConfigureFonts);
        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        return builder.Build();
    }

    /// <summary>
    /// Registers fonts used by the tracker interface.
    /// </summary>
    /// <param name="fonts">The MAUI font collection.</param>
    private static void ConfigureFonts(IFontCollection fonts) => fonts.AddFont("OpenSans-Regular.ttf", "TrackerSans");
}
