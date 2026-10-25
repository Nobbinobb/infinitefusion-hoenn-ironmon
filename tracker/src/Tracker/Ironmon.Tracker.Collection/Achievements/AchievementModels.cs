using System.Text.Json;

namespace Ironmon.Tracker.Collection.Achievements;

/// <summary>
/// Describes one earned achievement without exposing locked definitions or progress.
/// </summary>
/// <remarks>
/// Initializes the public record from a committed unlock and its revealed definition.
/// </remarks>
/// <param name="Id">The stable opaque achievement identity.</param>
/// <param name="Number">The current catalogue review number.</param>
/// <param name="Name">The approved player-facing title.</param>
/// <param name="Condition">The condition revealed by earning the achievement.</param>
/// <param name="UnlockedAt">The UTC time at which the tracker committed the unlock.</param>
public sealed record UnlockedAchievement(Guid Id, int Number, string Name, string Condition, DateTimeOffset UnlockedAt);

/// <summary>
/// Defines a prerequisite group without disclosing the protected condition.
/// </summary>
/// <remarks>
/// Initializes an all-or-any group of opaque prerequisite identities.
/// </remarks>
/// <param name="Any">Whether one parent suffices; otherwise every parent must be earned.</param>
/// <param name="Parents">Stable prerequisite identities.</param>
internal sealed record AchievementGate(bool Any, Guid[] Parents);

/// <summary>
/// Carries one independently encrypted definition and its opaque activation gates.
/// </summary>
/// <remarks>
/// Initializes the sealed definition without decrypting its contents.
/// </remarks>
/// <param name="Id">Stable opaque identity also authenticated by the cipher.</param>
/// <param name="Gates">Groups combined with AND; an empty list denotes a starting rule.</param>
/// <param name="Nonce">The independent twelve-byte AES-GCM nonce.</param>
/// <param name="Tag">The sixteen-byte authentication tag.</param>
/// <param name="Ciphertext">The protected rule and presentation data.</param>
internal sealed record SealedAchievement(Guid Id, AchievementGate[] Gates, byte[] Nonce, byte[] Tag, byte[] Ciphertext);

/// <summary>
/// Describes an earned condition using general directly retained evidence.
/// </summary>
/// <remarks>
/// Initializes an internally active rule; dormant rules remain sealed.
/// </remarks>
/// <param name="Number">The current review number.</param>
/// <param name="Name">The approved player-facing title.</param>
/// <param name="Condition">The approved player-facing condition.</param>
/// <param name="Group">Opaque generic summary group identity.</param>
/// <param name="Operation">The exact sum, maximum, membership or fraction operation.</param>
/// <param name="Measure">The generic measure key within the group.</param>
/// <param name="Filter">The explicit typed joint-evidence predicate.</param>
/// <param name="Threshold">The exact canonical comparison threshold.</param>
/// <param name="Comparison">The approved at-least or at-most comparison.</param>
/// <param name="AllowedMembers">Optional identities admitted to a cardinality count; an absent or null list admits every retained member.</param>
/// <param name="Available">Whether this definition can still earn new unlocks; retirement preserves previously earned records.</param>
internal sealed record AchievementRule(int Number, string Name, string Condition, string Group, string Operation, string Measure, JsonElement Filter, JsonElement Threshold, string Comparison, JsonElement AllowedMembers = default, bool Available = true);

/// <summary>
/// Retains the encrypted unlock ledger and durable notification queue.
/// </summary>
internal sealed class AchievementCheckpoint
{
    /// <summary>
    /// Gets or initializes exact earned identities and their original commit times.
    /// </summary>
    public Dictionary<Guid, DateTimeOffset> Earned { get; init; } = [];

    /// <summary>
    /// Gets or initializes earned identities awaiting a displayed-notification acknowledgement.
    /// </summary>
    public HashSet<Guid> Pending { get; init; } = [];
}
