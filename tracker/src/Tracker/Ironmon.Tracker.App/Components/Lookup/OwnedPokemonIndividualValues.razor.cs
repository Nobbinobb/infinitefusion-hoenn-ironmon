using Ironmon.Tracker.Collection;
using Microsoft.AspNetCore.Components;
using System.Text.Json;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Selects actual owned Pokemon from the chosen completed run without exposing encounter IVs.
/// </summary>
public partial class OwnedPokemonIndividualValues
{
    private const string _recordLabelKey = "Stats.IVs.RecordLabel";
    private const string _runnerKey = "Stats.IVs.Runner";
    private const string _ownedKey = "Stats.IVs.Owned";
    private const string _unknownKey = "Stats.IVs.Unknown";
    private const string _requestKeyFormat = "{0}|{1}|{2}|{3}";
    private IReadOnlyList<OwnedPokemonIvRecord> _records = [];
    private CancellationTokenSource? _loading;
    private string? _requestKey;
    private int _selectedIndex;
    private bool _disposed;

    /// <summary>
    /// Gets or sets the serialized collection store used to read retained ownership evidence.
    /// </summary>
    [Inject]
    private GameplayCollectionStore Collection { get; set; } = null!;

    /// <summary>
    /// Gets or sets the completed run, or null for species-only lookup.
    /// </summary>
    [Parameter]
    public CompletedRunRecipePayload? Recipe { get; set; }

    /// <summary>
    /// Gets or sets the legacy numeric species identity, or null when not provided by the game.
    /// </summary>
    [Parameter]
    public int? NumericSpeciesId { get; set; }

    /// <summary>
    /// Gets or sets the Stats page's original and generated base-stat information.
    /// </summary>
    [Parameter]
    public PokemonLookupStatsSnapshot Stats { get; set; } = new();

    /// <summary>
    /// Gets or sets whether fusion dominance labels should be shown in base-stat views.
    /// </summary>
    [Parameter]
    public bool Fusion { get; set; }

    /// <summary>
    /// Gets or sets the selected species and form identifier.
    /// </summary>
    [Parameter]
    public string SpeciesId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the species name used when the recorded nickname is unavailable.
    /// </summary>
    [Parameter]
    public string SpeciesName { get; set; } = string.Empty;

    /// <summary>
    /// Gets the selected individual's recorded configuration, or null when none is available.
    /// </summary>
    private OwnedPokemonIvRecord? Selected => _selectedIndex >= 0 && _selectedIndex < _records.Count ? _records[_selectedIndex] : null;

    /// <summary>
    /// Loads ownership records when the selected run or species changes, discarding stale completions.
    /// </summary>
    /// <returns>A task completing after the matching collection read or its cancellation.</returns>
    protected override async Task OnParametersSetAsync()
    {
        IReadOnlyList<string> attempts = Recipe is null ? [] : [.. Recipe.CollectionAttemptIds.Distinct(StringComparer.OrdinalIgnoreCase)];
        string requestKey = string.Format(System.Globalization.CultureInfo.InvariantCulture, _requestKeyFormat, Recipe?.RunId, SpeciesId, NumericSpeciesId, string.Join(',', attempts));
        if (_requestKey == requestKey || _disposed)
            return;

        _requestKey = requestKey;
        _loading?.Cancel();
        _loading?.Dispose();
        _loading = new();
        CancellationToken token = _loading.Token;
        _records = [];
        _selectedIndex = 0;
        if (attempts.Count == 0)
            return;

        string speciesId = SpeciesId;
        int? numericSpeciesId = NumericSpeciesId;
        try
        {
            IReadOnlyList<JsonElement> payloads = await Collection.GetOwnedConfigurationsAsync(attempts, token);
            if (token.IsCancellationRequested || _disposed)
                return;

            _records = [.. payloads.Select(payload => OwnedPokemonIvRecord.Read(payload, speciesId, numericSpeciesId)).OfType<OwnedPokemonIvRecord>().Where(record => PokemonIndividualValues.HasKnownValues(record.Ivs) || PokemonIndividualValues.HasKnownValues(record.EffectiveIvs)).OrderByDescending(record => record.Runner)];
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception) when (!token.IsCancellationRequested && !_disposed)
        {
            _records = [];
        }
    }

    /// <summary>
    /// Labels same-species individuals using nickname, role, level and a stable display ordinal.
    /// </summary>
    /// <param name="record">The actual recorded individual.</param>
    /// <param name="index">The zero-based index within this species selection.</param>
    /// <returns>The localized selector label.</returns>
    private string RecordLabel(OwnedPokemonIvRecord record, int index)
        => Text[_recordLabelKey, string.IsNullOrWhiteSpace(record.Nickname) ? SpeciesName : record.Nickname, Text[record.Runner ? _runnerKey : _ownedKey].Value, record.Level?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? Text[_unknownKey].Value, index + 1].Value;

    /// <summary>
    /// Cancels pending reads when the lookup is closed or replaced.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _loading?.Cancel();
        _loading?.Dispose();
        GC.SuppressFinalize(this);
    }
}
