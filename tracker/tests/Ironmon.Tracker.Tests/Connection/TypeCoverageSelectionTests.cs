namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies current-move defaults and manual type-selection retention.
/// </summary>
public sealed class TypeCoverageSelectionTests
{
    /// <summary>
    /// Initializes type-coverage selection tests.
    /// </summary>
    public TypeCoverageSelectionTests()
    {
    }

    /// <summary>
    /// Verifies that only unique Physical and Special move types become defaults.
    /// </summary>
    [Fact]
    public void MoveSelectorUsesUniqueDamagingMoveTypes()
    {
        IReadOnlyList<string> result = TrackerTypeCoverageMoveSelector.SelectCurrentMoveTypes(
        [
            CreateMove("EMBER", "FIRE", MoveCategory.Special),
            CreateMove("TACKLE", "NORMAL", MoveCategory.Physical),
            CreateMove("GROWL", "NORMAL", MoveCategory.Status),
            CreateMove("FLAME_WHEEL", "FIRE", MoveCategory.Physical)
        ]);

        Assert.Equal(["NORMAL", "FIRE"], result);
    }

    /// <summary>
    /// Verifies manual choices survive ordinary refreshes of the same Pokemon.
    /// </summary>
    [Fact]
    public void SelectionRetainsManualChoiceForSamePokemon()
    {
        TypeCoverageSelectionState state = new();
        state.UpdateCurrentPokemon("pokemon-1", ["NORMAL", "FIRE"]);
        state.Toggle("WATER");
        state.UpdateCurrentPokemon("pokemon-1", ["ELECTRIC"]);

        Assert.Equal(["NORMAL", "FIRE", "WATER"], state.SelectedTypes);
        Assert.Equal(["ELECTRIC"], state.CurrentMoveTypes);
        Assert.True(state.IsManual);
    }

    /// <summary>
    /// Verifies a different Pokemon resets selection to its current damaging moves.
    /// </summary>
    [Fact]
    public void SelectionResetsForDifferentPokemon()
    {
        TypeCoverageSelectionState state = new();
        state.UpdateCurrentPokemon("pokemon-1", ["FIRE"]);
        state.Toggle("WATER");
        state.UpdateCurrentPokemon("pokemon-2", ["GRASS"]);

        Assert.Equal(["GRASS"], state.SelectedTypes);
        Assert.False(state.IsManual);
    }

    /// <summary>
    /// Verifies Current Moves restores automatic tracking after manual changes.
    /// </summary>
    [Fact]
    public void ResetRestoresCurrentMovesAndAutomaticUpdates()
    {
        TypeCoverageSelectionState state = new();
        state.UpdateCurrentPokemon("pokemon-1", ["FIRE"]);
        state.Toggle("WATER");
        state.ResetToCurrentMoves();
        state.UpdateCurrentPokemon("pokemon-1", ["ELECTRIC"]);

        Assert.Equal(["ELECTRIC"], state.SelectedTypes);
        Assert.False(state.IsManual);
    }

    /// <summary>
    /// Creates one current player move snapshot.
    /// </summary>
    /// <param name="id">The move identifier.</param>
    /// <param name="type">The move type.</param>
    /// <param name="category">The damage category.</param>
    /// <returns>The move snapshot.</returns>
    private static PlayerMoveSnapshot CreateMove(string id, string type, MoveCategory category)
        => new() { Id = id, Name = id, Type = type, Category = category };
}
