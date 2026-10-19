using Ironmon.Tracker.Collection.Achievements;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Supplies only earned artwork and the explicit shared notification preview.
/// </summary>
public sealed partial class GameplayCollectionStore
{
    /// <summary>
    /// Gets shared test artwork without unlocking an achievement or reading locked metadata.
    /// </summary>
    public static AchievementIcon PreviewAchievementIcon => AchievementIconPack.Shared.Preview;

    /// <summary>
    /// Reads artwork only for requested identities present in the authenticated earned ledger.
    /// </summary>
    /// <param name="ids">Stable requested achievement identities.</param>
    /// <param name="cancellationToken">Cancels the earned-ledger query.</param>
    /// <returns>Only earned images; unknown and locked identities are absent.</returns>
    public async Task<IReadOnlyDictionary<Guid, AchievementIcon>> GetAchievementIconsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        HashSet<Guid> requested = [.. ids.Take(AchievementCount + 1)];
        ArgumentOutOfRangeException.ThrowIfGreaterThan(requested.Count, AchievementCount);
        IReadOnlyList<UnlockedAchievement> earned = await GetAchievementsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return earned.Where(item => requested.Contains(item.Id)).ToDictionary(item => item.Id, item => AchievementIconPack.Shared.ForEarned(item.Id));
    }
}
