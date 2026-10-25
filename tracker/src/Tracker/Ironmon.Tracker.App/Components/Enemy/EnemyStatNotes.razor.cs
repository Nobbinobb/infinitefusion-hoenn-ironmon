using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Enemy;

/// <summary>
/// Renders manual stat annotations for the selected opposing Pokemon.
/// </summary>
public partial class EnemyStatNotes
{
    private const string _hp = "HP";
    private const string _specialAttack = "SPA";
    private const string _specialDefense = "SPD";
    private const string _attack = "ATK";
    private const string _defense = "DEF";
    private const string _speed = "SPE";
    private const string _annotationClass = "obsidian-stat-note";
    private const string _positiveClass = "obsidian-positive";
    private const string _negativeClass = "obsidian-negative";
    private const string _plusKey = "Enemy.Stats.Plus";
    private const string _minusKey = "Enemy.Stats.Minus";
    private const string _emptyKey = "Enemy.Stats.Empty";
    private const string _plusSymbol = "+";
    private const string _minusSymbol = "−";
    private const string _emptySymbol = "·";
    private const string _annotationSymbolsPattern = "([+−])";
    private readonly (string Name, EnemyStat Stat)[] _stats =
        [(_hp, EnemyStat.Hp), (_specialAttack, EnemyStat.SpecialAttack), (_specialDefense, EnemyStat.SpecialDefense),
         (_attack, EnemyStat.Attack), (_defense, EnemyStat.Defense), (_speed, EnemyStat.Speed)];
    private bool _helpOpen;
    private string? _helpEnemyId;

    /// <summary>
    /// Dismisses help when the displayed individual changes.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (_helpEnemyId != Enemy?.EnemyId)
            _helpOpen = false;

        _helpEnemyId = Enemy?.EnemyId;
    }

    /// <summary>
    /// Opens the explanation of manual annotations.
    /// </summary>
    private void OpenHelp() => _helpOpen = true;

    /// <summary>
    /// Closes the annotation explanation.
    /// </summary>
    private void CloseHelp() => _helpOpen = false;

    /// <summary>
    /// Gets or initializes tracker-owned run knowledge.
    /// </summary>
    [Inject]
    private TrackerKnowledgeStore Knowledge { get; set; } = null!;

    /// <summary>
    /// Gets or sets the selected opposing Pokemon.
    /// </summary>
    [Parameter]
    public EnemyPokemonSnapshot? Enemy { get; set; }

    /// <summary>
    /// Cycles one visible enemy annotation.
    /// </summary>
    /// <param name="stat">The annotated stat.</param>
    /// <param name="forward">Whether to cycle forward.</param>
    private void CycleAnnotation(EnemyStat stat, bool forward)
    {
        if (Enemy is not null)
            Knowledge.CycleAnnotation(Enemy.SpeciesId, stat, forward);
    }

    /// <summary>
    /// Gets the CSS classes for an enemy annotation button.
    /// </summary>
    /// <param name="stat">The annotated stat.</param>
    /// <returns>The annotation button CSS classes.</returns>
    private string GetAnnotationClass(EnemyStat stat)
    {
        return GetAnnotation(stat) switch
        {
            EnemyStatAnnotation.Plus => $"{_annotationClass} {_positiveClass}",
            EnemyStatAnnotation.Minus => $"{_annotationClass} {_negativeClass}",
            _ => _annotationClass
        };
    }

    /// <summary>
    /// Gets the accessible text for an enemy annotation.
    /// </summary>
    /// <param name="stat">The annotated stat.</param>
    /// <returns>The annotation text.</returns>
    private string GetAnnotationText(EnemyStat stat) => GetAnnotation(stat) switch
    {
        EnemyStatAnnotation.Plus => Text[_plusKey],
        EnemyStatAnnotation.Minus => Text[_minusKey],
        _ => Text[_emptyKey]
    };

    /// <summary>
    /// Gets the visible symbol for an enemy annotation.
    /// </summary>
    /// <param name="stat">The annotated stat.</param>
    /// <returns>The annotation symbol.</returns>
    private string GetAnnotationSymbol(EnemyStat stat) => GetAnnotation(stat) switch
    {
        EnemyStatAnnotation.Plus => _plusSymbol,
        EnemyStatAnnotation.Minus => _minusSymbol,
        _ => _emptySymbol
    };

    /// <summary>
    /// Gets the current annotation for one stat.
    /// </summary>
    /// <param name="stat">The annotated stat.</param>
    /// <returns>The current annotation.</returns>
    private EnemyStatAnnotation GetAnnotation(EnemyStat stat)
        => Enemy is null ? EnemyStatAnnotation.Empty : Knowledge.GetAnnotation(Enemy.SpeciesId, stat);

}
