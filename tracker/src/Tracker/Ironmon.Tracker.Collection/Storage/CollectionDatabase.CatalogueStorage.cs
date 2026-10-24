using Ironmon.Sqlite.Definitions;
using System.Globalization;
using System.Text.Json;
using Ironmon.Tracker.Collection.Queries.Definitions;
using Ironmon.Tracker.Collection.Queries.Results;
using Ironmon.Tracker.Collection.Storage.Encoding;
using Ironmon.Tracker.Collection.Storage.Maintenance;

namespace Ironmon.Tracker.Collection.Storage;

/// <summary>
/// Retains complete immutable definitions in a shared compression stream with relational bindings.
/// </summary>
internal sealed partial class CollectionDatabase
{
    private const string _catalogueMetadataKind = "catalogue_metadata";
    private const string _blockValuesTable = "block_values";

    /// <summary>
    /// Builds a single current catalogue block while retaining every definition and stable numeric ID.
    /// </summary>
    /// <returns>The shared immutable definition block identifier.</returns>
    private long CurrentCatalogueBlock()
    {
        object[][] families = [.. _catalogue.Families.Select(family => new object[] { family.Id, family.Definition })];
        object[][] groups = [.. _catalogue.DirectGroups.Select((definition, index) => new object[] { index + 1, definition })];
        object[][] relationships = [.. _catalogue.Relationships.Select((definition, index) => new object[] { index + 1, definition })];
        return StoreCatalogueBlock(families, groups, relationships);
    }

    /// <summary>
    /// Stores complete definition objects together without changing their logical content or per-row digests.
    /// </summary>
    /// <param name="families">Stable family IDs paired with original definition objects.</param>
    /// <param name="groups">Stable summary group IDs paired with original definition objects.</param>
    /// <param name="relationships">Stable relationship IDs paired with original definition objects.</param>
    /// <returns>The shared immutable block identifier.</returns>
    private long StoreCatalogueBlock(object[][] families, object[][] groups, object[][] relationships)
        => Block(_catalogueMetadataKind, CollectionEncoding.Canonical(JsonSerializer.SerializeToElement(new { format = 1, families, direct_groups = groups, relationships })));

    /// <summary>
    /// Collects historical definitions from the database rather than substituting the current embedded catalogue.
    /// </summary>
    /// <returns>The new shared block ID, or zero when no historical catalogue rows exist.</returns>
    private long MigrateCatalogueBlock()
    {
        object[][] families = ReadHistoricalDefinitions(CollectionQueries.FindStoredFamilyDefinitions(_db, _transaction), true);
        object[][] groups = ReadHistoricalDefinitions(CollectionQueries.FindLegacyGroupDefinitions(_db, _transaction), false);
        object[][] relationships = ReadHistoricalDefinitions(CollectionQueries.FindLegacyContractDefinitions(_db, _transaction), false);
        if (families.Length + groups.Length + relationships.Length == 0)
            return 0;

        long block = StoreCatalogueBlock(families, groups, relationships);
        CollectionQueries.SaveCatalogueDefinitionBlock(_db, _transaction, block);
        return block;
    }

    /// <summary>
    /// Reads complete old JSON definitions or resolves their original immutable blocks.
    /// </summary>
    /// <param name="stored">The original definitions in unchanged identity order.</param>
    /// <param name="blocks">Whether the second column identifies an immutable block.</param>
    /// <returns>The complete definitions paired with their unchanged numeric IDs.</returns>
    private object[][] ReadHistoricalDefinitions(IEnumerable<HistoricalDefinitionRecord> stored, bool blocks)
    {
        List<(long Id, object Value)> rows = [.. stored.Select(row => (row.Id, row.Value))];

        List<object[]> definitions = [];
        foreach (var (id, value) in rows)
        {
            using JsonDocument? json = blocks ? null : JsonDocument.Parse((string)value);
            definitions.Add([id, blocks ? ReadStoredBlock(Convert.ToInt64(value, CultureInfo.InvariantCulture)) : json!.RootElement.Clone()]);
        }

        return [.. definitions];
    }

    /// <summary>
    /// Removes obsolete standalone definitions only if no retained relational record references them.
    /// </summary>
    private void RemoveUnusedDefinitionBlocks()
    {
        IReadOnlyList<string> tables = CollectionSqliteMaintenance.ReadTableNames(_db, _transaction);
        List<(string Table, string Column)> checks = [];
        foreach (string table in tables)
        {
            foreach (SqliteForeignKeyReference reference in CollectionSqliteMaintenance.ReadForeignKeyReferences(_db, _transaction, table))
            {
                if (reference.Table == _blockValuesTable)
                    checks.Add((table, reference.Column));
            }
        }

        List<long> candidates = [.. CollectionQueries.FindLegacyDefinitionBlocks(_db, _transaction).Select(row => row.Id)];
        foreach (long block in candidates)
        {
            if (checks.All(reference => CollectionSqliteMaintenance.CountColumnReferences(_db, _transaction, reference.Table, reference.Column, block) == 0))
                CollectionQueries.DeleteUnusedDefinitionBlock(_db, _transaction, block);
        }
    }

    /// <summary>
    /// Encodes one registered dimension kind without storing repeated text per reference.
    /// </summary>
    /// <param name="kind">The exact registered kind.</param>
    /// <returns>The stable storage code from zero through six.</returns>
    private static int DimensionKindCode(string kind)
        => kind switch { _integerKind => 0, _booleanKind => 1, _textKind => 2, _nativeKind => 3, _uint64Kind => 4, _rationalKind => 5, _setKind => 6, _ => throw new InvalidDataException("Unknown dimension kind cannot be compacted.") };
}
