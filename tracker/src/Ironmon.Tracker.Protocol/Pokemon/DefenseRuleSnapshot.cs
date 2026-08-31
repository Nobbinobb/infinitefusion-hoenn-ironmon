namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Describes a supporting defense rule and known source; the compact view renders derived effects instead.
/// </summary>
public sealed class DefenseRuleSnapshot
{
    /// <summary>
    /// Initializes an empty defense payload for serialization.
    /// </summary>
    public DefenseRuleSnapshot()
    {
    }

    /// <summary>
    /// The stable rule identifier.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets the source known to the tracker, retained for supporting rule data rather than displayed in the defense view.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// The effect and its move-specific conditions.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets a supporting rule summary; compact UI labels come from DefenseEffectSnapshot instead.
    /// </summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Whether state prerequisites are met; null means conditional or unknown.
    /// </summary>
    public bool? Active { get; init; }

    /// <summary>
    /// The attack types affected by this rule.
    /// </summary>
    public IReadOnlyList<string> AttackTypes { get; init; } = [];

    /// <summary>
    /// The public catalog of affected move names, never an enemy moveset.
    /// </summary>
    public IReadOnlyList<string> Moves { get; init; } = [];
}
