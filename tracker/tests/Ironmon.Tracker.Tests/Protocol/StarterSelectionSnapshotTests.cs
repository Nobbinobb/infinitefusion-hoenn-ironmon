namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Verifies starter-selection visibility and recovery payload serialization.
/// </summary>
public sealed class StarterSelectionSnapshotTests
{
    /// <summary>
    /// Initializes the starter-selection protocol tests.
    /// </summary>
    public StarterSelectionSnapshotTests()
    {
    }

    /// <summary>
    /// Verifies that hidden choices carry no identity while revealed choices round-trip completely.
    /// </summary>
    [Fact]
    public void SelectionRoundTripsHiddenAndRevealedChoices()
    {
        StarterSelectionSnapshot selection = CreateSelection();

        JsonElement json = TrackerJson.SerializePayload(selection);
        StarterSelectionSnapshot result = TrackerJson.DeserializePayload<StarterSelectionSnapshot>(json);

        Assert.True(result.Active);
        Assert.Equal(1, result.RandomPickIndex);
        Assert.Equal(3, result.Choices.Count);
        StarterChoiceSnapshot hidden = result.Choices[0];
        Assert.False(hidden.Revealed);
        Assert.Null(hidden.SpeciesId);
        Assert.Null(hidden.SpeciesName);
        Assert.Null(hidden.SpritePath);
        Assert.Null(hidden.BaseStatTotal);
        StarterChoiceSnapshot revealed = result.Choices[1];
        Assert.True(revealed.Revealed);
        Assert.Equal("VENUSAUR:0", revealed.SpeciesId);
        Assert.Equal("Venusaur", revealed.SpeciesName);
        Assert.Equal(525, revealed.BaseStatTotal);
    }

    /// <summary>
    /// Verifies that current-state recovery includes an active starter selection.
    /// </summary>
    [Fact]
    public void CurrentStateRoundTripsStarterSelection()
    {
        GameCurrentStatePayload state = new(true, "run-7", null, 12, starterSelection: CreateSelection());

        JsonElement json = TrackerJson.SerializePayload(state);
        GameCurrentStatePayload result = TrackerJson.DeserializePayload<GameCurrentStatePayload>(json);

        Assert.Equal("run-7", result.RunId);
        Assert.True(result.StarterSelection?.Active);
        Assert.Equal(1, result.StarterSelection?.RandomPickIndex);
        Assert.Equal(3, result.StarterSelection?.Choices.Count);
    }

    /// <summary>
    /// Creates a three-slot selection containing one revealed random pick.
    /// </summary>
    /// <returns>The test starter-selection snapshot.</returns>
    private static StarterSelectionSnapshot CreateSelection() => new()
    {
        Active = true,
        RandomPickIndex = 1,
        Choices =
        [
            new StarterChoiceSnapshot { Index = 0, Revealed = false },
            new StarterChoiceSnapshot
            {
                Index = 1,
                Revealed = true,
                SpeciesId = "VENUSAUR:0",
                SpeciesName = "Venusaur",
                SpritePath = "Graphics/Battlers/3.png",
                BaseStatTotal = 525
            },
            new StarterChoiceSnapshot { Index = 2, Revealed = false }
        ]
    };
}
