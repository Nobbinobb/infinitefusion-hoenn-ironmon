namespace Ironmon.Tracker.Tests.Access;

/// <summary>
/// Verifies the stable diagnostic capability catalog and implication rules.
/// </summary>
public sealed class DiagnosticCapabilityCatalogTests
{
    /// <summary>
    /// Initializes the capability-catalog tests.
    /// </summary>
    public DiagnosticCapabilityCatalogTests()
    {
    }

    /// <summary>
    /// Verifies every published capability has one unique stable definition.
    /// </summary>
    [Fact]
    public void CatalogContainsUniqueStableDefinitions()
    {
        Assert.Equal(29, DiagnosticCapabilityCatalog.All.Count);
        Assert.Equal(DiagnosticCapabilityCatalog.All.Count, DiagnosticCapabilityCatalog.All.Select(definition => definition.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(DiagnosticCapabilityCatalog.All, definition =>
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(definition.Description));
            Assert.True(DiagnosticCapabilityCatalog.IsKnown(definition.Id));
        });
    }

    /// <summary>
    /// Verifies all-active Pokemon availability includes both narrower live targets.
    /// </summary>
    [Fact]
    public void AllActivePokemonIncludesCurrentPlayerAndEnemies()
    {
        IReadOnlyList<string> effective = DiagnosticCapabilityCatalog.Expand([DiagnosticCapabilities.PokemonAllActive]);

        Assert.Equal(
        [
            DiagnosticCapabilities.PokemonAllActive,
            DiagnosticCapabilities.PokemonCurrentEnemies,
            DiagnosticCapabilities.PokemonCurrentPlayer
        ], effective);
    }

    /// <summary>
    /// Verifies expansion ignores unsupported IDs and removes duplicate direct grants.
    /// </summary>
    [Fact]
    public void ExpansionKeepsOnlyDistinctKnownCapabilities()
    {
        IReadOnlyList<string> effective = DiagnosticCapabilityCatalog.Expand(
        [
            DiagnosticCapabilities.RunSeed,
            "future.capability",
            DiagnosticCapabilities.RunSeed
        ]);

        Assert.Equal([DiagnosticCapabilities.RunSeed], effective);
    }
}
