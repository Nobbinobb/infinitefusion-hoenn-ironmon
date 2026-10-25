using Ironmon.Tracker.Connection.Lookup;
using System.Globalization;

namespace Ironmon.Tracker.Connection.Areas;

/// <summary>
/// Assembles encounter fusion rows from ordered game descriptors, native mapping results, and engine-confirmed sprites while preserving disclosure state.
/// </summary>
internal static class AreaFusionPageAssembler
{
    private const string _entryPrefix = "encounter_fusion";
    private const string _crossSuffix = "cross";
    private const string _speciesFormat = "B{0}H{1}:0";
    private const char _entrySeparator = ':';

    /// <summary>
    /// Validates one retained descriptor page against its mapping and sprite confirmation, then creates fusion rows in source order.
    /// </summary>
    /// <param name="page">The game-authorized page containing ordered descriptors and authored encounter entries.</param>
    /// <param name="confirmation">The engine response confirming the retained page context and selected sprite paths.</param>
    /// <param name="assignments">The native results for each distinct disclosed ordered material pair.</param>
    /// <param name="names">The immutable game-provided catalog used to compose disclosed fusion names.</param>
    /// <param name="normalSpeciesCount">The pinned catalog's component count used to decode numeric fusion identities.</param>
    /// <returns>The completed public page with fusion rows and no transport preparation metadata.</returns>
    internal static AreaLookupDetailResponsePayload Assemble(AreaLookupDetailResponsePayload page, AreaLookupDetailResponsePayload confirmation, IReadOnlyList<AreaFusionResultPayload> assignments, PokemonNameCatalog names, int normalSpeciesCount)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(confirmation);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentOutOfRangeException.ThrowIfLessThan(normalSpeciesCount, 1);
        if (page.FusionPageToken is null || page.Category != AreaContentCategory.Encounter || confirmation.Category != page.Category || confirmation.Pending || confirmation.FusionPageToken != page.FusionPageToken || confirmation.FusionNameContextToken != page.FusionNameContextToken || confirmation.AreaId != page.AreaId || confirmation.OverworldEncounters != page.OverworldEncounters || page.FusionDescriptors.Count + page.Encounters.Count > page.Limit || page.FusionDescriptors.Count > TrackerProtocol.MaximumSearchPageSize || assignments.Count > TrackerProtocol.MaximumSearchPageSize)
            throw new TrackerProtocolException("The engine fusion page confirmation does not match its descriptors.");

        Dictionary<(int Body, int Head), AreaFusionResultPayload> results = [];
        foreach (AreaFusionResultPayload assignment in assignments)
        {
            if (!results.TryAdd((assignment.BodyId, assignment.HeadId), assignment))
                throw new TrackerProtocolException("The native fusion page contains duplicate assignments.");
        }

        Dictionary<(int Body, int Head), AreaFusionPresentationPayload> sprites = [];
        foreach (AreaFusionPresentationPayload presentation in confirmation.FusionPresentations)
        {
            if (!results.TryGetValue((presentation.BodyId, presentation.HeadId), out AreaFusionResultPayload? result) || result.SpeciesNumber != presentation.SpeciesNumber || !sprites.TryAdd((presentation.BodyId, presentation.HeadId), presentation))
                throw new TrackerProtocolException("The engine fusion presentation does not match its native assignment.");
        }

        if (sprites.Count != results.Count)
            throw new TrackerProtocolException("The engine fusion presentation is incomplete.");

        List<AreaEncounterFusionEntryPayload> rows = [];
        HashSet<(int Body, int Head)> used = [];
        foreach (AreaEncounterFusionDescriptorPayload descriptor in page.FusionDescriptors)
        {
            string? speciesId = null;
            string? speciesName = null;
            string? spritePath = null;
            if (descriptor.DetailsRevealed)
            {
                FusionMaterialAssignmentPayload materials = descriptor.Materials ?? throw new TrackerProtocolException("A disclosed fusion descriptor is missing its material pair.");
                if (!results.TryGetValue((materials.BodyId, materials.HeadId), out AreaFusionResultPayload? result) || !sprites.TryGetValue((materials.BodyId, materials.HeadId), out AreaFusionPresentationPayload? presentation))
                    throw new TrackerProtocolException("A disclosed fusion descriptor has no confirmed result.");

                int body = (result.SpeciesNumber - 1) / normalSpeciesCount;
                int head = result.SpeciesNumber - body * normalSpeciesCount;
                if (body < 1 || body > normalSpeciesCount || head < 1 || head > normalSpeciesCount)
                    throw new TrackerProtocolException("The native fusion result is outside its pinned catalog.");

                speciesId = string.Format(CultureInfo.InvariantCulture, _speciesFormat, body, head);
                speciesName = names.TryGetFusionName(body, head, out string name) ? name : null;
                spritePath = presentation.SpritePath;
                used.Add((materials.BodyId, materials.HeadId));
            }
            else if (descriptor.Materials is not null)
            {
                throw new TrackerProtocolException("A hidden fusion descriptor disclosed its material pair.");
            }

            rows.Add(new AreaEncounterFusionEntryPayload
            {
                EntryId = string.Join(_entrySeparator, _entryPrefix, descriptor.MapId.ToString(CultureInfo.InvariantCulture), descriptor.Origin, descriptor.FirstVersion.ToString(CultureInfo.InvariantCulture), descriptor.FirstEncounterType, descriptor.FirstSlot.ToString(CultureInfo.InvariantCulture), descriptor.SecondVersion.ToString(CultureInfo.InvariantCulture), descriptor.SecondEncounterType, descriptor.SecondSlot.ToString(CultureInfo.InvariantCulture)),
                Origin = descriptor.Origin,
                Environment = descriptor.Environment,
                CrossEnvironment = descriptor.Origin.EndsWith(_crossSuffix, StringComparison.Ordinal),
                DetailsRevealed = descriptor.DetailsRevealed,
                Encountered = descriptor.Encountered,
                FirstEncounterType = descriptor.FirstEncounterType,
                FirstSlot = descriptor.FirstSlot,
                SecondEncounterType = descriptor.SecondEncounterType,
                SecondSlot = descriptor.SecondSlot,
                FusionChancePercent = descriptor.FusionChancePercent,
                MinimumLevel = descriptor.MinimumLevel,
                MaximumLevel = descriptor.MaximumLevel,
                SpeciesId = speciesId,
                SpeciesName = speciesName,
                SpritePath = spritePath
            });
        }

        if (used.Count != results.Count)
            throw new TrackerProtocolException("The native fusion page contains an undisclosed assignment.");

        return new AreaLookupDetailResponsePayload
        {
            AreaId = page.AreaId, Name = page.Name, Category = page.Category,
            Revision = page.Revision, Offset = page.Offset, Limit = page.Limit,
            TotalCount = page.TotalCount, Pending = false,
            OverworldEncounters = page.OverworldEncounters,
            EncounterEnvironment = page.EncounterEnvironment,
            EncounterEnvironments = page.EncounterEnvironments,
            Encounters = page.Encounters, EncounterFusions = rows,
            Trainers = page.Trainers, Items = page.Items
        };
    }
}
