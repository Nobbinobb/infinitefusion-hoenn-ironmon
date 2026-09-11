using System.Text.Json;

namespace Ironmon.Tracker.Connection.Areas;

/// <summary>
/// Owns durable per-run area discoveries and complete disclosed entry information.
/// </summary>
public sealed class AreaDiscoveryStore
{
    private const string _encounterPrefix = "encounter:";
    private const string _fusionPrefix = "encounter_fusion:";
    private const string _standardSameOrigin = "standard_same";
    private const string _standardCrossOrigin = "standard_cross";
    private const string _overworldSameOrigin = "overworld_same";
    private const string _overworldCrossOrigin = "overworld_cross";
    private readonly Dictionary<string, PersistedAreaDiscoveries> _runs = [];
    private readonly TrackerKnowledgeOptions _options;
    private readonly Lock _sync = new();

    /// <summary>
    /// Initializes tracker-owned area discovery persistence.
    /// </summary>
    /// <param name="options">The tracker-owned persistence location.</param>
    public AreaDiscoveryStore(TrackerKnowledgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// Occurs after a run's discoveries or complete disclosed entries change.
    /// </summary>
    public event EventHandler<AreaDiscoveryChangedEventArgs>? Changed;

    /// <summary>
    /// Gets the most recent non-fatal persistence error.
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Persists one idempotent discovery package before it is acknowledged.
    /// </summary>
    /// <param name="runId">The owning run identifier.</param>
    /// <param name="package">The received discovery package.</param>
    /// <returns>Whether the package is safely persisted and may be acknowledged.</returns>
    public bool RecordPackage(string runId, AreaDiscoveryPackagePayload package)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ValidatePackage(package);
        bool changed = false;
        bool persisted = true;
        lock (_sync)
        {
            PersistedAreaDiscoveries run = GetOrLoad(runId);
            HashSet<string> keys = GetOrCreateKeys(run, package.AreaId, package.Category);
            List<string> added = [.. package.EntryKeys.Distinct(StringComparer.Ordinal).Where(keys.Add)];
            changed = added.Count > 0;
            if (changed)
            {
                run.Revision++;
                persisted = Save(runId, run);
                if (!persisted)
                {
                    foreach (string key in added)
                        keys.Remove(key);

                    run.Revision--;
                    changed = false;
                }
            }
        }

        if (changed)
            Changed?.Invoke(this, new AreaDiscoveryChangedEventArgs(runId, package.AreaId, package.Category));

        return persisted;
    }

    /// <summary>
    /// Gets the discovery keys known for one run, area, and category.
    /// </summary>
    /// <param name="runId">The owning run identifier.</param>
    /// <param name="areaId">The stable area identifier.</param>
    /// <param name="category">The requested category.</param>
    /// <returns>The stable discovered entry keys.</returns>
    public IReadOnlyList<string> GetKeys(string runId, string areaId, AreaContentCategory category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(areaId);
        lock (_sync)
        {
            PersistedAreaDiscoveries run = GetOrLoad(runId);
            if (!TryGetKeys(run, areaId, category, out HashSet<string>? keys) || keys is null)
                return [];

            return [.. keys.Order(StringComparer.Ordinal)];
        }
    }

    /// <summary>
    /// Gets the completed discovery count for one run, area, and category.
    /// </summary>
    /// <param name="runId">The owning run identifier.</param>
    /// <param name="areaId">The stable area identifier.</param>
    /// <param name="category">The requested category.</param>
    /// <param name="overworldEncounters">The active encounter mode, or null to include both archived mechanics.</param>
    /// <returns>The number of distinct discovery keys for the displayed mechanic.</returns>
    public int GetCount(string runId, string areaId, AreaContentCategory category, bool? overworldEncounters = null)
    {
        IReadOnlyList<string> keys = GetKeys(runId, areaId, category);
        return category == AreaContentCategory.Encounter
            ? keys.Count(key => IsEncounterCounted(key, overworldEncounters))
            : keys.Count;
    }

