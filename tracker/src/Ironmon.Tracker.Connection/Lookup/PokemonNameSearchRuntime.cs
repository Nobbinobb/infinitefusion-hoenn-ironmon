using System.Text.Json;

namespace Ironmon.Tracker.Connection.Lookup;

/// <summary>
/// Prepares protocol serialization, catalog construction, and native sorting before the first user search.
/// </summary>
internal static class PokemonNameSearchRuntime
{
    private const string PreparationContext = "name-search-runtime-preparation";
    private const string NormalIdentity = "A:0";
    private const string Name = "A";
    private const string LowerName = "a";
    private const string NormalSearch = "a a";
    private const string PackedPool = "AQ==";

    /// <summary>
    /// Warms serialization and matching with a tiny private index on the local runtime.
    /// </summary>
    /// <remarks>
    /// Preparation contacts no game session and publishes no shared catalog. Missing native sort
    /// support disables tracker matching; unrelated preparation failures propagate to the caller.
    /// </remarks>
    /// <returns>True when preparation succeeds, or false when the native library, entry point, or platform is unavailable.</returns>
    internal static bool Prepare()
    {
        try
        {
            PokemonNameCatalogPayload payload = new()
            {
                GenerationProfileId = PreparationContext,
                ContextToken = PreparationContext,
                NormalSpeciesCount = 1,
                Prefixes = [null, Name], Suffixes = [null, LowerName],
                CapitalizedSuffixes = [null, Name],
                LowercasePrefixes = [null, LowerName],
                LowercaseTrimmedPrefixes = [null, string.Empty],
                LowercaseSuffixes = [null, LowerName],
                LowercaseCapitalizedSuffixes = [null, LowerName],
                NormalSpecies = [new() { SpeciesId = NormalIdentity, SpeciesName = Name, Fusion = false }],
                NormalizedNormalNames = [LowerName],
                NormalizedNormalSearchTexts = [NormalSearch],
                PackedCustomFusionPool = PackedPool
            };
            JsonElement wire = JsonSerializer.SerializeToElement(new PokemonSearchResponsePayload { NameCatalog = payload }, TrackerJson.Options);
            PokemonNameCatalogPayload imported = TrackerJson.DeserializePayload<PokemonSearchResponsePayload>(wire).NameCatalog!;
            PokemonSearchCatalog catalog = new(imported, CancellationToken.None);
            catalog.Search(LowerName, 0, 1, CancellationToken.None);
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
