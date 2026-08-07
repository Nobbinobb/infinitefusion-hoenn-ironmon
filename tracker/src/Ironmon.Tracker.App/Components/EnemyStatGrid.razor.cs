using Ironmon.Tracker.Connection;
using Ironmon.Tracker.Protocol;
using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components;

/// <summary>
/// Renders manual stat annotations for the selected opposing Pokemon.
/// </summary>
public partial class EnemyStatGrid
{
    private readonly (string Name, EnemyStat Stat)[] _stats =
        [("SPE", EnemyStat.Speed), ("HP", EnemyStat.Hp), ("ATK", EnemyStat.Attack),
         ("DEF", EnemyStat.Defense), ("SPA", EnemyStat.SpecialAttack), ("SPD", EnemyStat.SpecialDefense)];

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
        => $"annotation-button {GetAnnotation(stat).ToString().ToLowerInvariant()}";

    /// <summary>
    /// Gets the accessible text for an enemy annotation.
    /// </summary>
    /// <param name="stat">The annotated stat.</param>
    /// <returns>The annotation text.</returns>
    private string GetAnnotationText(EnemyStat stat) => GetAnnotation(stat) switch
    {
        EnemyStatAnnotation.Plus => "plus",
        EnemyStatAnnotation.Minus => "minus",
        _ => "empty"
    };

    /// <summary>
    /// Gets the visible symbol for an enemy annotation.
    /// </summary>
    /// <param name="stat">The annotated stat.</param>
    /// <returns>The annotation symbol.</returns>
    private string GetAnnotationSymbol(EnemyStat stat) => GetAnnotation(stat) switch
    {
        EnemyStatAnnotation.Plus => "+",
        EnemyStatAnnotation.Minus => "−",
        _ => string.Empty
    };

    /// <summary>
    /// Gets the current annotation for one stat.
    /// </summary>
    /// <param name="stat">The annotated stat.</param>
    /// <returns>The current annotation.</returns>
    private EnemyStatAnnotation GetAnnotation(EnemyStat stat) =>
        Enemy is null ? EnemyStatAnnotation.Empty : Knowledge.GetAnnotation(Enemy.SpeciesId, stat);
}
