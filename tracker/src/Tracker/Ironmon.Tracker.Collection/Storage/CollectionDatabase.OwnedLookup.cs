using System.Text.Json;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Reads actual owned configurations independently of encountered Pokemon observations.
/// </summary>
internal sealed partial class CollectionDatabase
{
    /// <summary>
    /// Reads the latest recorded configuration for each owned individual, species and form.
    /// </summary>
    /// <param name="attempts">Exact collection attempts explicitly associated with the requested run.</param>
    /// <returns>Complete recorded payloads; no inferred or encounter-only individuals are included.</returns>
    internal JsonElement OwnedLookup(IReadOnlyList<byte[]> attempts)
    {
        List<JsonElement> records = [];
        foreach (byte[] attempt in attempts)
        {
            IEnumerable<OwnedLookupRecord> latest = CollectionQueries.ReadOwnedLookup(_db, _transaction, attempt)
                .GroupBy(row => (row.Individual, row.Species, row.Form))
                .Select(group => group.MaxBy(row => row.Revision)!)
                .OrderBy(row => row.Individual);

            foreach (OwnedLookupRecord row in latest)
                records.Add(ReadStoredBlock(row.Detail));
        }

        return JsonSerializer.SerializeToElement(records);
    }
}
