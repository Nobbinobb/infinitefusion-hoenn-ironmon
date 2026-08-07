using Ironmon.Tracker.Core;

namespace Ironmon.Tracker.Tests;

/// <summary>
/// Verifies standard type-only move effectiveness calculations.
/// </summary>
public sealed class TypeEffectivenessRulesTests
{
    /// <summary>
    /// Initializes type-effectiveness rule tests.
    /// </summary>
    public TypeEffectivenessRulesTests()
    {
    }

    /// <summary>
    /// Verifies representative neutral, resistant, super-effective, double-weak, and immune matchups.
    /// </summary>
    /// <param name="moveType">The attacking type.</param>
    /// <param name="targetTypes">The comma-separated defending types.</param>
    /// <param name="expected">The expected combined effectiveness.</param>
    [Theory]
    [InlineData("NORMAL", "NORMAL", MoveEffectiveness.Neutral)]
    [InlineData("FIRE", "WATER", MoveEffectiveness.Half)]
    [InlineData("ELECTRIC", "WATER", MoveEffectiveness.Double)]
    [InlineData("ELECTRIC", "WATER,FLYING", MoveEffectiveness.Quadruple)]
    [InlineData("NORMAL", "GHOST", MoveEffectiveness.Immune)]
    [InlineData("GRASS", "FIRE,FLYING", MoveEffectiveness.Quarter)]
    public void CalculateReturnsCombinedTypeChartResult(string moveType, string targetTypes, MoveEffectiveness expected)
    {
        MoveEffectiveness result = TypeEffectivenessRules.Calculate(moveType, targetTypes.Split(','));
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that a duplicate single type is not treated as two defensive types.
    /// </summary>
    [Fact]
    public void CalculateIgnoresDuplicateTargetTypes()
    {
        MoveEffectiveness result = TypeEffectivenessRules.Calculate("FIRE", ["GRASS", "GRASS"]);
        Assert.Equal(MoveEffectiveness.Double, result);
    }
}
