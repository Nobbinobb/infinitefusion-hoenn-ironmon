using Ironmon.Sqlite.Definitions;
using Ironmon.Tracker.Collection.Queries.Tables;

namespace Ironmon.Tracker.Collection.Queries.Definitions;

/// <summary>
/// Defines generated coverage storage operations.
/// </summary>
internal static partial class CollectionQueries
{
    private const string _disposedCoverageGap = "disposed";

    /// <summary>
    /// Declares typed coverage queries for compile-time generation.
    /// </summary>
    private static partial class Definitions
    {
        /// <summary>
        /// Retains an interrupted unknown tail with exact nullable frontier and complete original detail.
        /// </summary>
        /// <param name="id">The exact sixteen-byte coverage gap identity.</param>
        /// <param name="scope">The exact retained lifetime scope identity.</param>
        /// <param name="stream">The exact sixteen-byte source stream identity.</param>
        /// <param name="first">The exact eight-byte first missing source sequence, or null when unknown.</param>
        /// <param name="reason">The complete retained interrupted-tail reason.</param>
        /// <param name="now">The recording time in UTC Unix milliseconds.</param>
        /// <param name="payload">The complete retained JSON coverage detail.</param>
        /// <returns>The provider affected-row count, including zero for suppressed duplicates.</returns>
        public static SqlQuery<int> InsertCoverageGap(byte[] id, long scope, byte[] stream, byte[]? first, string reason, long now, byte[] payload)
        {
            return Sql.Insert(new CoverageGapRow { Id = id, Scope = scope, Stream = stream, First = first, Reason = reason, State = _disposedCoverageGap, Opened = now, Closed = now, Unknown = 1, Format = 1, Payload = payload })
                .OnConflict()
                .DoNothing()
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Retains each actual gap-to-affected-family relationship once.
        /// </summary>
        /// <param name="id">The exact sixteen-byte coverage gap identity.</param>
        /// <param name="family">The registered aggregate family identity.</param>
        /// <returns>The provider affected-row count, including zero for suppressed duplicates.</returns>
        public static SqlQuery<int> AddCoverageGapFamily(byte[] id, int family)
        {
            return Sql.Insert(new CoverageGapFamilyRow { Id = id, Family = family })
                .OnConflict()
                .DoNothing()
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Retains each actual retirement-to-gap relationship once.
        /// </summary>
        /// <param name="stream">The exact sixteen-byte source stream identity.</param>
        /// <param name="id">The exact sixteen-byte coverage gap identity.</param>
        /// <returns>The provider affected-row count, including zero for suppressed duplicates.</returns>
        public static SqlQuery<int> AddRetirementGap(byte[] stream, byte[] id)
        {
            return Sql.Insert(new RetirementGapRow { Stream = stream, Id = id })
                .OnConflict()
                .DoNothing()
                .ExecuteNonQuery();
        }
    }
}
