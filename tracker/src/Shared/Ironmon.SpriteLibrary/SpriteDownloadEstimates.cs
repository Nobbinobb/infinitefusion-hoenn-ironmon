using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Ironmon.SpriteLibrary;

/// <summary>
/// Reads optional dated full-library estimates; this data never authorizes files or supplies download addresses.
/// </summary>
public static partial class SpriteDownloadEstimates
{
    /// <summary>
    /// Bounds the optional public feed independently of any declared response length.
    /// </summary>
    public const int MaximumDocumentBytes = 65536;
    private const string FeedUrl = "https://raw.githubusercontent.com/Nobbinobb/infinitefusion-hoenn-ironmon/sprite-size-estimates/estimates.json";
    private const string CommitRegex = "\\A[0-9a-f]{40}\\z";
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    /// <summary>
    /// Serializes one complete daily scan after validating its bounds.
    /// </summary>
    /// <param name="document">The finished measurements.</param>
    /// <returns>The compact public feed bytes.</returns>
    public static byte[] Serialize(SpriteDownloadEstimateDocument document)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, _json);
        _ = Parse(bytes, string.Empty, DateTimeOffset.UtcNow);
        return bytes;
    }

    /// <summary>
    /// Checks the feed's limits, unique game identities, completeness and freshness before selecting one estimate.
    /// </summary>
    /// <param name="bytes">The bounded JSON document.</param>
    /// <param name="gameCommit">The independently selected supported game commit.</param>
    /// <param name="now">The time used to reject future or expired observations.</param>
    /// <returns>The matching estimate, or null when absent or older than seven days.</returns>
    public static SpriteDownloadEstimate? Parse(byte[] bytes, string gameCommit, DateTimeOffset now)
    {
        if (bytes.Length is 0 or > MaximumDocumentBytes)
            throw new InvalidDataException("The sprite estimate document is oversized or empty.");

        var document = JsonSerializer.Deserialize<SpriteDownloadEstimateDocument>(bytes, _json) ?? throw new InvalidDataException("The sprite estimate document is empty.");
        if (document.SpriteManifestCommit is { } metadataCommit && !CommitPattern().IsMatch(metadataCommit))
            throw new InvalidDataException("The sprite manifest revision is invalid.");

        if (document.SchemaVersion != 1 || document.Games is null || document.Games.Length is < 1 or > 16
            || document.Games.Any(game => game is null || game.GameCommit is null || !CommitPattern().IsMatch(game.GameCommit) || game.Bytes is <= 0 or > 107374182400L || game.AvailableSheets is <= 0 or > 50000 || game.UnavailableSheets is < 0 or > 50000 || game.AvailableSheets + game.UnavailableSheets > 50000)
            || document.Games.Select(game => game.GameCommit).Distinct(StringComparer.Ordinal).Count() != document.Games.Length
            || document.MeasuredAt > now.AddMinutes(15))
        {
            throw new InvalidDataException("The sprite estimate document is invalid or incomplete.");
        }

        var selected = document.Games.SingleOrDefault(game => game.GameCommit == gameCommit);
        return selected is null || document.MeasuredAt < now.AddDays(-7) ? null : new SpriteDownloadEstimate(selected.Bytes, document.MeasuredAt, selected.AvailableSheets, selected.UnavailableSheets);
    }

    /// <summary>
    /// Fetches one small public estimate without requesting any sprite image or changing installer availability.
    /// </summary>
    /// <param name="client">A bounded client with redirects disabled.</param>
    /// <param name="gameCommit">The independently selected supported game commit.</param>
    /// <param name="cancellationToken">The review cancellation token.</param>
    /// <returns>The current matching estimate, or null on optional-feed failure.</returns>
    public static async Task<SpriteDownloadEstimate?> ReadAsync(HttpClient client, string gameCommit, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await client.GetAsync(FeedUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > MaximumDocumentBytes)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[4096];
            int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (output.Length + count > MaximumDocumentBytes)
                    return null;

                output.Write(buffer, 0, count);
            }

            return Parse(output.ToArray(), gameCommit, DateTimeOffset.UtcNow);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException || error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// Matches only complete immutable game commit identifiers.
    /// </summary>
    /// <returns>The strict commit pattern.</returns>
    [GeneratedRegex(CommitRegex, RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();
}

/// <summary>
/// Represents the dated estimate displayed to a player.
/// </summary>
/// <remarks>Constructs informational full-library download data.</remarks>
/// <param name="Bytes">The measured available sheet bytes.</param>
/// <param name="MeasuredAt">The start of the complete scan in UTC.</param>
/// <param name="AvailableSheets">The measured downloadable sheet count.</param>
/// <param name="UnavailableSheets">The count whose server response was 404 or 410.</param>
public sealed record SpriteDownloadEstimate(long Bytes, DateTimeOffset MeasuredAt, int AvailableSheets, int UnavailableSheets);

/// <summary>
/// Contains one complete scan of all game versions supported by the current published release.
/// </summary>
/// <remarks>Constructs the versioned public estimate document.</remarks>
/// <param name="SchemaVersion">The fixed schema version, currently one.</param>
/// <param name="MeasuredAt">The UTC time the scan began.</param>
/// <param name="Games">The complete per-game measurements.</param>
/// <param name="SpriteManifestCommit">The optional exact revision of the current upstream manifest snapshot.</param>
public sealed record SpriteDownloadEstimateDocument(int SchemaVersion, DateTimeOffset MeasuredAt, SpriteGameDownloadEstimate[] Games, string? SpriteManifestCommit = null);

/// <summary>
/// Records all available sprite-sheet bytes selected by one exact game snapshot.
/// </summary>
/// <remarks>Constructs a complete game-specific measurement.</remarks>
/// <param name="GameCommit">The exact game commit used to select the sheet manifests.</param>
/// <param name="Bytes">The total measured available sheet bytes.</param>
/// <param name="AvailableSheets">The count measured successfully.</param>
/// <param name="UnavailableSheets">The count definitively unavailable at the host.</param>
public sealed record SpriteGameDownloadEstimate(string GameCommit, long Bytes, int AvailableSheets, int UnavailableSheets);