    /// <summary>
    /// Counts authored slots separately from exact chance-fusion discoveries for the displayed encounter mode.
    /// </summary>
    /// <param name="runId">The owning run identifier.</param>
    /// <param name="areaId">The stable area identifier.</param>
    /// <param name="overworldEncounters">The active mode, or null for both archived mechanics.</param>
    /// <returns>The authored-slot count and eligible chance-fusion count.</returns>
    public (int Slots, int Fusions) GetEncounterCounts(string runId, string areaId, bool? overworldEncounters = null)
    {
        IReadOnlyList<string> keys = GetKeys(runId, areaId, AreaContentCategory.Encounter);
        int slots = keys.Count(key => key.StartsWith(_encounterPrefix, StringComparison.Ordinal));
        int fusions = keys.Count(key => key.StartsWith(_fusionPrefix, StringComparison.Ordinal) && IsEncounterCounted(key, overworldEncounters));
        return (slots, fusions);
    }

    /// <summary>
    /// Matches authored slots and encountered fusion possibilities against the displayed mechanic.
    /// </summary>
    /// <param name="key">The persisted discovery key.</param>
    /// <param name="overworldEncounters">The active mode, or null for both archived mechanics.</param>
    /// <returns>Whether the discovery belongs in the displayed encountered count.</returns>
    private static bool IsEncounterCounted(string key, bool? overworldEncounters)
    {
        if (key.StartsWith(_encounterPrefix, StringComparison.Ordinal))
            return true;

        if (!key.StartsWith(_fusionPrefix, StringComparison.Ordinal))
            return false;

        string[] parts = key.Split(':');
        if (parts.Length != 9)
            return false;

        return (overworldEncounters != true && parts[2] is _standardSameOrigin or _standardCrossOrigin)
            || (overworldEncounters != false && parts[2] is _overworldSameOrigin or _overworldCrossOrigin);
    }

    /// <summary>
    /// Gets the tracker-owned discovery revision for one run.
    /// </summary>
    /// <param name="runId">The owning run identifier.</param>
    /// <returns>The current revision.</returns>
    public long GetRevision(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        lock (_sync)
            return GetOrLoad(runId).Revision;
    }

    /// <summary>
    /// Restores tracker-owned disclosed entries into a reconstructed archived response.
    /// </summary>
    /// <param name="runId">The owning archived run identifier.</param>
    /// <param name="response">The game-reconstructed archived response.</param>
    /// <param name="itemReconstructionAvailable">Whether the archived recipe can reconstruct undisclosed randomized items.</param>
    /// <returns>The response with persisted run-owned details restored.</returns>
    public AreaLookupDetailResponsePayload RestoreArchivedDetails(string runId, AreaLookupDetailResponsePayload response, bool itemReconstructionAvailable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(response);
        lock (_sync)
        {
            PersistedAreaDiscoveries run = GetOrLoad(runId);
            IReadOnlyList<AreaTrainerEntryPayload> trainers = [.. response.Trainers.Select(entry => run.Trainers.TryGetValue(entry.EntryId, out AreaTrainerEntryPayload? persisted) ? RestoreTrainerDetails(persisted, entry) : entry)];
            IReadOnlyList<AreaEncounterEntryPayload> encounters = [.. response.Encounters.Select(entry => run.Encounters.GetValueOrDefault(entry.EntryId) ?? entry)];
            IReadOnlyList<AreaItemEntryPayload> items = [.. response.Items.Select(entry =>
            {
                if (run.Items.TryGetValue(entry.EntryId, out AreaItemEntryPayload? persisted))
                    return persisted;

                if (itemReconstructionAvailable)
                    return entry;

                return new AreaItemEntryPayload
                {
                    EntryId = entry.EntryId,
                    MapId = entry.MapId,
                    X = entry.X,
                    Y = entry.Y,
                    Kind = entry.Kind,
                    Hidden = entry.Hidden,
                    Collected = entry.Collected,
                    DetailsRevealed = false,
                    Items = []
                };
            })];

            return new AreaLookupDetailResponsePayload
            {
                AreaId = response.AreaId,
                Name = response.Name,
                Category = response.Category,
                Revision = response.Revision,
                Offset = response.Offset,
                Limit = response.Limit,
                TotalCount = response.TotalCount,
                Pending = response.Pending,
                OverworldEncounters = response.OverworldEncounters,
                EncounterEnvironment = response.EncounterEnvironment,
                EncounterEnvironments = response.EncounterEnvironments,
                Trainers = trainers,
                Encounters = encounters,
                EncounterFusions = response.EncounterFusions,
                Items = items
            };
        }
    }

