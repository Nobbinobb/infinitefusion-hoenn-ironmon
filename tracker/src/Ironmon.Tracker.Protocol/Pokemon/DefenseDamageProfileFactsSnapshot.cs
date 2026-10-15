using System.Text.Json.Serialization;

namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Contains ordered public rule mechanics for deferred numeric defense-profile composition.
/// </summary>
/// <remarks>
/// Rule selection, conditions, labels, and privacy filtering remain game-owned. This payload
/// contains no concealed battle state and requires an explicitly negotiated version.
/// </remarks>
public sealed class DefenseDamageProfileFactsSnapshot
{
    /// <summary>
    /// Defines the supported explicit numeric composition contract version.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Defines the maximum number of ordered rule facts accepted per overview.
    /// </summary>
    public const int MaximumRules = 256;

    /// <summary>
    /// Defines the maximum number of type rows or per-rule type entries accepted.
    /// </summary>
    public const int MaximumTypes = 64;

    /// <summary>
    /// Initializes an empty semantic facts payload for protocol serialization.
    /// </summary>
    public DefenseDamageProfileFactsSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the version of the explicit numeric composition contract.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Gets or initializes applicable rules in their original multiplication order.
    /// </summary>
    [JsonRequired]
    public IReadOnlyList<DefenseDamageRuleFactSnapshot> Rules { get; init; } = [];
}

/// <summary>
/// Describes one selected rule's numeric mechanics and its existing public adjustment snapshot.
/// </summary>
public sealed class DefenseDamageRuleFactSnapshot
{
    /// <summary>
    /// Initializes an empty numeric rule fact for protocol serialization.
    /// </summary>
    public DefenseDamageRuleFactSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the zero-based identity shared only by repeated occurrences of the same rule object.
    /// </summary>
    [JsonRequired]
    public int RuleIdentity { get; init; }

    /// <summary>
    /// Gets or initializes resolved activation, with null preserving an unresolved public condition.
    /// </summary>
    [JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? Active { get; init; }

    /// <summary>
    /// Gets or initializes the affected damage category, or null when both categories apply.
    /// </summary>
    [JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Category { get; init; }

    /// <summary>
    /// Gets or initializes whether the factor remains conditional even when activation is known.
    /// </summary>
    [JsonRequired]
    public bool Conditional { get; init; }

    /// <summary>
    /// Gets or initializes affected type identifiers, with an empty list applying to every type.
    /// </summary>
    [JsonRequired]
    public IReadOnlyList<string> Types { get; init; } = [];

    /// <summary>
    /// Gets or initializes whether the selected rule has move-specific public metadata.
    /// </summary>
    [JsonRequired]
    public bool HasMoves { get; init; }

    /// <summary>
    /// Gets or initializes membership in the game's separately displayed all-type effect group.
    /// </summary>
    [JsonRequired]
    public bool Global { get; init; }

    /// <summary>
    /// Gets or initializes a scalar factor, mutually exclusive with the per-type factor map.
    /// </summary>
    public double? Factor { get; init; }

    /// <summary>
    /// Gets or initializes per-type numeric factors, with an absent type leaving that type unaffected.
    /// </summary>
    public IReadOnlyDictionary<string, double>? TypeFactors { get; init; }

    /// <summary>
    /// Gets or initializes the already-filtered public rule snapshot used in adjustment rows.
    /// </summary>
    [JsonRequired]
    public DefenseRuleSnapshot Snapshot { get; init; } = new();
}
