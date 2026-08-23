using System.Text.Json;

namespace Ironmon.Tracker.Connection.Knowledge;

/// <summary>
/// Owns remembered move discoveries and manual enemy annotations for the active run.
/// </summary>
public sealed class TrackerKnowledgeStore
{
    private readonly TrackerKnowledgeOptions _options;
    private readonly Lock _sync = new();
    private PersistedRunKnowledge _knowledge = new();
    private string? _runId;

    /// <summary>
    /// Initializes the tracker knowledge store.
    /// </summary>
    /// <param name="options">The tracker-owned persistence location.</param>
    /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
    public TrackerKnowledgeStore(TrackerKnowledgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// Occurs after remembered knowledge or annotations change.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the active run identifier when one is selected.
    /// </summary>
    public string? RunId
    {
        get
        {
            lock (_sync)
                return _runId;
        }
    }

    /// <summary>
    /// Gets the most recent non-fatal persistence error.
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Selects and loads tracker-owned knowledge for a run.
    /// </summary>
    /// <param name="runId">The active run identifier, or null outside a run.</param>
    public void SelectRun(string? runId)
    {
        lock (_sync)
        {
            if (string.Equals(_runId, runId, StringComparison.Ordinal))
                return;
            _runId = runId;
            _knowledge = Load(runId);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Records legal level-up moves exposed by the player's current snapshot.
    /// </summary>
    /// <param name="player">The complete player snapshot.</param>
    public void ObservePlayer(PlayerPokemonSnapshot player)
    {
        ArgumentNullException.ThrowIfNull(player);
        ObserveMoves(player.SpeciesId, player.LevelUpMoves.Where(move => move.Source == "level_up"));
        if (player.AbilityDetails is not null)
            ObserveAbility(player.SpeciesId, player.AbilityDetails);
    }

    /// <summary>
    /// Records the visible level and any ability included in a legal enemy snapshot.
    /// </summary>
    /// <param name="enemy">The complete legal enemy snapshot.</param>
    public void ObserveEnemy(EnemyPokemonSnapshot enemy)
    {
        ArgumentNullException.ThrowIfNull(enemy);
        bool changed = false;
        lock (_sync)
        {
            if (_runId is null)
                return;

            int highestLevel = _knowledge.HighestLevels.GetValueOrDefault(enemy.SpeciesId);
            if (enemy.Level > highestLevel)
            {
                _knowledge.HighestLevels[enemy.SpeciesId] = enemy.Level;
                changed = true;
            }

            if (enemy.LastAbility is not null)
                changed |= AddAbility(enemy.SpeciesId, enemy.LastAbility);

            if (changed)
                Save();
        }

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Records an ability made observable by an enemy activation.
    /// </summary>
    /// <param name="observation">The enemy ability-reveal observation.</param>
    public void ObserveEnemyAbility(EnemyAbilityRevealedPayload observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ObserveAbility(observation.SpeciesId, observation.Ability);
    }

    /// <summary>
    /// Records a move made observable by enemy use.
    /// </summary>
    /// <param name="observation">The enemy move-use observation.</param>
    public void ObserveEnemyMove(EnemyMoveUsedPayload observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ObserveMoves(observation.SpeciesId, [observation.Move]);
    }

    /// <summary>
    /// Gets the newest four applicable remembered moves for an enemy level.
    /// </summary>
    /// <param name="speciesId">The stable species and form identifier.</param>
    /// <param name="level">The enemy's visible level.</param>
    /// <returns>The remembered moves in chronological moveset order.</returns>
    public IReadOnlyList<ObservedMoveSnapshot> GetDisplayedMoves(string speciesId, int level)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        lock (_sync)
        {
            if (!_knowledge.Moves.TryGetValue(speciesId, out List<ObservedMoveSnapshot>? moves))
                return [];

            IReadOnlyList<DiscoveredMove> selected = MoveDiscoveryRules.SelectDisplayedMoves(moves.Select(ToCoreDiscovery), level);
            return selected.Select(discovery => SelectObservation(moves, discovery)).ToArray();
        }
    }

    /// <summary>
    /// Gets all legally discovered abilities for a species and form, ordered by name.
    /// </summary>
    /// <param name="speciesId">The stable species and form identifier.</param>
    /// <returns>The remembered abilities ordered by localized name.</returns>
    public IReadOnlyList<AbilitySnapshot> GetAbilities(string speciesId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        lock (_sync)
        {
            if (!_knowledge.Abilities.TryGetValue(speciesId, out List<AbilitySnapshot>? abilities))
                return [];

            return [.. abilities.OrderBy(ability => ability.Name, StringComparer.CurrentCulture)];
        }
    }

    /// <summary>
    /// Gets the highest level encountered for a species and form.
    /// </summary>
    /// <param name="speciesId">The stable species and form identifier.</param>
    /// <returns>The highest encountered level, or zero when the species has not been observed.</returns>
    public int GetHighestLevel(string speciesId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        lock (_sync)
            return _knowledge.HighestLevels.GetValueOrDefault(speciesId);
    }

    /// <summary>
    /// Gets an immutable deep copy of tracker-owned knowledge for diagnostics.
    /// </summary>
    /// <returns>The current diagnostic knowledge snapshot.</returns>
    public TrackerKnowledgeSnapshot GetDiagnosticSnapshot()
    {
        lock (_sync)
        {
            Dictionary<string, IReadOnlyList<ObservedMoveSnapshot>> moves = _knowledge.Moves.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<ObservedMoveSnapshot>)[.. pair.Value]);
            Dictionary<string, IReadOnlyList<AbilitySnapshot>> abilities = _knowledge.Abilities.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<AbilitySnapshot>)[.. pair.Value]);
            Dictionary<string, int> highestLevels = new(_knowledge.HighestLevels);
            Dictionary<string, IReadOnlyDictionary<string, EnemyStatAnnotation>> annotations = _knowledge.Annotations.ToDictionary(pair => pair.Key, pair => (IReadOnlyDictionary<string, EnemyStatAnnotation>)new Dictionary<string, EnemyStatAnnotation>(pair.Value));
            return new TrackerKnowledgeSnapshot(_runId, moves, abilities, highestLevels, annotations, LastError);
        }
    }

    /// <summary>
    /// Gets one manual enemy-stat annotation.
    /// </summary>
    /// <param name="speciesId">The stable species and form identifier.</param>
    /// <param name="stat">The annotated stat.</param>
    /// <returns>The current annotation.</returns>
    public EnemyStatAnnotation GetAnnotation(string speciesId, EnemyStat stat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        lock (_sync)
        {
            if (!_knowledge.Annotations.TryGetValue(speciesId, out Dictionary<string, EnemyStatAnnotation>? annotations))
                return EnemyStatAnnotation.Empty;

            return annotations.GetValueOrDefault(stat.ToString(), EnemyStatAnnotation.Empty);
        }
    }

    /// <summary>
    /// Cycles one annotation forward or backward and persists the result.
    /// </summary>
    /// <param name="speciesId">The stable species and form identifier.</param>
    /// <param name="stat">The annotated stat.</param>
    /// <param name="forward">Whether to cycle forward.</param>
    public void CycleAnnotation(string speciesId, EnemyStat stat, bool forward)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        lock (_sync)
        {
            Dictionary<string, EnemyStatAnnotation> annotations = GetOrCreateAnnotations(speciesId);
            EnemyStatAnnotation current = annotations.GetValueOrDefault(stat.ToString(), EnemyStatAnnotation.Empty);
            annotations[stat.ToString()] = Cycle(current, forward);
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Records a set of observed moves and persists changes.
    /// </summary>
    /// <param name="speciesId">The stable species and form identifier.</param>
    /// <param name="observations">The legal move observations.</param>
    private void ObserveMoves(string speciesId, IEnumerable<ObservedMoveSnapshot> observations)
    {
        bool changed = false;
        lock (_sync)
        {
            if (_runId is null)
                return;

            List<ObservedMoveSnapshot> moves = GetOrCreateMoves(speciesId);
            foreach (ObservedMoveSnapshot observation in observations)
            {
                int index = moves.FindIndex(move => MatchesDiscovery(move, observation));
                if (index >= 0 && TrackerKnowledgeSnapshotComparer.AreEquivalent(moves[index], observation))
                    continue;

                if (index >= 0)
                {
                    moves[index] = observation;
                }
                else
                {
                    moves.Add(observation);
                }

                changed = true;
            }

            if (changed)
                Save();
        }

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Records one legally known ability and persists a new discovery.
    /// </summary>
    /// <param name="speciesId">The stable species and form identifier.</param>
    /// <param name="ability">The legally known ability.</param>
    private void ObserveAbility(string speciesId, AbilitySnapshot ability)
    {
        bool changed;
        lock (_sync)
        {
            if (_runId is null)
                return;

            changed = AddAbility(speciesId, ability);
            if (changed)
                Save();
        }

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Adds or refreshes one ability in mutable knowledge.
    /// </summary>
    /// <param name="speciesId">The stable species and form identifier.</param>
    /// <param name="ability">The ability observation.</param>
    /// <returns>True when persisted data changed.</returns>
    private bool AddAbility(string speciesId, AbilitySnapshot ability)
    {
        if (!_knowledge.Abilities.TryGetValue(speciesId, out List<AbilitySnapshot>? abilities))
        {
            abilities = [];
            _knowledge.Abilities.Add(speciesId, abilities);
        }

        int index = abilities.FindIndex(candidate => candidate.Id == ability.Id);
        if (index >= 0 && TrackerKnowledgeSnapshotComparer.AreEquivalent(abilities[index], ability))
            return false;

        if (index >= 0)
        {
            abilities[index] = ability;
        }
        else
        {
            abilities.Add(ability);
        }

        return true;
    }

    /// <summary>
    /// Gets or creates move storage for a species and form.
    /// </summary>
    /// <param name="speciesId">The stable species and form identifier.</param>
    /// <returns>The mutable move collection.</returns>
    private List<ObservedMoveSnapshot> GetOrCreateMoves(string speciesId)
    {
        if (!_knowledge.Moves.TryGetValue(speciesId, out List<ObservedMoveSnapshot>? moves))
        {
            moves = [];
            _knowledge.Moves.Add(speciesId, moves);
        }

        return moves;
    }

    /// <summary>
    /// Gets or creates annotation storage for a species and form.
    /// </summary>
    /// <param name="speciesId">The stable species and form identifier.</param>
    /// <returns>The mutable annotation map.</returns>
    private Dictionary<string, EnemyStatAnnotation> GetOrCreateAnnotations(string speciesId)
    {
        if (!_knowledge.Annotations.TryGetValue(speciesId, out Dictionary<string, EnemyStatAnnotation>? annotations))
        {
            annotations = [];
            _knowledge.Annotations.Add(speciesId, annotations);
        }

        return annotations;
    }

    /// <summary>
    /// Loads persisted knowledge without making persistence failure fatal.
    /// </summary>
    /// <param name="runId">The selected run identifier.</param>
    /// <returns>The loaded or empty knowledge.</returns>
    private PersistedRunKnowledge Load(string? runId)
    {
        LastError = null;
        if (runId is null)
            return new PersistedRunKnowledge();

        try
        {
            string path = GetRunPath(runId);
            if (!File.Exists(path))
                return new PersistedRunKnowledge();

            return JsonSerializer.Deserialize<PersistedRunKnowledge>(File.ReadAllText(path), TrackerJson.Options) ?? new PersistedRunKnowledge();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            LastError = exception.Message;
            return new PersistedRunKnowledge();
        }
    }

    /// <summary>
    /// Atomically saves active run knowledge without making persistence failure fatal.
    /// </summary>
    private void Save()
    {
        if (_runId is null)
            return;

        try
        {
            string path = GetRunPath(_runId);
            TrackerAtomicFileWriter.WriteAllText(path, JsonSerializer.Serialize(_knowledge, TrackerJson.Options));
            LastError = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LastError = exception.Message;
        }
    }

    /// <summary>
    /// Gets the safe persistence path for one run identifier.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    /// <returns>The run knowledge path.</returns>
    private string GetRunPath(string runId)
    {
        string fileName = string.Concat(runId.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
        return Path.Combine(_options.RootDirectory, TrackerStorageNames.RunsDirectory, $"{fileName}{TrackerStorageNames.JsonExtension}");
    }

    /// <summary>
    /// Determines whether two observations identify the same retained discovery.
    /// </summary>
    /// <param name="left">The retained observation.</param>
    /// <param name="right">The incoming observation.</param>
    /// <returns>True when both observations identify the same discovery.</returns>
    private static bool MatchesDiscovery(ObservedMoveSnapshot left, ObservedMoveSnapshot right)
        => left.Id == right.Id && left.LearnedLevel == right.LearnedLevel && left.LearnOrder == right.LearnOrder && left.Origin == right.Origin;

    /// <summary>
    /// Converts a persisted protocol observation into the core discovery rule model.
    /// </summary>
    /// <param name="observation">The persisted observation.</param>
    /// <returns>The core discovery.</returns>
    private static DiscoveredMove ToCoreDiscovery(ObservedMoveSnapshot observation)
    {
        MoveLearnSource source = observation.Source switch
        {
            "level_up" => MoveLearnSource.LevelUp,
            _ => MoveLearnSource.Unknown
        };

        MoveDiscoveryOrigin origin = observation.Origin switch
        {
            "enemy_use" => MoveDiscoveryOrigin.EnemyUse,
            "player_level_up" => MoveDiscoveryOrigin.PlayerLevelUp,
            _ => MoveDiscoveryOrigin.PlayerInitial
        };

        return new DiscoveredMove(observation.Id, observation.LearnedLevel, observation.LearnOrder, source, origin);
    }

    /// <summary>
    /// Selects display metadata for one core-selected discovery.
    /// </summary>
    /// <param name="observations">All retained protocol observations.</param>
    /// <param name="discovery">The selected core discovery.</param>
    /// <returns>The preferred display observation.</returns>
    private static ObservedMoveSnapshot SelectObservation(IEnumerable<ObservedMoveSnapshot> observations, DiscoveredMove discovery)
    {
        return observations.Where(move => move.Id == discovery.MoveId && move.LearnedLevel == discovery.LearnedLevel && move.LearnOrder == discovery.LearnOrder)
            .OrderByDescending(move => move.PpAfterUse.HasValue)
            .First();
    }

    /// <summary>
    /// Cycles an annotation in the requested direction.
    /// </summary>
    /// <param name="annotation">The current annotation.</param>
    /// <param name="forward">Whether to cycle forward.</param>
    /// <returns>The next annotation.</returns>
    private static EnemyStatAnnotation Cycle(EnemyStatAnnotation annotation, bool forward) => (annotation, forward) switch
    {
        (EnemyStatAnnotation.Empty, true) => EnemyStatAnnotation.Plus,
        (EnemyStatAnnotation.Plus, true) => EnemyStatAnnotation.Minus,
        (EnemyStatAnnotation.Minus, true) => EnemyStatAnnotation.Empty,
        (EnemyStatAnnotation.Empty, false) => EnemyStatAnnotation.Minus,
        (EnemyStatAnnotation.Minus, false) => EnemyStatAnnotation.Plus,
        (EnemyStatAnnotation.Plus, false) => EnemyStatAnnotation.Empty,
        _ => EnemyStatAnnotation.Empty
    };
}