    /// <summary>
    /// Persists newly disclosed complete entries and reconciles base-game completion discoveries.
    /// </summary>
    /// <param name="runId">The owning run identifier.</param>
    /// <param name="response">The game detail response.</param>
    /// <returns>Whether tracker-owned data changed.</returns>
    public bool RecordDetails(string runId, AreaLookupDetailResponsePayload response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(response);
        bool changed;
        lock (_sync)
        {
            PersistedAreaDiscoveries run = Copy(GetOrLoad(runId));
            HashSet<string> keys = GetOrCreateKeys(run, response.AreaId, response.Category);
            changed = response.Category switch
            {
                AreaContentCategory.Trainer => RecordTrainers(run, keys, response.Trainers),
                AreaContentCategory.Encounter => RecordEncounters(run, keys, response.Encounters),
                AreaContentCategory.Item => RecordItems(run, keys, response.Items),
                _ => false
            };

            if (changed)
            {
                run.Revision++;
                if (Save(runId, run))
                {
                    _runs[runId] = run;
                }
                else
                {
                    changed = false;
                }
            }
        }

        if (changed)
            Changed?.Invoke(this, new AreaDiscoveryChangedEventArgs(runId, response.AreaId, response.Category));

        return changed;
    }

    /// <summary>
    /// Creates a mutable copy so failed persistence cannot alter the live run document.
    /// </summary>
    /// <param name="source">The currently persisted run document.</param>
    /// <returns>An independent copy of the run document.</returns>
    private static PersistedAreaDiscoveries Copy(PersistedAreaDiscoveries source)
    {
        Dictionary<string, Dictionary<string, HashSet<string>>> discoveries = new(StringComparer.Ordinal);
        foreach ((string areaId, Dictionary<string, HashSet<string>> categories) in source.Discoveries)
        {
            Dictionary<string, HashSet<string>> copiedCategories = new(StringComparer.Ordinal);
            foreach ((string category, HashSet<string> keys) in categories)
                copiedCategories[category] = new HashSet<string>(keys, StringComparer.Ordinal);

            discoveries[areaId] = copiedCategories;
        }

        return new PersistedAreaDiscoveries
        {
            SchemaVersion = source.SchemaVersion,
            Revision = source.Revision,
            Discoveries = discoveries,
            Trainers = new Dictionary<string, AreaTrainerEntryPayload>(source.Trainers, StringComparer.Ordinal),
            Encounters = new Dictionary<string, AreaEncounterEntryPayload>(source.Encounters, StringComparer.Ordinal),
            Items = new Dictionary<string, AreaItemEntryPayload>(source.Items, StringComparer.Ordinal)
        };
    }

