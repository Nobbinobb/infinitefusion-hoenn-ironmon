using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Ironmon.Tracker.App.Components.Debug;

/// <summary>
/// Renders a searchable picker for one development catalog.
/// </summary>
public partial class DevelopmentCatalogPicker
{
    private const int _maximumVisibleOptions = 8;
    private string _query = string.Empty;
    private string? _observedSelectedId;
    private bool _open;
    private bool _openUp;
    private ElementReference _pickerElement;

    /// <summary>
    /// Gets or initializes JavaScript interoperability for viewport measurement.
    /// </summary>
    [Inject]
    private IJSRuntime JavaScript { get; set; } = null!;

    /// <summary>
    /// Gets or sets the complete selectable catalog.
    /// </summary>
    [Parameter]
    public IReadOnlyList<DebugDevelopmentCatalogEntry> Options { get; set; } = [];

    /// <summary>
    /// Gets or sets the selected stable identifier.
    /// </summary>
    [Parameter]
    public string? SelectedId { get; set; }

    /// <summary>
    /// Gets or sets the callback raised when the committed selection changes.
    /// </summary>
    [Parameter]
    public EventCallback<string?> SelectedIdChanged { get; set; }

    /// <summary>
    /// Gets or sets the search-input placeholder.
    /// </summary>
    [Parameter]
    public string Placeholder { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the accessible label shared by the search input and results.
    /// </summary>
    [Parameter]
    public string AriaLabel { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the message shown when no catalog entries match.
    /// </summary>
    [Parameter]
    public string EmptyText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the picker is disabled.
    /// </summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>
    /// Gets or sets whether clearing the search input also clears the committed selection.
    /// </summary>
    [Parameter]
    public bool ClearSelectionOnInput { get; set; } = true;

    /// <summary>
    /// Synchronizes an externally changed selection with the visible search text.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (SelectedId == _observedSelectedId)
            return;

        _observedSelectedId = SelectedId;
        _query = Options.FirstOrDefault(option => option.Id == SelectedId)?.Name ?? string.Empty;
        _open = false;
        _openUp = false;
    }

    /// <summary>
    /// Gets the picker classes for the measured opening direction.
    /// </summary>
    private string PickerCssClass => _openUp ? "development-picker opens-up" : "development-picker";

    /// <summary>
    /// Gets the bounded catalog entries that best match the current search text.
    /// </summary>
    private IReadOnlyList<DebugDevelopmentCatalogEntry> FilteredOptions
    {
        get
        {
            string query = _query.Trim();
            IEnumerable<DebugDevelopmentCatalogEntry> matches = Options;
            if (query.Length > 0)
            {
                matches = matches
                    .Where(option => option.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                        || option.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(option => option.Name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
                    .ThenBy(option => option.Name, StringComparer.CurrentCultureIgnoreCase);
            }

            return [.. matches.Take(_maximumVisibleOptions)];
        }
    }

    /// <summary>
    /// Opens the result list toward the side with more available viewport space.
    /// </summary>
    /// <returns>A task representing viewport measurement.</returns>
    private async Task OpenAsync()
    {
        double preferredHeight = Math.Min(240, 10 + (FilteredOptions.Count * 36));
        try
        {
            _openUp = await JavaScript.InvokeAsync<bool>("ironmonTrackerUi.shouldOpenPickerUp", _pickerElement, preferredHeight);
        }
        catch (JSException)
        {
            _openUp = false;
        }

        _open = true;
    }

    /// <summary>
    /// Closes the result list and restores the committed selection text.
    /// </summary>
    private void Close()
    {
        _open = false;
        _openUp = false;
        _query = Options.FirstOrDefault(option => option.Id == SelectedId)?.Name ?? string.Empty;
    }

    /// <summary>
    /// Updates the search text and optionally clears an emptied committed selection.
    /// </summary>
    /// <param name="args">The search input change.</param>
    /// <returns>A task representing selection notification.</returns>
    private async Task HandleInput(ChangeEventArgs args)
    {
        _query = args.Value?.ToString() ?? string.Empty;
        bool clearedSelection = ClearSelectionOnInput && SelectedId is not null && _query.Length == 0;
        _open = !clearedSelection;
        if (clearedSelection)
        {
            _observedSelectedId = null;
            await SelectedIdChanged.InvokeAsync(null);
        }
    }

    /// <summary>
    /// Selects the first match with Enter or closes the result list with Escape.
    /// </summary>
    /// <param name="args">The keyboard event.</param>
    /// <returns>A task representing the keyboard action.</returns>
    private async Task HandleKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == TrackerKeyboardKeys.Escape)
        {
            Close();
            return;
        }

        if (args.Key == TrackerKeyboardKeys.Enter && FilteredOptions.FirstOrDefault() is DebugDevelopmentCatalogEntry first)
            await SelectAsync(first);
    }

    /// <summary>
    /// Commits one selected catalog entry and closes the result list.
    /// </summary>
    /// <param name="option">The selected entry.</param>
    /// <returns>A task representing selection notification.</returns>
    private async Task SelectAsync(DebugDevelopmentCatalogEntry option)
    {
        _query = option.Name;
        _observedSelectedId = option.Id;
        _open = false;
        _openUp = false;
        await SelectedIdChanged.InvokeAsync(option.Id);
    }
}
