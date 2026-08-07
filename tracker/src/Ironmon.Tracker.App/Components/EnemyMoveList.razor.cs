using Ironmon.Tracker.Connection;
using Ironmon.Tracker.Protocol;
using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components;

/// <summary>
/// Renders remembered moves applicable to one selected opposing Pokemon.
/// </summary>
public partial class EnemyMoveList
{
    private readonly int[] _moveSlots = [1, 2, 3, 4];
    private ObservedMoveSnapshot? _selectedMove;

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
    /// Gets or sets legally visible player types used for effectiveness.
    /// </summary>
    [Parameter]
    public IReadOnlyList<string>? PlayerTypes { get; set; }

    /// <summary>
    /// Gets remembered moves applicable to the selected enemy's visible level.
    /// </summary>
    /// <returns>The selected enemy's remembered moves.</returns>
    private IReadOnlyList<ObservedMoveSnapshot> GetMoves() =>
        Enemy is null ? [] : Knowledge.GetDisplayedMoves(Enemy.SpeciesId, Enemy.Level);

    /// <summary>
    /// Formats observed enemy PP without implying unknown starting PP usage.
    /// </summary>
    /// <param name="move">The remembered move.</param>
    /// <returns>The observed PP text.</returns>
    private static string FormatObservedPp(ObservedMoveSnapshot move) =>
        move.PpAfterUse is null ? $"-- / {move.TotalPp} PP" : $"{move.PpAfterUse} / {move.TotalPp} PP";

    /// <summary>
    /// Opens full information for one remembered move.
    /// </summary>
    /// <param name="move">The selected move.</param>
    private void SelectMove(ObservedMoveSnapshot move)
        => _selectedMove = move;

    /// <summary>
    /// Closes the selected move information.
    /// </summary>
    private void CloseMove()
        => _selectedMove = null;
}
