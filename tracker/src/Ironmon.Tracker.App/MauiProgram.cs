using Ironmon.Tracker.Connection;
using Ironmon.Tracker.Protocol;

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
        builder.Services.AddSingleton(CreateConnectionOptions());
        builder.Services.AddSingleton(CreateKnowledgeOptions());
        builder.Services.AddSingleton<TrackerConnectionState>();
        builder.Services.AddSingleton<TrackerRunState>();
        builder.Services.AddSingleton<TrackerKnowledgeStore>();
        builder.Services.AddSingleton<TrackerConnectionService>();

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

    /// <summary>
    /// Creates the production loopback listener and tracker handshake options.
    /// </summary>
    /// <returns>The production tracker connection options.</returns>
    private static TrackerConnectionOptions CreateConnectionOptions()
    {
        string version = typeof(MauiProgram).Assembly.GetName().Version?.ToString() ?? "0.1.0";
        string[] arguments = Environment.GetCommandLineArgs();
        bool debugRequested = arguments.Any(argument => argument.Equals("--debug", StringComparison.OrdinalIgnoreCase));
        return new TrackerConnectionOptions(TrackerProtocol.Port, version, debugRequested, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Creates the tracker-owned persistence location outside the release directory.
    /// </summary>
    /// <returns>The tracker knowledge persistence options.</returns>
    private static TrackerKnowledgeOptions CreateKnowledgeOptions()
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new TrackerKnowledgeOptions(Path.Combine(localData, "IronmonTracker"));
    }
}