    /// <summary>
    /// Validates a received discovery package before persistence.
    /// </summary>
    /// <param name="package">The received package.</param>
    private static void ValidatePackage(AreaDiscoveryPackagePayload package)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(package.PackageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(package.AreaId);
        if (!Enum.IsDefined(package.Category))
            throw new ArgumentOutOfRangeException(nameof(package), "A discovery package requires a supported category.");

        if (package.EntryKeys is null || package.EntryKeys.Count == 0 || package.EntryKeys.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A discovery package requires non-empty entry keys.", nameof(package));
    }

    /// <summary>
    /// Supplements recorded trainer identities with archived battle details missing from older discoveries.
    /// </summary>
    /// <param name="persisted">The authoritative recorded party and completion state.</param>
    /// <param name="reconstructed">The party reconstructed under the archive's generation profile.</param>
    /// <returns>The recorded entry with compatible missing battle details restored.</returns>
    private static AreaTrainerEntryPayload RestoreTrainerDetails(AreaTrainerEntryPayload persisted, AreaTrainerEntryPayload reconstructed)
    {
        return new AreaTrainerEntryPayload
        {
            EntryId = persisted.EntryId,
            MapId = persisted.MapId,
            TrainerType = persisted.TrainerType,
            TrainerName = persisted.TrainerName,
            PartySize = persisted.PartySize,
            Defeated = persisted.Defeated,
            DetailsRevealed = persisted.DetailsRevealed,
            Party = [.. persisted.Party.Select(member => RestoreTrainerPokemonDetails(member, reconstructed))]
        };
    }

    /// <summary>
    /// Restores missing archived battle data only when the recorded slot, species, and level agree.
    /// </summary>
    /// <param name="persisted">The recorded party member.</param>
    /// <param name="reconstructed">The reconstructed trainer entry.</param>
    /// <returns>The recorded member supplemented by authorized matching archive data.</returns>
    private static AreaTrainerPokemonPayload RestoreTrainerPokemonDetails(AreaTrainerPokemonPayload persisted, AreaTrainerEntryPayload reconstructed)
    {
        AreaTrainerPokemonPayload? matching = reconstructed.DetailsRevealed ? reconstructed.Party.FirstOrDefault(member => member.Slot == persisted.Slot && member.SpeciesId == persisted.SpeciesId && member.Level == persisted.Level) : null;
        if (matching is null)
            return persisted;

        bool restoreAbilities = !persisted.AbilitiesRevealed && matching.AbilitiesRevealed;
        bool restoreMoves = !persisted.MovesRevealed && matching.MovesRevealed;
        return new AreaTrainerPokemonPayload
        {
            Slot = persisted.Slot,
            SpeciesId = persisted.SpeciesId,
            SpeciesName = persisted.SpeciesName,
            Level = persisted.Level,
            SpritePath = persisted.SpritePath,
            AbilitiesRevealed = persisted.AbilitiesRevealed || restoreAbilities,
            MovesRevealed = persisted.MovesRevealed || restoreMoves,
            Abilities = restoreAbilities ? matching.Abilities : persisted.Abilities,
            Moves = restoreMoves ? matching.Moves : persisted.Moves
        };
    }

    /// <summary>
    /// Records complete trainer entries and their base-game completion state.
    /// </summary>
    /// <param name="run">The mutable run document.</param>
    /// <param name="keys">The mutable discovery key set.</param>
    /// <param name="entries">The returned trainer entries.</param>
    /// <returns>Whether persisted data changed.</returns>
    private static bool RecordTrainers(PersistedAreaDiscoveries run, HashSet<string> keys, IEnumerable<AreaTrainerEntryPayload> entries)
    {
        bool changed = false;
        foreach (AreaTrainerEntryPayload entry in entries)
        {
            if (entry.Defeated)
                changed |= keys.Add(entry.EntryId);

            if (entry.DetailsRevealed)
                changed |= StoreChanged(run.Trainers, entry.EntryId, entry, AreaDiscoveryEntryComparer.AreEquivalent);
        }

        return changed;
    }

    /// <summary>
    /// Records complete encounter entries and their discovery state.
    /// </summary>
    /// <param name="run">The mutable run document.</param>
    /// <param name="keys">The mutable discovery key set.</param>
    /// <param name="entries">The returned encounter entries.</param>
    /// <returns>Whether persisted data changed.</returns>
    private static bool RecordEncounters(PersistedAreaDiscoveries run, HashSet<string> keys, IEnumerable<AreaEncounterEntryPayload> entries)
    {
        bool changed = false;
        foreach (AreaEncounterEntryPayload entry in entries)
        {
            if (entry.Encountered)
                changed |= keys.Add(entry.EntryId);

            if (entry.DetailsRevealed)
                changed |= StoreChanged(run.Encounters, entry.EntryId, entry, AreaDiscoveryEntryComparer.AreEquivalent);
        }

        return changed;
    }

    /// <summary>
    /// Records complete item entries and their base-game collection state.
    /// </summary>
    /// <param name="run">The mutable run document.</param>
    /// <param name="keys">The mutable discovery key set.</param>
    /// <param name="entries">The returned item entries.</param>
    /// <returns>Whether persisted data changed.</returns>
    private static bool RecordItems(PersistedAreaDiscoveries run, HashSet<string> keys, IEnumerable<AreaItemEntryPayload> entries)
    {
        bool changed = false;
        foreach (AreaItemEntryPayload entry in entries)
        {
            if (entry.Collected)
                changed |= keys.Add(entry.EntryId);

            if (entry.DetailsRevealed)
                changed |= StoreChanged(run.Items, entry.EntryId, entry, AreaDiscoveryEntryComparer.AreEquivalent);
        }

        return changed;
    }

    /// <summary>
    /// Adds or replaces a complete entry when its persisted value changed.
    /// </summary>
    /// <typeparam name="TEntry">The protocol entry type.</typeparam>
    /// <param name="entries">The persisted entry map.</param>
    /// <param name="key">The stable entry key.</param>
    /// <param name="entry">The complete entry.</param>
    /// <param name="equivalent">Compares all persisted fields.</param>
    /// <returns>Whether the persisted entry changed.</returns>
    private static bool StoreChanged<TEntry>(Dictionary<string, TEntry> entries, string key, TEntry entry, Func<TEntry, TEntry, bool> equivalent)
    {
        if (entries.TryGetValue(key, out TEntry? existing)
            && equivalent(existing, entry))
            return false;

        entries[key] = entry;
        return true;
    }

    /// <summary>
    /// Gets or creates one mutable discovery key set.
    /// </summary>
    /// <param name="run">The mutable run document.</param>
    /// <param name="areaId">The stable area identifier.</param>
    /// <param name="category">The discovery category.</param>
    /// <returns>The mutable key set.</returns>
    private static HashSet<string> GetOrCreateKeys(PersistedAreaDiscoveries run, string areaId, AreaContentCategory category)
    {
        if (!run.Discoveries.TryGetValue(areaId, out Dictionary<string, HashSet<string>>? categories))
        {
            categories = [];
            run.Discoveries.Add(areaId, categories);
        }

        string categoryKey = CategoryKey(category);
        if (!categories.TryGetValue(categoryKey, out HashSet<string>? keys))
        {
            keys = [];
            categories.Add(categoryKey, keys);
        }

        return keys;
    }

    /// <summary>
    /// Tries to obtain one persisted discovery key set.
    /// </summary>
    /// <param name="run">The run document.</param>
    /// <param name="areaId">The stable area identifier.</param>
    /// <param name="category">The discovery category.</param>
    /// <param name="keys">The discovered keys when present.</param>
    /// <returns>Whether the key set exists.</returns>
    private static bool TryGetKeys(PersistedAreaDiscoveries run, string areaId, AreaContentCategory category, out HashSet<string>? keys)
    {
        keys = null;
        return run.Discoveries.TryGetValue(areaId, out Dictionary<string, HashSet<string>>? categories)
            && categories.TryGetValue(CategoryKey(category), out keys);
    }

    /// <summary>
    /// Converts a category into its stable persistence key.
    /// </summary>
    /// <param name="category">The category.</param>
    /// <returns>The stable category key.</returns>
    private static string CategoryKey(AreaContentCategory category) => category switch
    {
        AreaContentCategory.Trainer => "trainer",
        AreaContentCategory.Encounter => "encounter",
        AreaContentCategory.Item => "item",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };

    /// <summary>
    /// Gets or lazily loads one run document.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    /// <returns>The mutable run document.</returns>
    private PersistedAreaDiscoveries GetOrLoad(string runId)
    {
        if (_runs.TryGetValue(runId, out PersistedAreaDiscoveries? run))
            return run;

        run = Load(runId);
        _runs.Add(runId, run);
        return run;
    }

    /// <summary>
    /// Loads one run document without making persistence failure fatal.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    /// <returns>The loaded or empty document.</returns>
    private PersistedAreaDiscoveries Load(string runId)
    {
        try
        {
            string path = GetPath(runId);
            if (!File.Exists(path))
                return new PersistedAreaDiscoveries();

            PersistedAreaDiscoveries run = JsonSerializer.Deserialize<PersistedAreaDiscoveries>(File.ReadAllText(path), TrackerJson.Options) ?? new PersistedAreaDiscoveries();
            if (run.SchemaVersion != 1)
                throw new JsonException($"Unsupported area-discovery schema {run.SchemaVersion}.");

            LastError = null;
            return run;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            LastError = exception.Message;
            return new PersistedAreaDiscoveries();
        }
    }

    /// <summary>
    /// Atomically saves one run document without making persistence failure fatal.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    /// <param name="run">The run document.</param>
    private bool Save(string runId, PersistedAreaDiscoveries run)
    {
        try
        {
            string path = GetPath(runId);
            TrackerAtomicFileWriter.WriteAllText(path, JsonSerializer.Serialize(run, TrackerJson.Options));
            LastError = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LastError = exception.Message;
            return false;
        }
    }

    /// <summary>
    /// Gets the safe area-discovery path for one run.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    /// <returns>The tracker-owned JSON path.</returns>
    private string GetPath(string runId)
    {
        string directory = string.Concat(runId.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
        return Path.Combine(_options.RootDirectory, TrackerStorageNames.RunsDirectory, directory, TrackerStorageNames.AreaDiscoveriesFile);
    }
}
