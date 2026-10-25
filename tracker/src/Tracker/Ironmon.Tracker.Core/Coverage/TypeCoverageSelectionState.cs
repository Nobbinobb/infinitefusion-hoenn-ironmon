namespace Ironmon.Tracker.Core.Coverage;

/// <summary>
/// Retains manual attacking-type choices while following the current player Pokemon.
/// </summary>
public sealed class TypeCoverageSelectionState
{
    private IReadOnlyList<string> _currentMoveTypes = [];
    private IReadOnlyList<string> _selectedTypes = [];

    /// <summary>
    /// Initializes an empty type-coverage selection.
    /// </summary>
    public TypeCoverageSelectionState()
    {
    }

    /// <summary>
    /// Gets the represented player Pokemon identifier.
    /// </summary>
    public string? PokemonId { get; private set; }

    /// <summary>
    /// Gets the current damaging-move types in chart order.
    /// </summary>
    public IReadOnlyList<string> CurrentMoveTypes => _currentMoveTypes;

    /// <summary>
    /// Gets the selected attacking types in chart order.
    /// </summary>
    public IReadOnlyList<string> SelectedTypes => _selectedTypes;

    /// <summary>
    /// Gets whether the selection has been changed manually since its last reset.
    /// </summary>
    public bool IsManual { get; private set; }

    /// <summary>
    /// Updates the current Pokemon and damaging-move types.
    /// </summary>
    /// <param name="pokemonId">The current player Pokemon identifier.</param>
    /// <param name="currentMoveTypes">Its current damaging-move type identifiers.</param>
    public void UpdateCurrentPokemon(string? pokemonId, IEnumerable<string> currentMoveTypes)
    {
        ArgumentNullException.ThrowIfNull(currentMoveTypes);
        IReadOnlyList<string> normalized = NormalizeTypes(currentMoveTypes);
        bool pokemonChanged = !string.Equals(PokemonId, pokemonId, StringComparison.Ordinal);
        PokemonId = pokemonId;
        _currentMoveTypes = normalized;
        if (pokemonChanged || !IsManual)
        {
            _selectedTypes = normalized;
            IsManual = false;
        }
    }

    /// <summary>
    /// Selects or unselects one attacking type manually.
    /// </summary>
    /// <param name="type">The attacking type identifier.</param>
    public void Toggle(string type)
    {
        string normalized = NormalizeType(type);
        HashSet<string> selected = new(_selectedTypes, StringComparer.Ordinal);
        if (!selected.Add(normalized))
            selected.Remove(normalized);

        _selectedTypes = [.. PokemonTypeCatalog.StandardTypes.Where(selected.Contains)];
        IsManual = true;
    }

    /// <summary>
    /// Restores the selection to the current damaging-move types.
    /// </summary>
    public void ResetToCurrentMoves()
    {
        _selectedTypes = _currentMoveTypes;
        IsManual = false;
    }

    /// <summary>
    /// Normalizes a sequence of attacking types into stable chart order.
    /// </summary>
    /// <param name="types">The type identifiers.</param>
    /// <returns>The normalized unique identifiers.</returns>
    private static IReadOnlyList<string> NormalizeTypes(IEnumerable<string> types)
    {
        HashSet<string> normalized = new(types.Select(NormalizeType), StringComparer.Ordinal);
        return [.. PokemonTypeCatalog.StandardTypes.Where(normalized.Contains)];
    }

    /// <summary>
    /// Normalizes and validates one attacking type identifier.
    /// </summary>
    /// <param name="type">The type identifier.</param>
    /// <returns>The normalized identifier.</returns>
    private static string NormalizeType(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        string normalized = type.ToUpperInvariant();
        PokemonTypeCatalog.GetOrder(normalized);
        return normalized;
    }
}
