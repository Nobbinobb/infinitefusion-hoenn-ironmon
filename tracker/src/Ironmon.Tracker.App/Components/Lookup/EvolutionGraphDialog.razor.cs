using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Displays a generated evolution graph while progressively loading bounded neighborhoods.
/// </summary>
public partial class EvolutionGraphDialog : IAsyncDisposable
{
    private readonly Dictionary<string, EvolutionGraphEdge> _edges = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _neighbors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EvolutionTargetSnapshot> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _loadCancellation;
    private string? _error;
    private string? _expansionOriginSpeciesId;
    private string? _focusedSpeciesId;
    private string? _observedSource;
    private int _displayedNodeCount;
    private int _expansionDepth;
    private int _nodesPerRow;
    private int _viewportRevision;
    private bool _disposed;
    private bool _compactRows;
    private bool _firstPageLoaded;
    private bool _loading;
    private bool _maximized;
    private bool _showAllLoaded;

    /// <summary>
    /// Gets or initializes the active game request client.
    /// </summary>
    [Inject]
    private TrackerRequestClient Connection { get; set; } = null!;

    /// <summary>
    /// Gets or initializes native tracker-window control.
    /// </summary>
    [Inject]
    private TrackerWindowService WindowService { get; set; } = null!;

    /// <summary>
    /// Gets the dialog classes for its current window state.
    /// </summary>
    private string DialogCssClass => _maximized ? "detail-panel evolution-graph-dialog maximized" : "detail-panel evolution-graph-dialog";

    /// <summary>
    /// Gets the accessible label for the window-size control.
    /// </summary>
    private string WindowToggleLabel => _maximized ? Text["Lookup.Graph.Restore"] : Text["Lookup.Graph.Maximize"];

    /// <summary>
    /// Gets the icon for the window-size control.
    /// </summary>
    private string WindowToggleIcon => _maximized ? "❐" : "⛶";

    /// <summary>
    /// Gets the accessible label for the graph-scope control.
    /// </summary>
    private string ScopeToggleLabel => _showAllLoaded ? Text["Lookup.Graph.ShowFocusedNeighborhood"] : Text["Lookup.Graph.ShowAllLoaded"];

    /// <summary>
    /// Gets the icon for the graph-scope control.
    /// </summary>
    private string ScopeToggleIcon => _showAllLoaded ? "◎" : "▦";

    /// <summary>
    /// Gets the accessible label for the physical-row display control.
    /// </summary>
    private string RowModeToggleLabel => _compactRows ? Text["Lookup.Graph.ShowExpandedRows"] : Text["Lookup.Graph.ShowCompactRows"];

    /// <summary>
    /// Gets the icon for the physical-row display control.
    /// </summary>
    private string RowModeToggleIcon => _compactRows ? "☷" : "▤";

    /// <summary>
    /// Gets the graph nodes visible in the selected canvas scope.
    /// </summary>
    private IReadOnlyList<EvolutionTargetSnapshot> GraphNodes
    {
        get
        {
            HashSet<string> visible = GetVisibleSpeciesIds();
            return [.. _nodes.Values.Where(node => visible.Contains(node.SpeciesId))];
        }
    }

    /// <summary>
    /// Gets the graph relationships visible in the selected canvas scope.
    /// </summary>
    private IReadOnlyList<EvolutionGraphEdge> GraphEdges
    {
        get
        {
            HashSet<string> visible = GetVisibleSpeciesIds();
            return [.. _edges.Values.Where(edge => visible.Contains(edge.SourceSpeciesId) && visible.Contains(edge.TargetSpeciesId))];
        }
    }

    /// <summary>
    /// Gets or sets the target species identifier.
    /// </summary>
    [Parameter]
    public required string SpeciesId { get; set; }

    /// <summary>
    /// Gets or sets the selected graph node.
    /// </summary>
    [Parameter]
    public required EvolutionTargetSnapshot Current { get; set; }

