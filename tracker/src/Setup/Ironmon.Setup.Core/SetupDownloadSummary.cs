using Ironmon.Updater.Infrastructure;

namespace Ironmon.Setup.Core;

/// <summary>
/// Totals only selected download sizes while keeping estimates and missing sizes explicit.
/// </summary>
/// <remarks>Constructs presentation data; cached content can reduce the eventual network transfer.</remarks>
/// <param name="KnownBytes">The sum of published or observed bytes, including any game estimate.</param>
/// <param name="Estimated">Whether the sum includes a negotiated game-transfer estimate.</param>
/// <param name="Incomplete">Whether selected downloads have sizes that cannot be included.</param>
public sealed record SetupDownloadSummary(long KnownBytes, bool Estimated, bool Incomplete)
{
    /// <summary>
    /// Calculates the selected downloads without counting sprites or prerequisites that were not selected.
    /// </summary>
    /// <param name="review">The authenticated release review.</param>
    /// <param name="webViewSelected">Whether the required runtime will be installed.</param>
    /// <param name="webViewBytes">The observed complete runtime package size.</param>
    /// <param name="spritesSelected">Whether changing sprite downloads were selected.</param>
    /// <param name="spriteBytes">The complete daily sprite estimate, or null when unavailable.</param>
    /// <returns>The known total and its explicit limitations.</returns>
    public static SetupDownloadSummary Create(SetupReview review, bool webViewSelected, long? webViewBytes, bool spritesSelected, long? spriteBytes = null)
    {
        var gameBytes = review.IncludesGame ? review.GameDownloadBytes : 0;
        var bytes = checked(review.PackageBytes + review.SupportDownloadBytes + (gameBytes ?? 0) + (webViewSelected ? webViewBytes ?? 0 : 0) + (spritesSelected ? spriteBytes ?? 0 : 0));
        return new SetupDownloadSummary(bytes, gameBytes > 0 || spritesSelected && spriteBytes > 0, spritesSelected && spriteBytes is null || gameBytes is null || webViewSelected && webViewBytes is null);
    }
}
