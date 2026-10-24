using Ironmon.Tracker.Collection.Achievements;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Supplies artwork only for authenticated earned achievements.
/// </summary>
public sealed partial class GameplayCollectionStore
{
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
        return earned.Where(item => requested.Contains(item.Id)).ToDictionary(item => item.Id, item => AchievementIconPack.Shared.ForEarned(item.Id) with { HasArtwork = AchievementIconPack.Shared.AssignedIconId(item.Id) is Guid iconId && AchievementArtworkPack.Shared.Contains(iconId) });
    }

    /// <summary>
    /// Opens detail artwork only after verifying the requested identity in the authenticated earned ledger.
    /// </summary>
    /// <param name="id">The achievement whose image was selected in the tracker.</param>
    /// <param name="cancellationToken">Cancels the ledger read, queued image work, or superseded delivery.</param>
    /// <returns>The in-memory artwork, or null for locked, unknown, or unfinished artwork.</returns>
    public async Task<AchievementArtwork?> GetAchievementArtworkAsync(Guid id, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<UnlockedAchievement> earned = await GetAchievementsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!earned.Any(item => item.Id == id) || AchievementIconPack.Shared.AssignedIconId(id) is not Guid iconId)
            return null;

        return await AchievementArtworkPack.Shared.ForEarnedAsync(iconId, cancellationToken).ConfigureAwait(false);
    }
}
