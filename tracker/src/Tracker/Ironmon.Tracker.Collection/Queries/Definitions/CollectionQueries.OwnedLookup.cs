using Ironmon.Sqlite.Definitions;
using Ironmon.Tracker.Collection.Queries.Results;
using Ironmon.Tracker.Collection.Queries.Tables;

namespace Ironmon.Tracker.Collection.Queries.Definitions;

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
        private const string _historyRunnerRole = "runner";
        private const string _historyExitCapture = "runner_exit";

        /// <summary>
        /// Reads only committed runner stages from one exact collection attempt.
        /// </summary>
        /// <param name="attempt">The exact sixteen-byte collection attempt identity.</param>
        /// <returns>Compact configuration headers excluding materials, utility Pokemon and retirement copies.</returns>
        public static SqlQuery<IEnumerable<RunPokemonHistoryHeader>> ReadRunPokemonHistory(byte[] attempt)
        {
            return Sql.From<OwnedIndividualRow>()
                .Join<OwnedConfigurationRow>((individual, configuration) => configuration.Individual == individual.Id)
                .Where((individual, configuration) => individual.Attempt == attempt && configuration.Role == _historyRunnerRole && configuration.Retained == 1 && configuration.Capture != _historyExitCapture)
                .Select((individual, configuration) => new RunPokemonHistoryHeader { Branch = configuration.Branch, Stream = configuration.Stream, Individual = individual.Id, Species = configuration.Species, Form = configuration.Form, Level = configuration.Level, Recorded = configuration.Recorded, Sequence = configuration.Sequence, Detail = configuration.Detail })
                .AsEnumerable();
        }

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
