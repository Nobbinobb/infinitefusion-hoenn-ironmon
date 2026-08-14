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
        builder.Services.AddLocalization(options => options.ResourcesPath = TrackerLocalizationConstants.ResourcesPath);
        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddSingleton(CreateKnowledgeOptions());
        builder.Services.AddSingleton<FavoritePokemonStore>();
        builder.Services.AddSingleton(static services => CreateConnectionOptions(services.GetRequiredService<FavoritePokemonStore>()));
        builder.Services.AddSingleton<TrackerDiagnosticsStore>();
        builder.Services.AddSingleton<TrackerConnectionState>();
        builder.Services.AddSingleton<TrackerRunState>();
        builder.Services.AddSingleton<TrackerKnowledgeStore>();
        builder.Services.AddSingleton<AreaDiscoveryStore>();
        builder.Services.AddSingleton<CompletedRunArchive>();
        builder.Services.AddSingleton<TrackerConnectionService>();
        builder.Services.AddSingleton<TrackerGlobalShortcutService>();
        builder.Services.AddSingleton(static services => services.GetRequiredService<TrackerConnectionService>().Requests);

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        MauiApp application = builder.Build();
        EnsureLocalizationAvailable(application.Services);
        return application;
    }

    /// <summary>
    /// Verifies that the compiled shared resource can be discovered by the configured localizer.
    /// </summary>
    /// <param name="services">The configured application services.</param>
    private static void EnsureLocalizationAvailable(IServiceProvider services)
    {
        IStringLocalizer<TrackerResources> text = services.GetRequiredService<IStringLocalizer<TrackerResources>>();
        if (text[TrackerLocalizationConstants.StartupProbeKey].ResourceNotFound)
            throw new InvalidOperationException(TrackerLocalizationConstants.MissingResourceMessage);
    }

    /// <summary>
    /// Registers fonts used by the tracker interface.
    /// </summary>
    /// <param name="fonts">The MAUI font collection.</param>
    private static void ConfigureFonts(IFontCollection fonts) => fonts.AddFont(TrackerApplicationConstants.FontFile, TrackerApplicationConstants.FontAlias);

    /// <summary>
    /// Creates the production loopback listener and tracker handshake options.
    /// </summary>
    /// <param name="favorites">The persisted Favorite Clause list.</param>
    /// <returns>The production tracker connection options.</returns>
    private static TrackerConnectionOptions CreateConnectionOptions(FavoritePokemonStore favorites)
    {
        string version = typeof(MauiProgram).Assembly.GetName().Version?.ToString() ?? TrackerApplicationConstants.DefaultVersion;
        string[] arguments = Environment.GetCommandLineArgs();
        bool debugRequested = arguments.Any(argument => argument.Equals(TrackerApplicationConstants.DebugArgument, StringComparison.OrdinalIgnoreCase));
#if DEBUG
        debugRequested = true;
#endif
        TrackerConnectionOptions options = new(TrackerProtocol.Port, version, debugRequested, TimeSpan.FromSeconds(TrackerProtocol.HandshakeTimeoutSeconds))
        {
            AutoSelectStarter = Preferences.Default.Get(TrackerApplicationConstants.AutoSelectStarterPreferenceKey, false),
            FavoriteSpeciesIds = favorites.Favorites.Select(favorite => favorite.SpeciesId).ToArray()
        };

        return options;
    }

    /// <summary>
    /// Creates the tracker-owned persistence location outside the release directory.
    /// </summary>
    /// <returns>The tracker knowledge persistence options.</returns>
    private static TrackerKnowledgeOptions CreateKnowledgeOptions()
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new TrackerKnowledgeOptions(Path.Combine(localData, TrackerStorageNames.RootDirectory));
    }
}
