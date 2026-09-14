using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Ironmon.SpriteLibrary;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.SpriteSizeTool;

/// <summary>
/// Produces a complete daily sprite-size feed from the currently published signed release's pinned game manifests.
/// </summary>
internal static class Program
{
    private const string Latest = "latest";
    private const string Game = "game";
    private const string RawGame = "https://raw.githubusercontent.com/infinitefusion/infinitefusion-hoenn-public/";
    private const string CustomManifest = "/Data/sprites/CUSTOM_SPRITES";
    private const string BaseManifest = "/Data/sprites/BASE_SPRITES";
    private const string CommitPattern = "\\A[0-9a-f]{40}\\z";
    private const string UserAgent = "Ironmon-Sprite-Size-Measurement/1";
    private const string MetadataReference = "https://api.github.com/repos/infinitefusion/pif-downloadables/git/ref/heads/master";
    private const string RawMetadata = "https://raw.githubusercontent.com/infinitefusion/pif-downloadables/";
    private const string ObjectProperty = "object";
    private const string ShaProperty = "sha";
    private const string CurrentCustom = "/CUSTOM_SPRITES";
    private const string CurrentBase = "/BASE_SPRITES";

    /// <summary>
    /// Measures the latest release or one explicit game commit without publishing, downloading images or touching game files.
    /// </summary>
    /// <param name="args">The mode, output file, and discovery-cache directory or explicit game commit.</param>
    /// <returns>Zero only after the complete, validated document is written.</returns>
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is not [Latest or Game, _, _])
                throw new ArgumentException("Usage: latest output.json discovery-cache | game output.json game-commit");

            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(25));
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            string[] commits;
            if (args[0] == Latest)
            {
                using var transport = new ReleaseHttpClient();
                var verifier = UpdaterTrust.CreateVerifier();
                using var discovery = new ReleaseDiscovery(transport, verifier, Path.GetFullPath(args[2]));
                var result = await discovery.CheckAsync(true, cancellation.Token);
                var release = result.Release ?? throw new IOException(result.Error ?? "No signed published release is available.");
                commits = verifier.Verify(release.Manifest, release.Signatures).Game.SupportedCommits;
            }
            else
            {
                commits = [args[2]];
            }

            var measuredAt = DateTimeOffset.UtcNow;
            using var metadata = JsonDocument.Parse(await client.GetByteArrayAsync(MetadataReference, cancellation.Token));
            var metadataCommit = metadata.RootElement.GetProperty(ObjectProperty).GetProperty(ShaProperty).GetString() ?? throw new InvalidDataException("The current sprite manifest revision is missing.");
            if (!Regex.IsMatch(metadataCommit, CommitPattern, RegexOptions.CultureInvariant))
                throw new InvalidDataException("The sprite manifest revision is invalid.");

            var currentCustom = await ReadManifestAsync(client, RawMetadata + metadataCommit + CurrentCustom, cancellation.Token);
            var currentBase = await ReadManifestAsync(client, RawMetadata + metadataCommit + CurrentBase, cancellation.Token);
            List<SpriteGameDownloadEstimate> games = [];
            foreach (var commit in commits.Distinct(StringComparer.Ordinal))
            {
                if (!Regex.IsMatch(commit, CommitPattern, RegexOptions.CultureInvariant))
                    throw new InvalidDataException("A full game commit is required.");

                var custom = await ReadManifestAsync(client, RawGame + commit + CustomManifest, cancellation.Token);
                var normal = await ReadManifestAsync(client, RawGame + commit + BaseManifest, cancellation.Token);
                Console.WriteLine($"Checking the current sprite library for {commit}; manifest revision {metadataCommit}.");
                var result = await SpriteDownloadMeasurement.MeasureAsync(client, commit, custom.Concat(currentCustom), normal.Concat(currentBase), cancellation.Token);
                games.Add(result);
                Console.WriteLine($"Measured {commit}: {result.Bytes} bytes across {result.AvailableSheets} sheets; {result.UnavailableSheets} unavailable.");
            }

            var bytes = SpriteDownloadEstimates.Serialize(new SpriteDownloadEstimateDocument(1, measuredAt, [.. games], metadataCommit));
            await File.WriteAllBytesAsync(args[1], bytes, cancellation.Token);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    /// <summary>
    /// Reads only a bounded manifest pinned to the supported game commit, never a mutable branch tip.
    /// </summary>
    /// <param name="client">The redirect-disabled metadata client.</param>
    /// <param name="url">The fixed-host immutable manifest URL.</param>
    /// <param name="cancellationToken">The bounded scan token.</param>
    /// <returns>The input lines for the same parser used by the player downloader.</returns>
    private static async Task<string[]> ReadManifestAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + count > 67108864)
                throw new IOException("The pinned sprite manifest exceeds its supported size.");

            output.Write(buffer, 0, count);
        }

        return Encoding.UTF8.GetString(output.ToArray()).Split('\n');
    }
}
