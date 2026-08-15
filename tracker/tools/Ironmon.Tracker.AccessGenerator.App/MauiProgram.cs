namespace Ironmon.Tracker.AccessGenerator.App;

/// <summary>
/// Configures and creates the Ironmon access-token generator application.
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
        builder.Services.AddLocalization(options => options.ResourcesPath = GeneratorLocalizationConstants.ResourcesPath);
        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddSingleton<DiagnosticAccessTokenGenerator>();
        builder.Services.AddSingleton<GeneratorFileService>();

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
        IStringLocalizer<GeneratorResources> text = services.GetRequiredService<IStringLocalizer<GeneratorResources>>();
        if (text[GeneratorLocalizationConstants.StartupProbeKey].ResourceNotFound)
            throw new InvalidOperationException(GeneratorLocalizationConstants.MissingResourceMessage);
    }

    /// <summary>
    /// Registers the font used by the generator interface.
    /// </summary>
    /// <param name="fonts">The MAUI font collection.</param>
    private static void ConfigureFonts(IFontCollection fonts)
        => fonts.AddFont(GeneratorApplicationConstants.FontFile, GeneratorApplicationConstants.FontAlias);
}
