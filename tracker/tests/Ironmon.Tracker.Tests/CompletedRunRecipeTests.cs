using System.Text.Json;
using Ironmon.Tracker.Core;

namespace Ironmon.Tracker.Tests;

/// <summary>
/// Verifies completed-run recipe invariants.
/// </summary>
public sealed class CompletedRunRecipeTests
{
    /// <summary>
    /// Initializes the completed-run recipe tests.
    /// </summary>
    public CompletedRunRecipeTests()
    {
    }

    /// <summary>
    /// Verifies that a recipe retains only deterministic reconstruction inputs.
    /// </summary>
    [Fact]
    public void ConstructorRetainsDeterministicRunInputs()
    {
        using JsonDocument configuration = JsonDocument.Parse("{\"wild_policy\":\"fusion\"}");
        CompletedRunRecipe recipe = new("run-123", 918273645, RunResult.Lost, "6.8.0", "0.4.0", configuration.RootElement, 3, 2, "species-fingerprint", "ability-fingerprint", "fusion-fingerprint");

        Assert.Equal("run-123", recipe.RunId);
        Assert.Equal(918273645, recipe.Seed);
        Assert.Equal(RunResult.Lost, recipe.Result);
        Assert.Equal("fusion", recipe.Configuration.GetProperty("wild_policy").GetString());
    }

    /// <summary>
    /// Verifies that invalid generator versions are rejected.
    /// </summary>
    [Fact]
    public void ConstructorRejectsNonPositiveGeneratorVersion()
    {
        using JsonDocument configuration = JsonDocument.Parse("{}");
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompletedRunRecipe("run-123", 1, RunResult.Lost, "6.8.0", "0.4.0", configuration.RootElement, 0, 1, "species-fingerprint", "ability-fingerprint", "fusion-fingerprint"));
    }
}
