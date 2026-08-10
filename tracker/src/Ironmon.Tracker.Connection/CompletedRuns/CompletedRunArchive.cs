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
    /// Adds or replaces one completed-run recipe and persists it atomically.
    /// </summary>
    /// <param name="recipe">The compact deterministic recipe.</param>
    public void Store(CompletedRunRecipePayload recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        Validate(recipe);
        lock (_sync)
        {
            string path = GetRecipePath(recipe.RunId);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temporaryPath = $"{path}{TrackerStorageNames.TemporaryExtension}";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(recipe, TrackerJson.Options));
                File.Move(temporaryPath, path, true);
                _recipes.RemoveAll(candidate => candidate.RunId == recipe.RunId);
                _recipes.Insert(0, recipe);
                LastError = null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LastError = exception.Message;
                return;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
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

        if (recipe.MoveAccessMetrics is not null)
        {
            if (recipe.MoveAccessGenerator is null)
                throw new ArgumentException("Move-access metrics require move-access generator metadata.", nameof(recipe));

            ArgumentOutOfRangeException.ThrowIfNotEqual(recipe.MoveAccessMetrics.SchemaVersion, MoveAccessMetricIdentifiers.SchemaVersion);
        }

        if (recipe.EvolutionMetrics is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNotEqual(recipe.EvolutionMetrics.SchemaVersion, EvolutionMetricIdentifiers.SchemaVersion);
            if (recipe.EvolutionGenerator is null)
                throw new ArgumentException("Evolution metrics require evolution generator metadata.", nameof(recipe));
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
