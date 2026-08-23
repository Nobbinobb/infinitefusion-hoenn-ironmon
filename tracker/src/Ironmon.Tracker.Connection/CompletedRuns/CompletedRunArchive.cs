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
        CompletedRunRecipeValidator.Validate(recipe);
        bool selectionRequested;
        lock (_sync)
        {
            selectionRequested = _recipes.All(candidate => candidate.RunId != recipe.RunId);
            string path = GetRecipePath(recipe.RunId);
            try
            {
                TrackerAtomicFileWriter.WriteAllText(path, JsonSerializer.Serialize(recipe, TrackerJson.Options));
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
                    if (!CompletedRunRecipeValidator.HasRequiredShape(document.RootElement))
                    {
                        LastError = $"Ignored incompatible completed-run recipe '{path}'.";
                        continue;
                    }

                    CompletedRunRecipePayload? recipe = JsonSerializer.Deserialize<CompletedRunRecipePayload>(json, TrackerJson.Options);
                    if (recipe is null)
                        continue;

                    CompletedRunRecipeValidator.Validate(recipe);
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
