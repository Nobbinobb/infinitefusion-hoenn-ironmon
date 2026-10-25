using System.Text.Json;
using Ironmon.Tracker.Collection.Queries.Results;
using Ironmon.Tracker.Collection.Queries.Definitions;
using Ironmon.Tracker.Collection.Storage.Encoding;

namespace Ironmon.Tracker.Collection.Storage;

/// <summary>
/// Reconstructs compact archived runner stages from retained configurations without changing stored evidence.
/// </summary>
internal sealed partial class CollectionDatabase
{
    private const string _historyDisplay = "display_metadata";
    private const string _historyName = "species_name";
    private const string _historyNickname = "nickname";
    private const string _historyLookupId = "species_id";
    private const string _historySprite = "sprite_path";
    private const string _historyAcquisition = "acquisition";
    private const string _historySource = "source_kind";
    private const string _historyStarter = "starter";
    private const string _historyTransformation = "transformation_kind";
    private const string _historyReasons = "change_reasons";
    private const string _historyEvolution = "evolution";
    private const string _historyFuse = "fuse";
    private const string _historyReverseTake = "reverse_and_take";
    private const string _historySwapReverse = "swap_and_reverse";
    private const string _historyUnfuseTake = "unfuse_and_take";
    private const string _historySwapUnfuse = "swap_and_unfuse";
    private const string _historyDefaultForm = "0";
    private const string _historyContext = "history_context";
    private const string _historyParent = "saved_parent";
    private const string _historyStream = "stream_id";
    private const string _historySequence = "sequence";
    private const string _historyAncestryKnown = "ancestry_known";

    /// <summary>
    /// Collapses consecutive configurations into real runner stages while preserving returns and peak levels.
    /// </summary>
    /// <param name="attempts">Exact collection attempts explicitly associated with the selected archived run.</param>
    /// <returns>Newest-first immutable presentation records.</returns>
    internal JsonElement RunPokemonHistory(IReadOnlyList<byte[]> attempts)
    {
        IEnumerable<RunPokemonHistoryHeader> headers = attempts.SelectMany(HistoryTimeline)
            .OrderBy(row => CollectionEncoding.ReadRevision(row.Recorded)).ThenBy(row => CollectionEncoding.ReadRevision(row.Sequence));
        List<(RunPokemonHistoryHeader First, RunPokemonHistoryHeader Last, int Peak)> stages = [];
        foreach (RunPokemonHistoryHeader row in headers)
        {
            int level = checked((int)row.Level);
            if (stages.Count > 0 && stages[^1].Last.Individual == row.Individual && stages[^1].Last.Species == row.Species && stages[^1].Last.Form == row.Form)
                stages[^1] = (stages[^1].First, row, Math.Max(stages[^1].Peak, level));
            else
                stages.Add((row, row, level));
        }

        List<RunPokemonHistoryEntry> result = [];
        for (int index = 0; index < stages.Count; index++)
        {
            var (First, Last, Peak) = stages[index];
            JsonElement first = ReadStoredBlock(First.Detail);
            JsonElement last = Last.Detail == First.Detail ? first : ReadStoredBlock(Last.Detail);
            JsonElement display = HistoryProperty(last, _historyDisplay);
            JsonElement species = HistoryProperty(last, _speciesIdentityProperty);
            string canonical = HistoryProperty(species, _speciesProperty).ToString();
            string form = HistoryProperty(species, _formProperty).ToString();
            string lookup = HistoryProperty(display, _historyLookupId).GetString() ?? string.Concat(canonical, ':', string.IsNullOrEmpty(form) ? _historyDefaultForm : form);
            string name = HistoryProperty(display, _historyName).GetString() ?? HistoryProperty(display, _historyNickname).GetString() ?? canonical;
            string? sprite = HistoryProperty(display, _historySprite).GetString() ?? HistoryProperty(HistoryProperty(first, _historyDisplay), _historySprite).GetString();
            RunPokemonTransition transition = HistoryTransition(first, index > 0 ? stages[index - 1].Last : null, First);
            result.Add(new(index + 1, lookup, name, sprite, Peak, transition));
        }

        result.Reverse();
        return JsonSerializer.SerializeToElement(result);
    }

