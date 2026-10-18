using Ironmon.Sqlite.Querying;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Defines reads of actual retained ownership history for completed-run inspection.
/// </summary>
internal static partial class CollectionQueries
{
    /// <summary>
    /// Declares ownership history queries for compile-time generation.
    /// </summary>
    private static partial class Definitions
    {
        /// <summary>
        /// Reads configuration headers for individuals owned during exactly one attempt.
        /// </summary>
        /// <param name="attempt">The exact sixteen-byte collection attempt identity.</param>
        /// <returns>Retained configuration headers, including individuals subsequently switched away.</returns>
        public static SqlQuery<IEnumerable<OwnedLookupRecord>> ReadOwnedLookup(byte[] attempt)
        {
            return Sql.From<OwnedIndividualRow>()
                .Join<OwnedConfigurationRow>((individual, configuration) => configuration.Individual == individual.Id)
                .Where((individual, configuration) => individual.Attempt == attempt)
                .Select((individual, configuration) => new OwnedLookupRecord { Individual = individual.Id, Revision = configuration.Revision, Species = configuration.Species, Form = configuration.Form, Detail = configuration.Detail })
                .AsEnumerable();
        }
    }
}
