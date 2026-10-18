using System.Text.Json.Serialization;

namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Contains game-derived matchups, compact effects, and supporting rules filtered to tracker-visible information.
/// </summary>
public sealed class DefenseOverviewSnapshot
{
    private IReadOnlyList<DefenseTypeSnapshot>? _completedTypeMatchups;
    private bool _damageProfilesCompleted;
    private bool _damageProfilesInvalid;

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
    public IReadOnlyList<DefenseTypeSnapshot> TypeMatchups { get => _completedTypeMatchups ?? field; init; } = [];

    /// <summary>
    /// Gets broad damage factors displayed once outside the redesigned type chart.
    /// </summary>
    public IReadOnlyList<DefenseAllTypeEffectSnapshot> AllTypeEffects { get => _damageProfilesInvalid ? [] : field; init; } = [];

    /// <summary>
    /// Gets or initializes the marker requesting completion of explicitly versioned numeric rule facts.
    /// </summary>
    /// <remarks>
    /// Completion clears the marker before publication or persistence. Missing or unsupported facts
    /// remove numeric advice rather than letting incomplete profiles appear as known multipliers.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DamageProfilesDeferred { get => !_damageProfilesCompleted && field; init; }

    /// <summary>
    /// Gets or initializes public numeric rule facts consumed and hidden by local completion.
    /// </summary>
    public DefenseDamageProfileFactsSnapshot? DamageProfileFacts { get => _damageProfilesCompleted ? null : field; init; }

    /// <summary>
    /// Gets or initializes whether unavailable deferred input prevents showing numeric damage advice.
    /// </summary>
    /// <remarks>
    /// Valid native or completed profiles clear this indication. False values are omitted from the
    /// wire, while failures remain explicit after persistence without retaining transient rule facts.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DamageProfilesUnavailable { get => _damageProfilesCompleted ? _damageProfilesInvalid : field; init; }

    /// <summary>
    /// Records completed numeric type rows or removes unavailable numeric advice before publication.
    /// </summary>
    /// <remarks>
    /// Existing complete native profiles take precedence. Invalid deferred input preserves the game's
    /// selected protection and recovery summaries while removing incomplete type and all-type factors.
    /// Repeated completion keeps the previously resolved result.
    /// </remarks>
    /// <param name="typeMatchups">The completed type rows, or null when deferred input cannot be completed safely.</param>
    public void CompleteDamageProfiles(IReadOnlyList<DefenseTypeSnapshot>? typeMatchups)
    {
        if (_damageProfilesCompleted)
            return;

        if (TypeMatchups is { Count: > 0 } nativeRows && nativeRows.All(row => row is not null && row.HasExplicitDamageProfiles))
            typeMatchups = nativeRows;

        _completedTypeMatchups = typeMatchups ?? [];
        _damageProfilesInvalid = typeMatchups is null;
        _damageProfilesCompleted = true;
    }

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
