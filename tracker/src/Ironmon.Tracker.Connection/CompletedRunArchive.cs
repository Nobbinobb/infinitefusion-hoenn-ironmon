using System.Text.Json;
using Ironmon.Tracker.Protocol;

namespace Ironmon.Tracker.Connection;

/// <summary>
/// Persists compact completed-run recipes without storing reconstructed Pokemon data.
/// </summary>
public sealed class CompletedRunArchive
{
    private readonly TrackerKnowledgeOptions _options;
    private readonly object _sync = new();
    private List<CompletedRunRecipePayload> _recipes;

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
                string temporaryPath = $"{path}.tmp";
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
        string root = Path.Combine(_options.RootDirectory, "runs");
        if (!Directory.Exists(root))
            return [];

        List<CompletedRunRecipePayload> recipes = [];
        try
        {
            IEnumerable<string> paths = Directory.EnumerateFiles(root, "recipe.json", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc);
            foreach (string path in paths)
            {
                try
                {
                    CompletedRunRecipePayload? recipe = JsonSerializer.Deserialize<CompletedRunRecipePayload>(File.ReadAllText(path), TrackerJson.Options);
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
    /// Validates the required identity and generator metadata of one recipe.
    /// </summary>
    /// <param name="recipe">The recipe to validate.</param>
    private static void Validate(CompletedRunRecipePayload recipe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.Result);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.GameVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.IronmonVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.SpeciesPoolFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.AbilityPoolFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.FusionPoolFingerprint);
        ArgumentOutOfRangeException.ThrowIfLessThan(recipe.SpeciesGeneratorVersion, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(recipe.AbilityGeneratorVersion, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(recipe.PlayerFusionGeneratorVersion, 1);
    }

    /// <summary>
    /// Gets the tracker-owned path for one completed-run recipe.
    /// </summary>
    /// <param name="runId">The stable run identifier.</param>
    /// <returns>The recipe file path.</returns>
    private string GetRecipePath(string runId)
    {
        string directory = string.Concat(runId.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
        return Path.Combine(_options.RootDirectory, "runs", directory, "recipe.json");
    }
}
