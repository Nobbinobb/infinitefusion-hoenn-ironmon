using Ironmon.Tracker.Protocol;
using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components;

/// <summary>
/// Renders the complete generated level-up learnset.
/// </summary>
public partial class LookupMoveList
{
    private ObservedMoveSnapshot? _selectedMove;

    /// <summary>
    /// Gets or sets the reconstructed Pokemon information.
    /// </summary>
    [Parameter]
    public PokemonLookupSnapshot Pokemon { get; set; } = null!;

    /// <summary>
    /// Opens full information for one generated move.
    /// </summary>
    /// <param name="move">The selected move.</param>
    private void SelectMove(ObservedMoveSnapshot move) 
        => _selectedMove = move;

    /// <summary>
    /// Closes the generated move information.
    /// </summary>
    private void CloseMove() 
        => _selectedMove = null;
}
