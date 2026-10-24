using Ironmon.Sqlite.Definitions;

namespace Ironmon.Tracker.Collection.Queries.Definitions;

/// <summary>
/// Exposes generated provider methods from typed domain query definitions.
/// </summary>
[SqlQueries]
internal static partial class CollectionQueries
{
    private const string _orderedCodec = "zlib-value-v2";
    private const string _activeContextState = "active";
    private const string _maintenanceDefinition = "family_definition";
    private const string _keyBattleScope = "battle";
    private const string _keyIndividualScope = "individual";
    private const string _keySessionScope = "session";
    private const string _keySaveLineageScope = "save_lineage";
    private const string _keyWardrobeSessionScope = "wardrobe_session";
}
