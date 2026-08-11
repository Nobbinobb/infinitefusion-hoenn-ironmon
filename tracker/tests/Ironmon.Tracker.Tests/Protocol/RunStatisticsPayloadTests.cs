using System.Text.Json;

namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Verifies versioned run-statistics protocol compatibility.
/// </summary>
public sealed class RunStatisticsPayloadTests
{
    /// <summary>
    /// Initializes the run-statistics payload tests.
    /// </summary>
    public RunStatisticsPayloadTests()
    {
    }

    /// <summary>
    /// Verifies that current-attempt statistics round-trip with canonical field names.
    /// </summary>
    [Fact]
    public void CurrentAttemptStatisticsRoundTrip()
    {
        RunStatisticsPayload statistics = new()
        {
            SchemaVersion = 1,
            AttemptNumber = 7,
            Seed = 12345,
            Result = "active",
            ActiveSeconds = 65.5,
            AttemptsStarted = 7,
            AttemptsLost = 5,
            AttemptsWon = 1,
            AttemptsAbandoned = 0,
            BattlesCompleted = 9,
            HighestPlayerLevel = 31,
            BadgesEarned = 2,
            TotalItemHealing = 120,
            WastedItemHealing = 15,
            ItemsUsed = 6,
            ItemsBySource = new Dictionary<string, Dictionary<string, int>> { ["Held"] = new() { ["SITRUSBERRY"] = 1 } },
            TrainerSpeciesCounts = new Dictionary<string, int> { ["PIKACHU"] = 3 },
            TrainerSpeciesDistinct = 1,
            TrainerSpeciesMostEncountered = ["PIKACHU"],
            TrainerDefeatedCount = 8,
            TrainerDefeatedBstAverage = 400.25,
            TrainerDefeatedBstMinimum = 300,
            TrainerDefeatedBstMinimumSpecies = ["BULBASAUR"],
            TrainerDefeatedBstMaximum = 500,
            TrainerDefeatedBstMaximumSpecies = ["PIKACHU"]
        };
        GameCurrentStatePayload state = new(true, "run-7", null, 12, attemptStatistics: statistics);

        JsonElement json = TrackerJson.SerializePayload(state);
        GameCurrentStatePayload result = TrackerJson.DeserializePayload<GameCurrentStatePayload>(json);

        Assert.Equal(7, result.AttemptStatistics!.AttemptNumber);
        Assert.Equal(1, result.AttemptStatistics.ItemsBySource["Held"]["SITRUSBERRY"]);
        Assert.True(json.TryGetProperty("attempt_statistics", out _));
    }

    /// <summary>
    /// Verifies that state created before statistics were available remains readable.
    /// </summary>
    [Fact]
    public void MissingStatisticsRemainUnavailable()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "ironmon_active": true,
              "run_id": "legacy-run",
              "battle_id": null,
              "sequence": 1,
              "battle": null,
              "player": null,
              "enemies": []
            }
            """);

        GameCurrentStatePayload result = TrackerJson.DeserializePayload<GameCurrentStatePayload>(document.RootElement);

        Assert.Null(result.AttemptStatistics);
    }
}
