using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Restores a selected information page only after the same species is loaded with current access checks.
/// </summary>
public partial class PokemonLookupCard : IDisposable
{
    private const string UpdateNavigationKey = "pokemon-page";

    /// <summary>
    /// Gets the optional updater navigation registry supplied by the tracker layout.
    /// </summary>
    [CascadingParameter]
    private TrackerUpdateNavigation? UpdateNavigation { get; set; }

    /// <summary>
    /// Registers the selected information page and requests its normal authorized data after relaunch.
    /// </summary>
    /// <returns>The existing section-loading task.</returns>
    private async Task RestoreUpdateNavigationAsync()
    {
        UpdateNavigation?.Register(UpdateNavigationKey, () => new UpdateSelection(Pokemon.Identity.SpeciesId, _selectedPage));
        if (UpdateNavigation?.Take<UpdateSelection>(UpdateNavigationKey) is { } saved && saved.Species == Pokemon.Identity.SpeciesId && Enum.IsDefined(saved.Page) && CanShowPage(saved.Page))
            await SelectPageAsync(saved.Page);
    }

    /// <summary>
    /// Releases navigation capture when the card is unmounted by ordinary tracker navigation.
    /// </summary>
    public void Dispose()
        => UpdateNavigation?.Unregister(UpdateNavigationKey);

    /// <summary>
    /// Stores an information page identity without storing its Pokémon response.
    /// </summary>
    /// <remarks>
    /// Constructs the bounded information-page resume selection.
    /// </remarks>
    /// <param name="Species">The original species identity.</param>
    /// <param name="Page">The selected information page.</param>
    private sealed record UpdateSelection(string Species, PokemonInformationPage Page);
}