    /// <summary>
    /// Gets or sets immediate generated normal targets.
    /// </summary>
    [Parameter]
    public IReadOnlyList<EvolutionTargetSnapshot> Targets { get; set; } = [];

    /// <summary>
    /// Gets or sets immediate targets activated by the fusion Head.
    /// </summary>
    [Parameter]
    public IReadOnlyList<EvolutionTargetSnapshot> HeadTargets { get; set; } = [];

    /// <summary>
    /// Gets or sets immediate targets activated by the fusion Body.
    /// </summary>
    [Parameter]
    public IReadOnlyList<EvolutionTargetSnapshot> BodyTargets { get; set; } = [];

    /// <summary>
    /// Gets or sets the completed-run reconstruction recipe when lookup is not using Debug.
    /// </summary>
    [Parameter]
    public CompletedRunRecipePayload? Recipe { get; set; }

    /// <summary>
    /// Gets or sets whether requests use the authorized active Debug run.
    /// </summary>
    [Parameter]
    public bool DebugMode { get; set; }

    /// <summary>
    /// Gets or sets the optional live source that securely supplies the represented species.
    /// </summary>
    [Parameter]
    public DebugPokemonTarget? DebugTarget { get; set; }

    /// <summary>
    /// Gets or sets the enemy battler position when the live source is an enemy.
    /// </summary>
    [Parameter]
    public int? DebugEnemyPosition { get; set; }

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when a graph node is selected.
    /// </summary>
    [Parameter]
    public EventCallback<string> Selected { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the dialog should close.
    /// </summary>
    [Parameter]
    public EventCallback Closed { get; set; }

    /// <summary>
    /// Starts a fresh progressive lookup when the represented source changes.
    /// </summary>
    /// <returns>A task representing the progressive lookup.</returns>
    protected override async Task OnParametersSetAsync()
    {
        string source = $"{SpeciesId}|{DebugMode}|{DebugTarget}|{DebugEnemyPosition}|{Recipe?.RunId}|{EvolutionGraphSettings.ExpansionDepth}|{EvolutionGraphSettings.NodesPerRow}";
        if (source == _observedSource)
            return;

        _observedSource = source;
        _viewportRevision++;
        await LoadGraphAsync();
    }

    /// <summary>
    /// Loads a fresh bounded graph neighborhood around the original Pokemon.
    /// </summary>
    /// <returns>A task representing the progressive lookup.</returns>
    private async Task LoadGraphAsync()
    {
        CancelLoading();
        _nodes.Clear();
        _edges.Clear();
        _expandedNodes.Clear();
        _neighbors.Clear();
        _error = null;
        _displayedNodeCount = 0;
        _firstPageLoaded = false;
        _showAllLoaded = false;
        _expansionDepth = DebugMode && !Connection.HasDiagnosticCapability(DiagnosticCapabilities.PokemonAllActive)
            ? EvolutionGraphSettings.MinimumExpansionDepth
            : EvolutionGraphSettings.ExpansionDepth;

        _nodesPerRow = EvolutionGraphSettings.NodesPerRow;
        _focusedSpeciesId = Current.SpeciesId;
        AddNode(Current);
        AddOutgoingRelationships(Current.SpeciesId, Targets, HeadTargets, BodyTargets);
        await StartNeighborhoodExpansionAsync(SpeciesId);
    }

    /// <summary>
    /// Starts one cancelable bounded expansion around a selected graph node.
    /// </summary>
    /// <param name="originSpeciesId">The center of the requested neighborhood.</param>
    /// <returns>A task representing the progressive expansion.</returns>
    private async Task StartNeighborhoodExpansionAsync(string originSpeciesId)
    {
        if (!NeedsExpansion(originSpeciesId))
            return;

        CancelLoading();
        _error = null;
        _expansionOriginSpeciesId = originSpeciesId;
        _loading = true;
        CancellationTokenSource cancellation = new();
        _loadCancellation = cancellation;
        try
        {
            await ExpandNeighborhoodAsync(originSpeciesId, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException)
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
                _error = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                _loadCancellation = null;
                _loading = false;
                cancellation.Dispose();
                if (!_disposed)
                    await InvokeAsync(StateHasChanged);
            }
            else
            {
                cancellation.Dispose();
            }
        }
    }

