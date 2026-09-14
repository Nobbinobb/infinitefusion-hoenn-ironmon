using Ironmon.Setup.Core;
using Ironmon.SpriteLibrary;
using Ironmon.Tracker.Connection.Sprites;
using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;
using System.IO;
using System.Net.Http;
using System.Windows;

namespace Ironmon.Setup;

/// <summary>
/// Starts disposable native Setup without MAUI, WebView2 or installed tracker dependencies.
/// </summary>
internal static class Program
{
    private const string CacheDirectory = "Ironmon/Updater";
    private const string Downloads = "downloads";
    private const string Discovery = "discovery";
    private const string GitCache = "git-cache";
    private const string GitHome = "git-home";
    private const string GameStaging = "game-staging";
    private const string GameRepository = "https://github.com/infinitefusion/infinitefusion-hoenn-public.git";
    private const string GameBranch = "releases";

    /// <summary>
    /// Composes the same trusted release, transaction and sprite services used by the installed tracker.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        var application = new Application();
        try
        {
            var platform = new WindowsSetupPlatform();
            platform.EnsureSupported();
            var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), CacheDirectory);
            using var http = new ReleaseHttpClient();
            using var source = new GitHubArtifactSource();
            using var sprites = new CustomSpriteSheetInstaller();
            using var estimateHandler = new HttpClientHandler { AllowAutoRedirect = false };
            using var estimates = new HttpClient(estimateHandler) { Timeout = TimeSpan.FromSeconds(5) };
            var verifier = UpdaterTrust.CreateVerifier();
            var downloads = new ReleaseDownloadStore(Path.Combine(cache, Downloads), http);
            var gitCache = new MinGitCache(Path.Combine(cache, GitCache), source);
            var policy = new RepositoryPolicy(new Uri(GameRepository), GameBranch);
            var runtime = new TrackerRuntimeCompatibility();
            var game = new CombinedGamePreparation(gitCache, policy, Path.Combine(cache, GameStaging));
            var verification = new CombinedGitVerification(gitCache, policy, Path.Combine(cache, GitHome));
            var protectedUpdates = new ProtectedUpdateClient(verifier, downloads);
            var preparation = new SetupPreparation(verifier, downloads, runtime, game, verification, protectedUpdates);
            var authority = new SignedIronmonAuthority(verifier, runtime, verification);
            using var session = new SetupSession(new ReleaseDiscovery(http, verifier, Path.Combine(cache, Discovery)), preparation, (root, progress) => new UpdateTransaction(authority, token => UpdateProcessIdentity.EnsureInstallationIdleAsync(root, UpdaterHandoff.TrackerRelativePath, token), progress), sprites, platform, downloads, protectedUpdates, (commit, token) => SpriteDownloadEstimates.ReadAsync(estimates, commit, token));
            application.Run(new SetupWindow(session));
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, UpdaterText.SetupProgramIronmonSetup, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
