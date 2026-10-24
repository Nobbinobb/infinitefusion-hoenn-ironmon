using Ironmon.Tracker.Collection;
using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Loads recorded runner stages locally and optionally materializes missing artwork without run reconstruction.
/// </summary>
public partial class ArchivePokemonHistory : IDisposable
{
    private const string _keyFormat = "{0}|{1}|{2}|{3}";
    private const string _transitionKeyPrefix = "Archive.PokemonHistory.Transition.";
    private static readonly SemaphoreSlim _spriteRequests = new(1, 1);
    private IReadOnlyList<RunPokemonHistoryEntry> _entries = [];
    private CancellationTokenSource? _request;
    private string? _key;
    private bool _loading;
    private bool _failed;
    private bool _disposed;

    /// <summary>
    /// Gets or initializes the serialized collection reader.
    /// </summary>
    [Inject]
    private GameplayCollectionStore Collection { get; set; } = null!;

    /// <summary>
    /// Gets or initializes the bounded artwork request client.
    /// </summary>
    [Inject]
    private TrackerRequestClient Connection { get; set; } = null!;

    /// <summary>
    /// Gets or sets the exact archived run whose recorded history is shown.
    /// </summary>
    [Parameter, EditorRequired]
    public CompletedRunRecipePayload Recipe { get; set; } = null!;

    /// <summary>
    /// Gets or sets the installation containing recorded sprite paths, including while disconnected.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets or sets whether to render only the final runner as a noninteractive run-list identity.
    /// </summary>
    [Parameter]
    public bool Compact { get; set; }

    /// <summary>
    /// Gets or sets the action opening normal archived species information.
    /// </summary>
    [Parameter]
    public EventCallback<string> PokemonSelected { get; set; }

    /// <summary>
    /// Subscribes to late ownership batches for the selected completed attempt.
    /// </summary>
    protected override void OnInitialized()
        => Collection.OwnedHistoryChanged += HandleHistoryChanged;

    /// <summary>
    /// Loads a newly selected run or newly associated exact collection attempts.
    /// </summary>
    /// <returns>The collection read and render lifecycle task.</returns>
    protected override async Task OnParametersSetAsync()
    {
        string key = string.Format(System.Globalization.CultureInfo.InvariantCulture, _keyFormat, Recipe.RunId, string.Join(',', Recipe.CollectionAttemptIds), GameRoot, Compact);
        if (_key == key)
            return;

        _key = key;
        _entries = [];
        await ReloadAsync();
    }

    /// <summary>
    /// Reads the current archived runner history and discards stale or disposed completions.
    /// </summary>
    /// <returns>The bounded collection-reader operation.</returns>
    private async Task ReloadAsync()
    {
        if (_disposed)
            return;

        _request?.Cancel();
        _request?.Dispose();
        _request = new();
        CancellationToken token = _request.Token;
        _loading = true;
        _failed = false;
        try
        {
            IReadOnlyList<RunPokemonHistoryEntry> entries = await Collection.GetRunPokemonHistoryAsync(Recipe.CollectionAttemptIds, token);
            if (!token.IsCancellationRequested && !_disposed)
            {
                IEnumerable<RunPokemonHistoryEntry> visible = Compact ? entries.Take(1) : entries;
                _entries = [.. visible.Select(entry => entry with { SpritePath = ArchivedPokemonSpriteLoader.Resolve(GameRoot, entry.SpeciesId, entry.SpritePath) })];
                _loading = false;
                StateHasChanged();
                await ResolveSpritesAsync(token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception) when (!token.IsCancellationRequested && !_disposed)
        {
            _failed = true;
        }
        finally
        {
            if (!token.IsCancellationRequested && !_disposed)
                _loading = false;
        }
    }

    /// <summary>
    /// Serializes legacy artwork batches across visible runs while leaving recorded identities immediately visible.
    /// </summary>
    /// <param name="token">Cancels work for an older run selection or disposed component.</param>
    /// <returns>The bounded artwork refresh task; connection failures leave the history usable.</returns>
    private async Task ResolveSpritesAsync(CancellationToken token)
    {
        if (GameRoot is null)
            return;

        bool acquired = false;
        try
        {
            await _spriteRequests.WaitAsync(token);
            acquired = true;
            token.ThrowIfCancellationRequested();
            _entries = [.. _entries.Select(entry => entry with { SpritePath = ArchivedPokemonSpriteLoader.Resolve(GameRoot, entry.SpeciesId, entry.SpritePath) })];
            StateHasChanged();
            string[] missing = [.. _entries.Where(entry => entry.SpritePath is null).Select(entry => entry.SpeciesId).Distinct(StringComparer.Ordinal)];
            foreach (string[] batch in missing.Chunk(2))
            {
                PokemonSpritesResponsePayload response = await Connection.GetPokemonSpritesAsync(batch, token);
                if (token.IsCancellationRequested || _disposed)
                    return;

                _entries = [.. _entries.Select(entry => entry.SpritePath is null && response.Sprites.TryGetValue(entry.SpeciesId, out string? sprite)
                    ? entry with { SpritePath = ArchivedPokemonSpriteLoader.Resolve(GameRoot, entry.SpeciesId, sprite) } : entry)];
                StateHasChanged();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception) when (!token.IsCancellationRequested && !_disposed)
        {
        }
        finally
        {
            if (acquired)
                _spriteRequests.Release();
        }
    }

    /// <summary>
    /// Refreshes only when a late durable batch belongs to the selected run.
    /// </summary>
    /// <param name="attempts">Exact attempts whose ownership evidence was received.</param>
    private void HandleHistoryChanged(IReadOnlyList<string> attempts)
    {
        if (_disposed || !attempts.Intersect(Recipe.CollectionAttemptIds, StringComparer.OrdinalIgnoreCase).Any())
            return;

        _ = InvokeAsync(async () =>
        {
            await ReloadAsync();
            if (!_disposed)
                StateHasChanged();
        });
    }

    /// <summary>
    /// Labels a recorded transition with the shared tracker localizer.
    /// </summary>
    /// <param name="transition">The retained transition classification.</param>
    /// <returns>The localized transition label.</returns>
    private string TransitionLabel(RunPokemonTransition transition)
        => Text[string.Concat(_transitionKeyPrefix, transition)].Value;

    /// <summary>
    /// Opens a retained stage using the parent's normal archived species navigation.
    /// </summary>
    /// <param name="speciesId">The retained species and form identity.</param>
    /// <returns>The parent navigation event task.</returns>
    private Task OpenPokemonAsync(string speciesId)
        => PokemonSelected.InvokeAsync(speciesId);

    /// <summary>
    /// Unsubscribes collection updates and cancels stale reads when the selected run is removed.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        Collection.OwnedHistoryChanged -= HandleHistoryChanged;
        _request?.Cancel();
        _request?.Dispose();
        GC.SuppressFinalize(this);
    }
}
