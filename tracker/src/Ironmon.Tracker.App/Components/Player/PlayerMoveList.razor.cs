using Microsoft.AspNetCore.Components;
using System.Globalization;

namespace Ironmon.Tracker.App.Components.Player;

/// <summary>
/// Renders the player's four current move slots and generated learnset progress.
/// </summary>
public partial class PlayerMoveList
{
    private readonly int[] _moveSlots = [1, 2, 3, 4];
    private PlayerMoveSnapshot? _selectedMove;

    /// <summary>
    /// Gets or sets the active player Pokemon.
    /// </summary>
    [Parameter]
    public PlayerPokemonSnapshot? Player { get; set; }

    /// <summary>
    /// Gets or sets legally visible opposing types used for effectiveness.
    /// </summary>
    [Parameter]
    public IReadOnlyList<string>? TargetTypes { get; set; }

    /// <summary>
    /// Gets or sets the selected target's current evasion stage.
    /// </summary>
    [Parameter]
    public int TargetEvasionStage { get; set; }

    /// <summary>
    /// Formats progress through the generated level-up learnset.
    /// </summary>
    /// <returns>The learned, maximum, and next-level display.</returns>
    private string FormatLearnsetProgress()
    {
        if (Player is null)
            return "--/-- (--)";

        LearnsetProgressSnapshot progress = Player.LearnsetProgress;
        string nextLevel = progress.NextMoveLevel?.ToString(CultureInfo.InvariantCulture) ?? "—";
        return $"{progress.LearnedMoves}/{progress.MaximumMoves} ({nextLevel})";
    }

    /// <summary>
    /// Opens full information for one current move.
    /// </summary>
    /// <param name="move">The selected move.</param>
    private void SelectMove(PlayerMoveSnapshot move)
        => _selectedMove = move;

    /// <summary>
    /// Closes the selected move information.
    /// </summary>
    private void CloseMove()
        => _selectedMove = null;
}
