using Ironmon.Sqlite.Querying;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Defines generated catalogue storage operations.
/// </summary>
internal static partial class CollectionQueries
{
    private const string _archiveMeasureCodec = "sqlite-integer-or-varint-overflow-v1";
    private const string _registeredSummaryShape = "joint_summary";

    /// <summary>
    /// Declares typed catalogue queries for compile-time generation.
    /// </summary>
    private static partial class Definitions
    {
        /// <summary>
        /// Registers an immutable family definition while retaining all existing metadata on duplicate identity.
        /// </summary>
        /// <param name="family">The registered aggregate family identity.</param>
        /// <param name="key">The exact stable family or measure key.</param>
        /// <param name="eventKey">The owning event key.</param>
        /// <param name="kind">The declared physical storage or measure kind.</param>
        /// <param name="block">The exact immutable block identity.</param>
        /// <param name="digest">The complete thirty-two-byte definition digest.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> EnsureFamilyDefinition(int family, string key, string eventKey, string kind, long block, byte[] digest)
        {
            return Sql.Insert(new AggregateFamilyRow { Id = family, Key = key, Version = 1, Event = eventKey, Kind = kind, Block = block, Digest = digest })
                .OnConflict(row => row.Id)
                .DoNothing()
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Reads the original key and digest for explicit catalogue compatibility checks.
        /// </summary>
        /// <param name="family">The registered aggregate family identity.</param>
        /// <returns>The exact selected columns, or null when absent.</returns>
        public static SqlQuery<FamilyBindingRecord?> ReadFamilyBinding(int family)
        {
            return Sql.From<AggregateFamilyRow>()
                .Where(row => row.Id == family)
                .Select(row => new FamilyBindingRecord { Key = row.Key, Digest = row.Digest })
                .FirstOrDefault();
        }

        /// <summary>
        /// Registers a measure without modifying an existing ordinal or silently suppressing a different key conflict.
        /// </summary>
        /// <param name="family">The registered aggregate family identity.</param>
        /// <param name="measure">The zero-based measure ordinal.</param>
        /// <param name="key">The exact stable family or measure key.</param>
        /// <param name="kind">The declared physical storage or measure kind.</param>
        /// <param name="unit">The declared measurement unit.</param>
        /// <param name="merge">The declared measure merge operation.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> EnsureFamilyMeasure(int family, int measure, string key, string kind, string unit, string merge)
        {
            return Sql.Insert(new FamilyMeasureRow { Family = family, Id = measure, Key = key, Kind = kind, Codec = _archiveMeasureCodec, Unit = unit, Merge = merge })
                .OnConflict(row => new { row.Family, row.Id })
                .DoNothing()
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Deletes the exact obsolete definition block after the caller checks all retained references.
        /// </summary>
        /// <param name="block">The exact immutable block identity.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> DeleteUnusedDefinitionBlock(long block)
        {
            return Sql.From<ImmutableBlockRow>()
                .Where(row => row.Id == block)
                .Delete();
        }

        /// <summary>
        /// Rebinds all retained family definitions to their lossless shared historical catalogue body.
        /// </summary>
        /// <param name="block">The shared body retaining every historical definition.</param>
        /// <returns>The number of family rows updated.</returns>
        public static SqlQuery<int> SaveCatalogueDefinitionBlock(long block)
        {
            return Sql.From<AggregateFamilyRow>().AllRows().Update(row => new AggregateFamilyRow { Block = block }).ExecuteNonQuery();
        }

        /// <summary>
        /// Selects old standalone family-definition bodies for retained-reference validation.
        /// </summary>
        /// <returns>The complete original block identities.</returns>
        public static SqlQuery<IEnumerable<MaintenanceIdentityRecord>> FindLegacyDefinitionBlocks()
        {
            return Sql.From<MaintenanceBlockViewRow>().Where(row => row.Kind == _maintenanceDefinition).Select(row => new MaintenanceIdentityRecord { Id = row.Id }).AsEnumerable();
        }

        /// <summary>
        /// Streams original family block references in ascending full-width family identity order.
        /// </summary>
        /// <returns>Matching original rows; enumeration owns and disposes the command and reader.</returns>
        public static SqlQuery<IEnumerable<HistoricalDefinitionRecord>> FindStoredFamilyDefinitions()
        {
            return Sql.From<HistoricalFamilyRow>()
                .OrderBy(row => row.Id)
                .Select(row => new HistoricalDefinitionRecord { Id = row.Id, Value = row.Block })
                .AsEnumerable();
        }

        /// <summary>
        /// Registers a summary group without replacing existing metadata on duplicate identity.
        /// </summary>
        /// <param name="group">The registered summary group identity.</param>
        /// <param name="key">The exact stable definition key and retained meaning.</param>
        /// <param name="version">The positive definition version.</param>
        /// <param name="variant">The exact summary variant key.</param>
        /// <param name="digest">The complete thirty-two-byte definition digest.</param>
        /// <param name="block">The complete retained definition block identity.</param>
        /// <returns>The provider affected-row count, including zero for suppressed duplicates.</returns>
        public static SqlQuery<int> EnsureSummaryGroup(int group, string key, int version, string variant, byte[] digest, long block)
        {
            return Sql.Insert(new SummaryGroupRow { Id = group, Key = key, Version = version, Variant = variant, Shape = _registeredSummaryShape, Digest = digest, Block = block, CodecVersion = 1 })
                .OnConflict(row => row.Id)
                .DoNothing()
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Reads the original projection key and complete digest for catalogue compatibility validation.
        /// </summary>
        /// <param name="group">The registered summary group identity.</param>
        /// <returns>The exact selected columns, or null when absent.</returns>
        public static SqlQuery<SummaryGroupBindingRecord?> ReadSummaryGroupBinding(int group)
        {
            return Sql.From<SummaryGroupRow>()
                .Where(row => row.Id == group)
                .Select(row => new SummaryGroupBindingRecord { Key = row.Key, Digest = row.Digest })
                .FirstOrDefault();
        }

        /// <summary>
        /// Retains each actual source-family relationship once.
        /// </summary>
        /// <param name="group">The registered summary group identity.</param>
        /// <param name="family">The registered aggregate family identity.</param>
        /// <returns>The provider affected-row count, including zero for suppressed duplicates.</returns>
        public static SqlQuery<int> AddSummaryGroupSource(int group, int family)
        {
            return Sql.Insert(new SummaryGroupSourceRow { Group = group, Family = family })
                .OnConflict()
                .DoNothing()
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Retains the original dimension metadata under any existing unique key.
        /// </summary>
        /// <param name="group">The registered summary group identity.</param>
        /// <param name="ordinal">The zero-based dimension, measure or field ordinal.</param>
        /// <param name="key">The exact stable definition key and retained meaning.</param>
        /// <param name="kind">The declared dimension or measure kind.</param>
        /// <param name="required">The required-dimension marker, zero or one.</param>
        /// <returns>The provider affected-row count, including zero for suppressed duplicates.</returns>
        public static SqlQuery<int> EnsureSummaryDimension(int group, int ordinal, string key, string kind, int required)
        {
            return Sql.Insert(new SummaryDimensionRow { Group = group, Ordinal = ordinal, Key = key, Kind = kind, Meaning = key, Required = required })
                .OnConflict()
                .DoNothing()
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Retains every measure field while preserving native suppression of any unique-key duplicate.
        /// </summary>
        /// <param name="group">The registered summary group identity.</param>
        /// <param name="ordinal">The zero-based dimension, measure or field ordinal.</param>
        /// <param name="key">The exact stable definition key and retained meaning.</param>
        /// <param name="kind">The declared dimension or measure kind.</param>
        /// <param name="domain">The declared exact measure domain.</param>
        /// <param name="merge">The declared measure merge operation.</param>
        /// <param name="unit">The declared unit of measurement.</param>
        /// <returns>The provider affected-row count, including zero for suppressed duplicates.</returns>
        public static SqlQuery<int> EnsureSummaryMeasure(int group, int ordinal, string key, string kind, string domain, string merge, string unit)
        {
            return Sql.Insert(new SummaryMeasureRow { Group = group, Ordinal = ordinal, Key = key, Kind = kind, Domain = domain, Merge = merge, Unit = unit, Meaning = key })
                .OnConflict()
                .DoNothing()
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Retains complete original relationship contract metadata under any unique-key duplicate.
        /// </summary>
        /// <param name="contract">The registered relationship contract identity.</param>
        /// <param name="key">The exact stable definition key and retained meaning.</param>
        /// <param name="version">The positive definition version.</param>
        /// <param name="digest">The complete thirty-two-byte definition digest.</param>
        /// <param name="block">The complete retained definition block identity.</param>
        /// <returns>The provider affected-row count, including zero for suppressed duplicates.</returns>
        public static SqlQuery<int> EnsureRelationshipContract(int contract, string key, int version, byte[] digest, long block)
        {
            return Sql.Insert(new RelationshipContractRow { Id = contract, Key = key, Version = version, Digest = digest, Block = block })
                .OnConflict()
                .DoNothing()
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Retains complete original relationship field metadata under any unique-key duplicate.
        /// </summary>
        /// <param name="contract">The registered relationship contract identity.</param>
        /// <param name="ordinal">The zero-based dimension, measure or field ordinal.</param>
        /// <param name="key">The exact stable definition key and retained meaning.</param>
        /// <param name="type">The declared logical field type.</param>
        /// <param name="binding">The declared physical storage binding.</param>
        /// <returns>The provider affected-row count, including zero for suppressed duplicates.</returns>
        public static SqlQuery<int> EnsureRelationshipField(int contract, int ordinal, string key, string type, string binding)
        {
            return Sql.Insert(new RelationshipFieldRow { Contract = contract, Ordinal = ordinal, Key = key, Type = type, Binding = binding, Meaning = key })
                .OnConflict()
                .DoNothing()
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Retains each actual summary-to-contract relationship once.
        /// </summary>
        /// <param name="group">The registered summary group identity.</param>
        /// <param name="contract">The registered relationship contract identity.</param>
        /// <returns>The provider affected-row count, including zero for suppressed duplicates.</returns>
        public static SqlQuery<int> AddSummaryContract(int group, int contract)
        {
            return Sql.Insert(new SummaryContractRow { Group = group, Contract = contract })
                .OnConflict()
                .DoNothing()
                .ExecuteNonQuery();
        }
    }
}
