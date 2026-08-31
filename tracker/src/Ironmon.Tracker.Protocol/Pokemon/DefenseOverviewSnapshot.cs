namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Contains game-derived matchups, compact effects, and supporting rules filtered to tracker-visible information.
/// </summary>
public sealed class DefenseOverviewSnapshot
{
    /// <summary>
    /// Initializes an empty defense payload for serialization.
    /// </summary>
    public DefenseOverviewSnapshot()
    {
    }

    /// <summary>
    /// Whether battle conditions are available.
    /// </summary>
    public bool InBattle { get; init; }

    /// <summary>
    /// Whether concealed enemy state is excluded.
    /// </summary>
    public bool LimitedInformation { get; init; }

    /// <summary>
    /// Gets the individually known ability name retained in the payload, not displayed in the defense view.
    /// </summary>
    public string? AbilityName { get; init; }

    /// <summary>
    /// Gets supporting ability text retained in the payload, not displayed in the compact defense view.
    /// </summary>
    public string? AbilityDescription { get; init; }

    /// <summary>
    /// Whether the known ability is suppressed.
    /// </summary>
    public bool AbilitySuppressed { get; init; }

    /// <summary>
    /// Gets baseline matchups and combined physical and special factors, including neutral entries omitted by the view.
    /// </summary>
    public IReadOnlyList<DefenseTypeSnapshot> TypeMatchups { get; init; } = [];

    /// <summary>
    /// Gets the compact, deduplicated protection labels and their move catalogs.
    /// </summary>
    public IReadOnlyList<DefenseEffectSnapshot> Protections { get; init; } = [];

    /// <summary>
    /// Gets recovery triggers with separate healing amounts disclosed on click, without source information.
    /// </summary>
    public IReadOnlyList<DefenseEffectSnapshot> Recovery { get; init; } = [];

    /// <summary>
    /// Gets known numeric modifiers supporting the combined factors; these rules are not separate UI entries.
    /// </summary>
    public IReadOnlyList<DefenseRuleSnapshot> Modifiers { get; init; } = [];

    /// <summary>
    /// Gets known status rules used to derive compact protections, including inactive or conditional rules.
    /// </summary>
    public IReadOnlyList<DefenseRuleSnapshot> StatusProtections { get; init; } = [];

    /// <summary>
    /// Gets known move-protection rules and public move catalogs used to derive compact effects.
    /// </summary>
    public IReadOnlyList<DefenseRuleSnapshot> MoveProtections { get; init; } = [];

    /// <summary>
    /// Gets supporting defense rules, some of which are omitted because they cannot be displayed compactly.
    /// </summary>
    public IReadOnlyList<DefenseRuleSnapshot> OtherProtections { get; init; } = [];
}
