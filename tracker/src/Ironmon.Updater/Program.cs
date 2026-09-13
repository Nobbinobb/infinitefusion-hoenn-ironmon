using System.Windows;
using Ironmon.Updater.Infrastructure;
using Ironmon.Tracker.Connection.Sprites;

namespace Ironmon.Updater;

/// <summary>
/// Runs the independent recovery window without loading the tracker, MAUI or Blazor.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Reports optional sprite counts inside the authenticated administrator operation.
    /// </summary>
    private sealed class SpriteProgressObserver : IProgress<Ironmon.SpriteLibrary.CustomSpriteInstallProgress>
    {
        /// <summary>
        /// Forwards measured sheets and bytes without exposing remote text.
        /// </summary>
        /// <param name="value">The shared sprite measurement.</param>
        public void Report(Ironmon.SpriteLibrary.CustomSpriteInstallProgress value)
            => InstallationProgressScope.Report(new(InstallationStage.DownloadingSprites, value.CompletedSheetCount, value.TotalSheetCount, value.DownloadedBytes));
    }

    private const string CacheDirectory = "Ironmon/Updater";
    private const string GitDirectory = "git-cache";
    private const string GitHome = "git-home";
    private const string GameRepository = "https://github.com/infinitefusion/infinitefusion-hoenn-public.git";
    private const string GameBranch = "releases";

    /// <summary>
    /// Starts the independent helper with embedded release trust and offline inventory verification.
    /// </summary>
    /// <param name="args">The bounded handoff or recovery entry point.</param>
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 3 && args[0] == ProtectedUpdateServer.Argument && int.TryParse(args[2], out var ownerId))
        {
            try
            {
                using var sprites = new CustomSpriteSheetInstaller();
                ProtectedUpdateServer.RunAsync(args[1], ownerId, async (root, includeUnavailable, token) =>
                {
                    var result = await sprites.InstallAsync(sprites.CreatePlan(root, includeUnavailable), new SpriteProgressObserver(), token).ConfigureAwait(false);
                    return new ProtectedSpriteResult(result.DownloadedSheetCount, result.UnchangedSheetCount, result.FailedSheetCount, result.DownloadedBytes, result.UnavailableSheetCount);
                }).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                Environment.ExitCode = 1;
            }

            return;
        }

        using var source = new GitHubArtifactSource();
        var cacheRoot = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), CacheDirectory);
        var cache = new MinGitCache(System.IO.Path.Combine(cacheRoot, GitDirectory), source);
        var git = new CombinedGitVerification(cache, new RepositoryPolicy(new Uri(GameRepository), GameBranch), System.IO.Path.Combine(cacheRoot, GitHome));
        var application = new Application();
        application.Run(new UpdaterWindow(args, new SignedIronmonAuthority(UpdaterTrust.CreateVerifier(), new TrackerRuntimeCompatibility(), git)));
    }

}