    /// <summary>
    /// Expands only unprocessed nodes inside the configured distance from one selection.
    /// </summary>
    /// <param name="originSpeciesId">The selected neighborhood center.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the bounded breadth-first expansion.</returns>
    private async Task ExpandNeighborhoodAsync(string originSpeciesId, CancellationToken cancellationToken)
    {
        Queue<(string SpeciesId, int Distance)> pending = new();
        HashSet<string> queued = new(StringComparer.OrdinalIgnoreCase) { originSpeciesId };
        pending.Enqueue((originSpeciesId, 0));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (string speciesId, int distance) = pending.Dequeue();
            if (distance >= _expansionDepth)
                continue;

            if (!_expandedNodes.Contains(speciesId))
                await ExpandNodeAsync(speciesId, cancellationToken);

            if (distance + 1 >= _expansionDepth)
                continue;

            foreach (string relatedSpeciesId in GetRelatedSpeciesIds(speciesId))
            {
                if (queued.Add(relatedSpeciesId))
                    pending.Enqueue((relatedSpeciesId, distance + 1));
            }

            await Task.Yield();
        }
    }

    /// <summary>
    /// Loads all outgoing relationships and predecessor pages required to complete one node.
    /// </summary>
    /// <param name="speciesId">The node to complete.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the node expansion.</returns>
    private async Task ExpandNodeAsync(string speciesId, CancellationToken cancellationToken)
    {
        bool root = speciesId.Equals(SpeciesId, StringComparison.OrdinalIgnoreCase);
        if (!root)
        {
            PokemonLookupSnapshot lookup = await LookupEvolutionsAsync(speciesId, cancellationToken);
            PokemonLookupEvolutionsSnapshot evolutions = lookup.Evolutions ?? throw new TrackerProtocolException("The evolution lookup response is missing graph data.");
            AddNode(new EvolutionTargetSnapshot
            {
                SpeciesId = lookup.Identity.SpeciesId,
                SpeciesName = lookup.Identity.SpeciesName,
                SpritePath = lookup.Identity.SpritePath,
                BaseStatTotal = evolutions.CurrentBaseStatTotal,
                StageLevel = evolutions.CurrentStageLevel
            });

            AddOutgoingRelationships(speciesId, evolutions.GeneratedTargets, evolutions.HeadTargets, evolutions.BodyTargets);
            await InvokeAsync(StateHasChanged);
        }

        await LoadPredecessorPagesAsync(speciesId, root, cancellationToken);
        _expandedNodes.Add(speciesId);
    }

    /// <summary>
    /// Loads every predecessor page for one node while exposing each page immediately.
    /// </summary>
    /// <param name="speciesId">The destination species identifier.</param>
    /// <param name="root">Whether this is the graph's original selected node.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the paged lookup.</returns>
    private async Task LoadPredecessorPagesAsync(string speciesId, bool root, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (true)
        {
            EvolutionPredecessorSearchResponsePayload response = await SearchPredecessorsAsync(speciesId, offset, root, cancellationToken);
            ValidatePage(response, offset);
            foreach (EvolutionTargetSnapshot predecessor in response.Matches)
            {
                AddNode(predecessor);
                AddEdge(predecessor.SpeciesId, speciesId, predecessor);
            }

            _firstPageLoaded = true;
            await InvokeAsync(StateHasChanged);
            if (response.Continuation == EvolutionPredecessorContinuation.Complete)
                break;

            offset = response.NextOffset!.Value;
            await Task.Yield();
        }
    }

    /// <summary>
    /// Requests one evolution lookup for recursive expansion.
    /// </summary>
    /// <param name="speciesId">The represented species identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The requested evolution section.</returns>
    private Task<PokemonLookupSnapshot> LookupEvolutionsAsync(string speciesId, CancellationToken cancellationToken)
    {
        return DebugMode
            ? Connection.LookupDebugPokemonAsync(speciesId, PokemonLookupSection.Evolutions, cancellationToken)
            : Connection.LookupPokemonAsync(Recipe ?? throw new InvalidOperationException("A completed-run recipe is required to load the evolution tree."), speciesId, PokemonLookupSection.Evolutions, cancellationToken);
    }

    /// <summary>
    /// Requests one predecessor page, retaining the secure live target only for the original node.
    /// </summary>
    /// <param name="speciesId">The destination species identifier.</param>
    /// <param name="offset">The requested page offset.</param>
    /// <param name="root">Whether this is the original node.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The requested predecessor page.</returns>
    private Task<EvolutionPredecessorSearchResponsePayload> SearchPredecessorsAsync(string speciesId, int offset, bool root, CancellationToken cancellationToken)
    {
        return DebugMode
            ? Connection.SearchDebugEvolutionPredecessorsAsync(speciesId, offset, TrackerProtocol.EvolutionPredecessorPageSize, root ? DebugTarget : null, root ? DebugEnemyPosition : null, cancellationToken)
            : Connection.SearchEvolutionPredecessorsAsync(Recipe ?? throw new InvalidOperationException("A completed-run recipe is required to load evolution predecessors."), speciesId, offset, TrackerProtocol.EvolutionPredecessorPageSize, cancellationToken);
    }

    /// <summary>
    /// Adds one node to the graph.
    /// </summary>
    /// <param name="node">The node snapshot.</param>
    private void AddNode(EvolutionTargetSnapshot node)
        => _nodes[node.SpeciesId] = node;

    /// <summary>
    /// Adds all outgoing relationships returned by one evolution lookup.
    /// </summary>
    /// <param name="sourceSpeciesId">The source species identifier.</param>
    /// <param name="normal">Normal relationships.</param>
    /// <param name="head">Fusion-head relationships.</param>
    /// <param name="body">Fusion-body relationships.</param>
    private void AddOutgoingRelationships(string sourceSpeciesId, IReadOnlyList<EvolutionTargetSnapshot> normal, IReadOnlyList<EvolutionTargetSnapshot> head, IReadOnlyList<EvolutionTargetSnapshot> body)
    {
        AddOutgoingGroup(sourceSpeciesId, normal, EvolutionCandidateSide.Normal);
        AddOutgoingGroup(sourceSpeciesId, head, EvolutionCandidateSide.Head);
        AddOutgoingGroup(sourceSpeciesId, body, EvolutionCandidateSide.Body);
    }

    /// <summary>
    /// Adds one outgoing relationship group.
    /// </summary>
    /// <param name="sourceSpeciesId">The source species identifier.</param>
    /// <param name="targets">The destination snapshots.</param>
    /// <param name="fallbackSide">The component side used for older payloads.</param>
    private void AddOutgoingGroup(string sourceSpeciesId, IReadOnlyList<EvolutionTargetSnapshot> targets, EvolutionCandidateSide fallbackSide)
    {
        foreach (EvolutionTargetSnapshot target in targets)
        {
            AddNode(target);
            EvolutionCandidateSide side = target.ComponentSide == EvolutionCandidateSide.Normal ? fallbackSide : target.ComponentSide;
            EvolutionGraphEdge edge = new(sourceSpeciesId, target.SpeciesId, target.EffectiveMethods, side);
            AddGraphEdge(edge);
        }
    }

    /// <summary>
    /// Adds one predecessor relationship.
    /// </summary>
    /// <param name="sourceSpeciesId">The predecessor species identifier.</param>
    /// <param name="targetSpeciesId">The destination species identifier.</param>
    /// <param name="source">The predecessor snapshot carrying relationship metadata.</param>
    private void AddEdge(string sourceSpeciesId, string targetSpeciesId, EvolutionTargetSnapshot source)
    {
        EvolutionGraphEdge edge = new(sourceSpeciesId, targetSpeciesId, source.EffectiveMethods, source.ComponentSide);
        AddGraphEdge(edge);
    }

    /// <summary>
    /// Adds one deduplicated edge and updates the undirected neighborhood index.
    /// </summary>
    /// <param name="edge">The directed graph relationship.</param>
    private void AddGraphEdge(EvolutionGraphEdge edge)
    {
        if (!_edges.TryAdd(edge.Key, edge))
            return;

        AddNeighbor(edge.SourceSpeciesId, edge.TargetSpeciesId);
        AddNeighbor(edge.TargetSpeciesId, edge.SourceSpeciesId);
    }

    /// <summary>
    /// Adds one neighbor to the graph traversal index.
    /// </summary>
    /// <param name="speciesId">The indexed species identifier.</param>
    /// <param name="neighborSpeciesId">The directly connected species identifier.</param>
    private void AddNeighbor(string speciesId, string neighborSpeciesId)
    {
        if (!_neighbors.TryGetValue(speciesId, out HashSet<string>? neighbors))
        {
            neighbors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _neighbors[speciesId] = neighbors;
        }

        neighbors.Add(neighborSpeciesId);
    }

    /// <summary>
    /// Gets all currently known nodes directly connected to one graph node.
    /// </summary>
    /// <param name="speciesId">The graph node identifier.</param>
    /// <returns>The distinct neighboring node identifiers.</returns>
    private IReadOnlyList<string> GetRelatedSpeciesIds(string speciesId)
        => _neighbors.TryGetValue(speciesId, out HashSet<string>? related) ? [.. related] : [];

    /// <summary>
    /// Gets the loaded node identities shown by the focused or all-loaded scope.
    /// </summary>
    /// <returns>The visible species identifiers.</returns>
    private HashSet<string> GetVisibleSpeciesIds()
    {
        if (_showAllLoaded)
            return new HashSet<string>(_nodes.Keys, StringComparer.OrdinalIgnoreCase);

        string originSpeciesId = _focusedSpeciesId ?? SpeciesId;
        HashSet<string> visible = new(StringComparer.OrdinalIgnoreCase) { originSpeciesId };
        Queue<(string SpeciesId, int Distance)> pending = new();
        pending.Enqueue((originSpeciesId, 0));
        while (pending.Count > 0)
        {
            (string speciesId, int distance) = pending.Dequeue();
            if (distance >= _expansionDepth)
                continue;

            foreach (string relatedSpeciesId in GetRelatedSpeciesIds(speciesId))
            {
                if (visible.Add(relatedSpeciesId))
                    pending.Enqueue((relatedSpeciesId, distance + 1));
            }
        }

        visible.IntersectWith(_nodes.Keys);
        return visible;
    }

    /// <summary>
    /// Switches between the selected bounded neighborhood and every loaded node.
    /// </summary>
    private void ToggleGraphScope()
    {
        _showAllLoaded = !_showAllLoaded;
        _viewportRevision++;
    }

    /// <summary>
    /// Switches between all physical BST rows and one selected range per logical level.
    /// </summary>
    private void ToggleRowMode()
    {
        _compactRows = !_compactRows;
        _viewportRevision++;
    }

    /// <summary>
    /// Updates the status count after compact range selection changes rendered nodes.
    /// </summary>
    /// <param name="count">The number of graph nodes currently rendered.</param>
    private void HandleVisibleNodeCountChanged(int count)
        => _displayedNodeCount = count;

    /// <summary>
    /// Gets whether the configured neighborhood around one node contains incomplete data.
    /// </summary>
    /// <param name="originSpeciesId">The selected neighborhood center.</param>
    /// <returns>Whether selecting the node should start another request.</returns>
    private bool NeedsExpansion(string originSpeciesId)
    {
        Queue<(string SpeciesId, int Distance)> pending = new();
        HashSet<string> queued = new(StringComparer.OrdinalIgnoreCase) { originSpeciesId };
        pending.Enqueue((originSpeciesId, 0));
        while (pending.Count > 0)
        {
            (string speciesId, int distance) = pending.Dequeue();
            if (distance >= _expansionDepth)
                continue;

            if (!_expandedNodes.Contains(speciesId))
                return true;

            if (distance + 1 >= _expansionDepth)
                continue;

            foreach (string relatedSpeciesId in GetRelatedSpeciesIds(speciesId))
            {
                if (queued.Add(relatedSpeciesId))
                    pending.Enqueue((relatedSpeciesId, distance + 1));
            }
        }

        return false;
    }

    /// <summary>
    /// Validates that one page can safely advance the progressive lookup.
    /// </summary>
    /// <param name="response">The page returned by the game.</param>
    /// <param name="expectedOffset">The offset requested by the tracker.</param>
    private static void ValidatePage(EvolutionPredecessorSearchResponsePayload response, int expectedOffset)
    {
        if (response.Offset != expectedOffset)
            throw new TrackerProtocolException("The evolution predecessor response returned an unexpected offset.");

        if (response.Continuation != EvolutionPredecessorContinuation.Complete && (response.NextOffset is null || response.NextOffset <= expectedOffset))
            throw new TrackerProtocolException("The evolution predecessor response cannot advance to another page.");
    }

    /// <summary>
    /// Retries the progressive graph lookup from its first page.
    /// </summary>
    /// <returns>A task representing the retry.</returns>
    private Task RetryAsync()
        => StartNeighborhoodExpansionAsync(_expansionOriginSpeciesId ?? SpeciesId);

    /// <summary>
    /// Selects the emphasized branch and fills its configured missing neighborhood.
    /// </summary>
    /// <param name="speciesId">The focused species identifier.</param>
    /// <returns>A task representing any required bounded expansion.</returns>
    private async Task FocusPokemonAsync(string speciesId)
    {
        _focusedSpeciesId = speciesId;
        _viewportRevision++;
        await InvokeAsync(StateHasChanged);
        if (DebugMode && !speciesId.Equals(SpeciesId, StringComparison.OrdinalIgnoreCase) && !Connection.HasDiagnosticCapability(DiagnosticCapabilities.PokemonAllActive))
            return;

        await StartNeighborhoodExpansionAsync(speciesId);
    }

    /// <summary>
    /// Switches the native tracker between its compact window and the current monitor.
    /// </summary>
    /// <returns>A task representing the native window operation.</returns>
    private async Task ToggleMaximizedAsync()
    {
        _maximized = await WindowService.ToggleMaximizedAsync();
        _viewportRevision++;
    }

    /// <summary>
    /// Cancels pending predecessor work and closes the dialog.
    /// </summary>
    /// <returns>A task representing callback delivery.</returns>
    private async Task CloseAsync()
    {
        CancelLoading();
        await RestoreWindowAsync();
        await Closed.InvokeAsync();
    }

    /// <summary>
    /// Cancels loading, closes the dialog, and navigates to one graph node.
    /// </summary>
    /// <param name="speciesId">The selected species identifier.</param>
    /// <returns>A task representing callback delivery.</returns>
    private async Task SelectPokemonAsync(string speciesId)
    {
        CancelLoading();
        await RestoreWindowAsync();
        await Closed.InvokeAsync();
        await Selected.InvokeAsync(speciesId);
    }

    /// <summary>
    /// Cancels the active progressive request, if any.
    /// </summary>
    private void CancelLoading()
        => _loadCancellation?.Cancel();

    /// <summary>
    /// Restores the compact tracker window when this dialog expanded it.
    /// </summary>
    /// <returns>A task representing the native window operation.</returns>
    private async Task RestoreWindowAsync()
    {
        if (!_maximized)
            return;

        await WindowService.RestoreAsync();
        _maximized = false;
    }

    /// <summary>
    /// Cancels background loading when the dialog leaves the render tree.
    /// </summary>
    /// <returns>A value task representing native window restoration.</returns>
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        CancelLoading();
        await RestoreWindowAsync();
        GC.SuppressFinalize(this);
    }
}