    /// <summary>
    /// Follows saved branch cursors and excludes discarded unsaved continuations from the displayed timeline.
    /// </summary>
    /// <param name="attempt">The exact archived attempt identity.</param>
    /// <returns>The final branch and the saved portions of its ancestors, in capture order.</returns>
    private IReadOnlyList<RunPokemonHistoryHeader> HistoryTimeline(byte[] attempt)
    {
        IEnumerable<IGrouping<long, RunPokemonHistoryHeader>> branches = CollectionQueries.ReadRunPokemonHistory(_db, _transaction, attempt)
            .OrderBy(row => CollectionEncoding.ReadRevision(row.Recorded)).ThenBy(row => CollectionEncoding.ReadRevision(row.Sequence)).GroupBy(row => row.Branch);
        Dictionary<string, List<RunPokemonHistoryHeader>> savedBranches = new(StringComparer.Ordinal);
        List<RunPokemonHistoryHeader> timeline = [];
        foreach (IGrouping<long, RunPokemonHistoryHeader> branch in branches)
        {
            RunPokemonHistoryHeader first = branch.First();
            JsonElement payload = ReadStoredBlock(first.Detail);
            JsonElement context = HistoryProperty(payload, _historyContext);
            if (context.ValueKind == JsonValueKind.Object && HistoryProperty(context, _historyAncestryKnown).ValueKind != JsonValueKind.False)
            {
                timeline = [];
                JsonElement parent = HistoryProperty(context, _historyParent);
                string? parentBranch = HistoryProperty(parent, _branchProperty).GetString();
                string? stream = HistoryProperty(parent, _historyStream).GetString();
                if (parentBranch is not null && stream is not null && ulong.TryParse(HistoryProperty(parent, _historySequence).ToString(), out ulong sequence) && savedBranches.TryGetValue(parentBranch, out List<RunPokemonHistoryHeader>? ancestor))
                {
                    int end = ancestor.FindLastIndex(row => CollectionEncoding.Identifier(row.Stream) == stream && CollectionEncoding.ReadRevision(row.Sequence) <= sequence);
                    if (end >= 0)
                        timeline = [.. ancestor.Take(end + 1)];
                }
            }
            else if (HistoryProperty(HistoryProperty(payload, _historyAcquisition), _historySource).ToString() == _historyStarter)
            {
                timeline = [];
            }

            timeline.AddRange(branch);
            string? branchId = HistoryProperty(context, _branchProperty).GetString();
            if (branchId is not null)
                savedBranches[branchId] = [.. timeline];
        }

        return timeline;
    }

    /// <summary>
    /// Identifies a stage transition from its committed acquisition or species-change evidence.
    /// </summary>
    /// <param name="first">The first configuration in this stage.</param>
    /// <param name="previous">The preceding stage, or null for the first recorded runner.</param>
    /// <param name="current">The current stage's first header.</param>
    /// <returns>The supported transition, with an explicit first-recorded fallback for missing history.</returns>
    private static RunPokemonTransition HistoryTransition(JsonElement first, RunPokemonHistoryHeader? previous, RunPokemonHistoryHeader current)
    {
        if (previous?.Individual == current.Individual)
        {
            if (previous.Species == current.Species && previous.Form != current.Form)
                return RunPokemonTransition.FormChange;

            JsonElement reasons = HistoryProperty(first, _historyReasons);
            return reasons.ValueKind == JsonValueKind.Array && reasons.EnumerateArray().Any(value => value.ToString().Contains(_historyEvolution, StringComparison.OrdinalIgnoreCase))
                ? RunPokemonTransition.Evolution : RunPokemonTransition.SpeciesChange;
        }

        if (HistoryProperty(HistoryProperty(first, _historyAcquisition), _historySource).ToString() == _historyStarter)
            return RunPokemonTransition.Starter;

        string transformation = HistoryProperty(HistoryProperty(first, _lineageProperty), _historyTransformation).ToString();
        return transformation switch
        {
            _historyFuse => RunPokemonTransition.FusionPivot,
            _historyReverseTake or _historySwapReverse => RunPokemonTransition.ReversePivot,
            _historyUnfuseTake or _historySwapUnfuse => RunPokemonTransition.UnfusionPivot,
            _ => previous is null ? RunPokemonTransition.FirstRecorded : RunPokemonTransition.Pivot
        };
    }

    /// <summary>
    /// Reads an optional retained object property without inventing missing values.
    /// </summary>
    /// <param name="value">The retained object.</param>
    /// <param name="name">The native property name.</param>
    /// <returns>The recorded value, or a null JSON value for absent properties.</returns>
    private static JsonElement HistoryProperty(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement result) ? result : JsonSerializer.SerializeToElement<object?>(null);
}
