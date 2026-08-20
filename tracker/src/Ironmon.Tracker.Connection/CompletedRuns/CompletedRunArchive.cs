using System.Text.Json;

namespace Ironmon.Tracker.Connection.CompletedRuns;

/// <summary>
/// Persists compact completed-run recipes without storing reconstructed Pokemon data.
/// </summary>
public sealed class CompletedRunArchive
{
    private readonly TrackerKnowledgeOptions _options;
    private readonly Lock _sync = new();
    private readonly List<CompletedRunRecipePayload> _recipes;
    private string? _requestedRunId;

    /// <summary>
    /// Initializes and loads the completed-run recipe archive.
    /// </summary>
    /// <param name="options">The tracker-owned persistence location.</param>
    /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
    public CompletedRunArchive(TrackerKnowledgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _recipes = Load();
    }

    /// <summary>
    /// Occurs after the available completed-run recipes change.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Occurs when a previously unknown completed run should be selected.
    /// </summary>
    public event EventHandler? SelectionRequested;

    /// <summary>
    /// Gets the available completed-run recipes with the newest files first.
    /// </summary>
    public IReadOnlyList<CompletedRunRecipePayload> Recipes
    {
        get
        {
            lock (_sync)
                return _recipes.ToArray();
        }
    }

    /// <summary>
    /// Gets the most recent non-fatal persistence error.
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Gets the completed run most recently requested for foreground selection.
    /// </summary>
    public string? RequestedRunId
    {
        get
        {
            lock (_sync)
                return _requestedRunId;
        }
    }

