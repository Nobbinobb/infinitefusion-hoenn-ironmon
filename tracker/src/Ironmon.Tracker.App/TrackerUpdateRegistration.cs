using Ironmon.Updater.Infrastructure;

namespace Ironmon.Tracker.App;

/// <summary>
/// Configures the fixed release and private Git services shared with the independent updater.
/// </summary>
internal static class TrackerUpdateRegistration
{
    private const string CacheDirectory = "Ironmon/Updater";
    private const string DownloadsDirectory = "downloads";
    private const string DiscoveryDirectory = "discovery";
    private const string GitDirectory = "git-cache";
    private const string GitHome = "git-home";
    private const string GameStaging = "game-staging";
    private const string GameRepository = "https://github.com/infinitefusion/infinitefusion-hoenn-public.git";
    private const string GameBranch = "releases";

    /// <summary>
    /// Registers lazy services without performing network calls on the native UI thread.
    /// </summary>
    /// <param name="services">The tracker service collection.</param>
    internal static void AddUpdater(IServiceCollection services)
    {
        services.AddSingleton<TrackerUpdateNavigation>();
        services.AddSingleton<ITrackerUpdateHost, TrackerUpdateHost>();
        services.AddSingleton<ReleaseHttpClient>();
        services.AddSingleton<GitHubArtifactSource>();
        services.AddSingleton(_ => UpdaterTrust.CreateVerifier());
        services.AddSingleton<ITrackerRuntimeCompatibility, TrackerRuntimeCompatibility>();
        services.AddSingleton(services => new ReleaseDownloadStore(CachePath(DownloadsDirectory), services.GetRequiredService<ReleaseHttpClient>()));
        services.AddSingleton(services => new ReleaseDiscovery(services.GetRequiredService<ReleaseHttpClient>(), services.GetRequiredService<ReleaseVerifier>(), CachePath(DiscoveryDirectory)));
        services.AddSingleton(services => new MinGitCache(CachePath(GitDirectory), services.GetRequiredService<GitHubArtifactSource>()));
        services.AddSingleton(_ => new RepositoryPolicy(new Uri(GameRepository), GameBranch));
        services.AddSingleton(services => new CombinedGamePreparation(services.GetRequiredService<MinGitCache>(), services.GetRequiredService<RepositoryPolicy>(), CachePath(GameStaging)));
        services.AddSingleton(services => new CombinedGitVerification(services.GetRequiredService<MinGitCache>(), services.GetRequiredService<RepositoryPolicy>(), CachePath(GitHome)));
        services.AddSingleton<ProtectedUpdateClient>();
        services.AddSingleton<TrackerUpdatePreparation>();
        services.AddSingleton<TrackerUpdateSession>();
    }

    /// <summary>
    /// Keeps private updater work outside installed game and tracker directories.
    /// </summary>
    /// <param name="child">The fixed service directory name.</param>
    /// <returns>The current user's private cache path.</returns>
    private static string CachePath(string child)
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), CacheDirectory, child);
}
