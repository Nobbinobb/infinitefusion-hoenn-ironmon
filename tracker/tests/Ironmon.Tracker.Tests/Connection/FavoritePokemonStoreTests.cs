namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies persistent Favorite Clause list behavior.
/// </summary>
public sealed class FavoritePokemonStoreTests
{
    /// <summary>
    /// Initializes the favorite-Pokemon store tests.
    /// </summary>
    public FavoritePokemonStoreTests()
    {
    }

    /// <summary>
    /// Verifies that favorites persist alphabetically and ignore duplicate stable identities.
    /// </summary>
    [Fact]
    public void FavoritesPersistWithoutDuplicates()
    {
        string root = Path.Combine(Path.GetTempPath(), $"ironmon-favorites-{Guid.NewGuid():N}");
        FavoritePokemonStore store = new(new TrackerKnowledgeOptions(root));

        Assert.True(store.Add(new PokemonSearchMatch { SpeciesId = "SQUIRTLE:0", SpeciesName = "Squirtle" }));
        Assert.True(store.Add(new PokemonSearchMatch { SpeciesId = "BULBASAUR:0", SpeciesName = "Bulbasaur" }));
        Assert.False(store.Add(new PokemonSearchMatch { SpeciesId = "bulbasaur:0", SpeciesName = "Bulbasaur" }));

        FavoritePokemonStore reloaded = new(new TrackerKnowledgeOptions(root));
        Assert.Equal(["Bulbasaur", "Squirtle"], reloaded.Favorites.Select(favorite => favorite.SpeciesName));
        Assert.True(reloaded.Remove("BULBASAUR:0"));
        Assert.Equal("Squirtle", Assert.Single(new FavoritePokemonStore(new TrackerKnowledgeOptions(root)).Favorites).SpeciesName);
    }
}
