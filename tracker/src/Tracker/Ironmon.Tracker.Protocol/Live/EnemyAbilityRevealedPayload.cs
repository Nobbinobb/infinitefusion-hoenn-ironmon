namespace Ironmon.Tracker.Protocol.Live;

/// <summary>
/// Describes an opposing ability that became visible during battle.
/// </summary>
public sealed class EnemyAbilityRevealedPayload
{
    /// <summary>
    /// Initializes an empty ability-reveal payload for protocol serialization.
    /// </summary>
    public EnemyAbilityRevealedPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the battle-stable enemy identifier.
    /// </summary>
    public required string EnemyId { get; init; }

    /// <summary>
    /// Gets or initializes the stable species and form identifier.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the revealed ability.
    /// </summary>
    public required AbilitySnapshot Ability { get; init; }
}
