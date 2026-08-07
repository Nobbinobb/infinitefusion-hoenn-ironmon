using System.Text.Json;
using Ironmon.Tracker.Connection;
using Ironmon.Tracker.Protocol;

namespace Ironmon.Tracker.Tests;

/// <summary>
/// Verifies compact completed-run recipe persistence.
/// </summary>
public sealed class CompletedRunArchiveTests
{
    /// <summary>
    /// Initializes completed-run archive tests.
    /// </summary>
    public CompletedRunArchiveTests()
    {
    }

    /// <summary>
    /// Verifies that a stored recipe survives archive reconstruction without lookup results.
    /// </summary>
    [Fact]
    public void StorePersistsOnlyCompletedRunRecipe()
    {
        string root = Path.Combine(Path.GetTempPath(), "IronmonTrackerTests", Guid.NewGuid().ToString("N"));
        TrackerKnowledgeOptions options = new(root);
        CompletedRunArchive archive = new(options);
        CompletedRunRecipePayload recipe = CreateRecipe("run-archive");

        archive.Store(recipe);

        CompletedRunArchive reloaded = new(options);
        CompletedRunRecipePayload stored = Assert.Single(reloaded.Recipes);
        Assert.Equal("run-archive", stored.RunId);
        Assert.Equal(98765, stored.Seed);
        string recipePath = Path.Combine(root, "runs", "run-archive", "recipe.json");
        Assert.True(File.Exists(recipePath));
        Assert.DoesNotContain("lookup", File.ReadAllText(recipePath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a valid compact completed-run recipe.
    /// </summary>
    /// <param name="runId">The stable test run identifier.</param>
    /// <returns>The recipe.</returns>
    private static CompletedRunRecipePayload CreateRecipe(string runId) => new()
    {
        RunId = runId,
        Seed = 98765,
        Result = "lost",
        GameVersion = "6.8.0",
        IronmonVersion = "0.3.3",
        Configuration = JsonSerializer.SerializeToElement(new { wild_policy = "mixed", trainer_policy = "mixed" }),
        SpeciesGeneratorVersion = 1,
        AbilityGeneratorVersion = 3,
        PlayerFusionGeneratorVersion = 2,
        SpeciesPoolFingerprint = "species",
        AbilityPoolFingerprint = "abilities",
        FusionPoolFingerprint = "fusions"
    };
}