    /// <summary>
    /// Adds or replaces one completed-run recipe and persists it atomically.
    /// </summary>
    /// <param name="recipe">The compact deterministic recipe.</param>
    public void Store(CompletedRunRecipePayload recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        Validate(recipe);
        bool selectionRequested;
        lock (_sync)
        {
            selectionRequested = _recipes.All(candidate => candidate.RunId != recipe.RunId);
            string path = GetRecipePath(recipe.RunId);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temporaryPath = $"{path}{TrackerStorageNames.TemporaryExtension}";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(recipe, TrackerJson.Options));
                File.Move(temporaryPath, path, true);
                _recipes.RemoveAll(candidate => candidate.RunId == recipe.RunId);
                _recipes.Insert(0, recipe);
                if (selectionRequested)
                    _requestedRunId = recipe.RunId;

                LastError = null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LastError = exception.Message;
                return;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        if (selectionRequested)
            SelectionRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Loads valid completed-run recipes without making an individual file failure fatal.
    /// </summary>
    /// <returns>The loaded recipes with newest files first.</returns>
    private List<CompletedRunRecipePayload> Load()
    {
        string root = Path.Combine(_options.RootDirectory, TrackerStorageNames.RunsDirectory);
        if (!Directory.Exists(root))
            return [];

        List<CompletedRunRecipePayload> recipes = [];
        try
        {
            IEnumerable<string> paths = Directory.EnumerateFiles(root, TrackerStorageNames.RecipeFile, SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc);
            foreach (string path in paths)
            {
                try
                {
                    string json = File.ReadAllText(path);
                    using JsonDocument document = JsonDocument.Parse(json);
                    if (!HasNestedRecipeShape(document.RootElement))
                    {
                        LastError = $"Ignored incompatible completed-run recipe '{path}'.";
                        continue;
                    }

                    CompletedRunRecipePayload? recipe = JsonSerializer.Deserialize<CompletedRunRecipePayload>(json, TrackerJson.Options);
                    if (recipe is null)
                        continue;

                    Validate(recipe);
                    if (recipes.All(candidate => candidate.RunId != recipe.RunId))
                        recipes.Add(recipe);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
                {
                    LastError = exception.Message;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LastError = exception.Message;
        }

        return recipes;
    }

    /// <summary>
    /// Determines whether a JSON document has the required nested recipe contract.
    /// </summary>
    /// <param name="root">The parsed recipe root.</param>
    /// <returns><see langword="true"/> when required nested manifests are present as objects.</returns>
    private static bool HasNestedRecipeShape(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object
            && HasObjectProperty(root, "configuration")
            && HasObjectProperty(root, "species_generator")
            && HasObjectProperty(root, "ability_generator")
            && HasObjectProperty(root, "player_fusion_generator");
    }

    /// <summary>
    /// Determines whether a JSON object contains one named object property.
    /// </summary>
    /// <param name="root">The containing JSON object.</param>
    /// <param name="propertyName">The serialized property name.</param>
    /// <returns><see langword="true"/> when the property exists and contains an object.</returns>
    private static bool HasObjectProperty(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.Object;

    /// <summary>
    /// Validates the required identity and generator metadata of one recipe.
    /// </summary>
    /// <param name="recipe">The recipe to validate.</param>
    private static void Validate(CompletedRunRecipePayload recipe)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(recipe.SchemaVersion, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.Result);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.GameVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.IronmonVersion);
        ArgumentNullException.ThrowIfNull(recipe.Configuration);
        ArgumentOutOfRangeException.ThrowIfLessThan(recipe.Configuration.SchemaVersion, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.Configuration.WildPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.Configuration.TrainerPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.Configuration.UnfusionSetting);

        ArgumentNullException.ThrowIfNull(recipe.SpeciesGenerator);
        ArgumentOutOfRangeException.ThrowIfLessThan(recipe.SpeciesGenerator.Version, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.SpeciesGenerator.PoolFingerprint);

        ArgumentNullException.ThrowIfNull(recipe.AbilityGenerator);
        ArgumentOutOfRangeException.ThrowIfLessThan(recipe.AbilityGenerator.Version, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(recipe.AbilityGenerator.PoolSize, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.AbilityGenerator.PoolFingerprint);

        if (recipe.BaseStatGenerator is not null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(recipe.BaseStatGenerator.Version, 1);
            ArgumentException.ThrowIfNullOrWhiteSpace(recipe.BaseStatGenerator.SourceFingerprint);
        }

        if (recipe.EvolutionGenerator is not null)
            ValidateEvolutionGenerator(recipe.EvolutionGenerator);

        if (recipe.MoveAccessGenerator is not null)
            ValidateMoveAccessGenerator(recipe.MoveAccessGenerator);

        ArgumentNullException.ThrowIfNull(recipe.PlayerFusionGenerator);
        ArgumentOutOfRangeException.ThrowIfLessThan(recipe.PlayerFusionGenerator.Version, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(recipe.PlayerFusionGenerator.PoolSize, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.PlayerFusionGenerator.PoolFingerprint);

        if (recipe.ItemGenerator is not null)
            ValidateItemGenerator(recipe.ItemGenerator);

        if (recipe.Statistics is not null)
            ValidateStatistics(recipe.Statistics);

    }

    /// <summary>
    /// Validates deterministic item-slot generator metadata.
    /// </summary>
    /// <param name="generator">The item generator metadata to validate.</param>
    private static void ValidateItemGenerator(ItemGeneratorRecipePayload generator)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.Version, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.RulesVersion, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.GroundPoolSize, 1);
        if (generator.RulesVersion >= 3)
            ArgumentOutOfRangeException.ThrowIfLessThan(generator.GroundTotalWeight, 1);

        ArgumentException.ThrowIfNullOrWhiteSpace(generator.GroundPoolFingerprint);
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.TmPoolSize, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.TmPoolFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.ResultBanFingerprint);
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.ShopPolicyVersion, 1);
        if (generator.ResultBans.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Item result bans must have identifiers.", nameof(generator));

        if (generator.ResultBans.Distinct(StringComparer.Ordinal).Count() != generator.ResultBans.Count)
            throw new ArgumentException("Item result bans must be unique.", nameof(generator));
    }

    /// <summary>
    /// Validates one authoritative run-statistics payload.
    /// </summary>
    /// <param name="statistics">The statistics to validate.</param>
    private static void ValidateStatistics(RunStatisticsPayload statistics)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(statistics.SchemaVersion, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(statistics.AttemptNumber, 1);
        if (statistics.SaveSlot is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(statistics.SaveSlot);

        ArgumentException.ThrowIfNullOrWhiteSpace(statistics.Result);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.ActiveSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.AttemptsStarted);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.AttemptsLost);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.AttemptsWon);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.AttemptsAbandoned);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.BattlesCompleted);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.HighestPlayerLevel);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.BadgesEarned);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.TotalItemHealing);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.WastedItemHealing);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.ItemsUsed);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.TrainerSpeciesDistinct);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.TrainerDefeatedCount);
        if (statistics.TrainerDefeatedBstAverage is double average)
            ArgumentOutOfRangeException.ThrowIfNegative(average);

        if (statistics.TrainerDefeatedBstMinimum is int minimum)
            ArgumentOutOfRangeException.ThrowIfNegative(minimum);

        if (statistics.TrainerDefeatedBstMaximum is int maximum)
            ArgumentOutOfRangeException.ThrowIfNegative(maximum);

        foreach (IReadOnlyDictionary<string, int> counts in statistics.ItemsBySource.Values)
        {
            foreach (int count in counts.Values)
                ArgumentOutOfRangeException.ThrowIfNegative(count);
        }

        foreach (int count in statistics.TrainerSpeciesCounts.Values)
            ArgumentOutOfRangeException.ThrowIfNegative(count);

        foreach (KeyValuePair<string, string> species in statistics.TrainerSpeciesNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(species.Key);
            ArgumentException.ThrowIfNullOrWhiteSpace(species.Value);
        }
    }

    /// <summary>
    /// Validates one complete evolution-generator manifest.
    /// </summary>
    /// <param name="generator">The manifest to validate.</param>
    private static void ValidateEvolutionGenerator(EvolutionGeneratorRecipePayload generator)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.Version, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.RulesVersion, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.SourceFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.TaxonomyFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.MethodFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.TargetFingerprint);
        ArgumentNullException.ThrowIfNull(generator.BaseStatGenerator);
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.BaseStatGenerator.Version, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.BaseStatGenerator.SourceFingerprint);
        ArgumentNullException.ThrowIfNull(generator.Fusion);
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.Fusion.Version, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.Fusion.RulesVersion, 1);
        ArgumentNullException.ThrowIfNull(generator.Fusion.TargetPool);
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.Fusion.TargetPool.Version, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.Fusion.TargetPool.Size, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.Fusion.TargetPool.Fingerprint);
    }

    /// <summary>
    /// Validates one complete move-access-generator manifest.
    /// </summary>
    /// <param name="generator">The manifest to validate.</param>
    private static void ValidateMoveAccessGenerator(MoveAccessGeneratorRecipePayload generator)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.Version, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.PoolFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.ContextualRestrictionFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.LevelUpSourceFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.EggSourceFingerprint);
        ValidateMachineSource(generator.Tm);
        ValidateMachineSource(generator.Tr);
        ValidateTutorSource(generator.Tutor);
        ValidateTutorSource(generator.FusionTutor);
    }

    /// <summary>
    /// Validates one machine source manifest.
    /// </summary>
    /// <param name="source">The manifest to validate.</param>
    private static void ValidateMachineSource(MachineSourceRecipePayload source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.RosterFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.SourceFingerprint);
    }

    /// <summary>
    /// Validates one tutor source manifest.
    /// </summary>
    /// <param name="source">The manifest to validate.</param>
    private static void ValidateTutorSource(TutorSourceRecipePayload source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.CatalogFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.SourceFingerprint);
    }

    /// <summary>
    /// Gets the tracker-owned path for one completed-run recipe.
    /// </summary>
    /// <param name="runId">The stable run identifier.</param>
    /// <returns>The recipe file path.</returns>
    private string GetRecipePath(string runId)
    {
        string directory = string.Concat(runId.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
        return Path.Combine(_options.RootDirectory, TrackerStorageNames.RunsDirectory, directory, TrackerStorageNames.RecipeFile);
    }
}
