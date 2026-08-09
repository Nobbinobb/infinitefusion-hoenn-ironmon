namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Verifies generated move-access protocol serialization.
/// </summary>
public sealed class MoveAccessSnapshotCodecTests
{
    /// <summary>
    /// Initializes the move-access protocol tests.
    /// </summary>
    public MoveAccessSnapshotCodecTests()
    {
    }

    /// <summary>
    /// Verifies that all four channels and acquisition details round-trip.
    /// </summary>
    [Fact]
    public void SnapshotRoundTripsAllMoveAccessChannels()
    {
        MoveAccessEntrySnapshot learnset = CreateEntry("TACKLE", "level_up", "Body", learnedLevel: 1);
        MoveAccessEntrySnapshot egg = CreateEntry("GROWL", "egg", "Head");
        MoveAccessEntrySnapshot machine = CreateEntry("SURF", "tm", "Body + Head", itemId: "TM001", itemName: "TM Surf");
        MoveAccessEntrySnapshot tutor = CreateEntry("METRONOME", "ordinary_tutor", "Body", tutorId: "slot-1", tutorName: "Slateport City");
        MoveAccessSnapshot snapshot = new()
        {
            Learnset = [learnset],
            EggMoves = [egg],
            MachineMoves = [machine],
            TutorMoves = [tutor],
            OrdinaryTutorAbstractCount = 12,
            OrdinaryTutorSupportedCount = 1
        };

        MoveAccessSnapshot result = TrackerJson.DeserializePayload<MoveAccessSnapshot>(TrackerJson.SerializePayload(snapshot));

        Assert.Equal("TACKLE", Assert.Single(result.Learnset).Id);
        Assert.Equal("GROWL", Assert.Single(result.EggMoves).Id);
        Assert.Equal("TM001", Assert.Single(result.MachineMoves).ItemId);
        Assert.Equal("Slateport City", Assert.Single(result.TutorMoves).TutorName);
        Assert.Equal(12, result.OrdinaryTutorAbstractCount);
        Assert.Equal(1, result.OrdinaryTutorSupportedCount);
    }

    /// <summary>
    /// Creates one representative generated move-access row.
    /// </summary>
    /// <param name="id">The stable move identifier.</param>
    /// <param name="source">The access channel.</param>
    /// <param name="sourceLabel">The component source label.</param>
    /// <param name="learnedLevel">The optional learned level.</param>
    /// <param name="itemId">The optional machine item identifier.</param>
    /// <param name="itemName">The optional machine item name.</param>
    /// <param name="tutorId">The optional tutor slot identifier.</param>
    /// <param name="tutorName">The optional tutor location.</param>
    /// <returns>The move-access row.</returns>
    private static MoveAccessEntrySnapshot CreateEntry(string id, string source, string sourceLabel, int? learnedLevel = null, string? itemId = null, string? itemName = null, string? tutorId = null, string? tutorName = null) => new()
    {
        Id = id,
        Name = id,
        LearnedLevel = learnedLevel,
        Source = source,
        SourceLabel = sourceLabel,
        Type = "NORMAL",
        Category = MoveCategory.Status,
        TotalPp = 10,
        ItemId = itemId,
        ItemName = itemName,
        TutorId = tutorId,
        TutorName = tutorName
    };
}
