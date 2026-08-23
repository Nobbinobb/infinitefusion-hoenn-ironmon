using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Renders a staged generated evolution graph on a pannable and zoomable canvas.
/// </summary>
public partial class EvolutionGraph : IAsyncDisposable
{
    private const double _canvasMinimumHeight = 430;
    private const double _canvasMinimumWidth = 620;
    private const double _canvasPadding = 78;
    private const double _edgeLabelHeight = 52;
    private const double _edgeLabelWidth = 116;
    private const double _maximumScale = 2;
    private const double _minimumScale = 0.35;
    private const double _levelBandGap = 72;
    private const double _levelBandPadding = 22;
    private const double _nodeGap = 42;
    private const double _nodeHeight = 62;
    private const double _nodeWidth = 152;
    private const double _rangeButtonGap = 8;
    private const double _rangeButtonHeight = 28;
    private const double _rangeButtonWidth = 108;
    private const double _rangeSelectorGap = 76;
    private const double _rowGap = 116;
    private const double _zoomStep = 1.16;
    private readonly string _activeArrowMarkerId = $"evolution-arrow-active-{Guid.NewGuid():N}";
    private readonly string _faintArrowMarkerId = $"evolution-arrow-faint-{Guid.NewGuid():N}";
    private readonly Dictionary<int, int> _activePageIndexes = [];
    private readonly Dictionary<int, IReadOnlyList<IReadOnlyList<EvolutionTargetSnapshot>>> _levelPages = [];
    private readonly Dictionary<string, (int Level, int PageIndex)> _nodePageIndexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EvolutionTargetSnapshot> _nodeLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(int Level, int PageIndex), int> _portalCounts = [];
    private readonly Dictionary<string, (double X, double Y)> _positions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(int Level, int PageIndex), (double X, double Y)> _rangePositions = [];
    private readonly Dictionary<(int Level, int PageIndex), string> _rangeLabels = [];
    private readonly Dictionary<(int Level, int PageIndex), string> _rangeTitles = [];
    private ElementReference _canvas;
    private ElementReference _viewport;
    private IReadOnlyList<(int Level, int PageIndex, double X, double Y)> _expandedRangeLabels = [];
    private IReadOnlyList<(int Level, double Top, double Height)> _levelBands = [];
    private IReadOnlyList<EvolutionTargetSnapshot> _orderedNodes = [];
    private IReadOnlyList<(string Key, string VisibleSpeciesId, int HiddenLevel, int HiddenPageIndex, bool HiddenIsSource, int Count, bool Active)> _portalConnections = [];
    private IReadOnlyList<EvolutionGraphEdge> _visibleEdges = [];
    private IReadOnlyList<EvolutionTargetSnapshot> _visibleNodes = [];
    private bool _observedCompactRows;
    private string? _observedFocusSpeciesId;
    private int _observedViewportRevision = -1;
    private int _reportedVisibleNodeCount = -1;
    private double _canvasHeight;
    private double _canvasWidth;
    private double _focusX;
    private double _focusY;
    private bool _geometryChanged = true;
    private bool _viewNeedsReset = true;

    /// <summary>
    /// Gets or initializes browser interaction services.
    /// </summary>
    [Inject]
    private IJSRuntime JavaScript { get; set; } = null!;

    /// <summary>
    /// Gets or sets the selected graph node.
    /// </summary>
    [Parameter]
    public EvolutionTargetSnapshot Current { get; set; } = null!;

    /// <summary>
    /// Gets or sets all progressively loaded graph nodes.
    /// </summary>
    [Parameter]
    public IReadOnlyList<EvolutionTargetSnapshot> Nodes { get; set; } = [];

    /// <summary>
    /// Gets or sets all progressively loaded graph relationships.
    /// </summary>
    [Parameter]
    public IReadOnlyList<EvolutionGraphEdge> Edges { get; set; } = [];

    /// <summary>
    /// Gets or sets whether graph nodes can select an emphasized branch.
    /// </summary>
    [Parameter]
    public bool BranchSelectionEnabled { get; set; }

    /// <summary>
    /// Gets or sets the branch currently emphasized in the graph.
    /// </summary>
    [Parameter]
    public string? FocusedSpeciesId { get; set; }

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when a graph node is opened.
    /// </summary>
    [Parameter]
    public EventCallback<string> Selected { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when a graph branch is focused.
    /// </summary>
    [Parameter]
    public EventCallback<string> Focused { get; set; }

    /// <summary>
    /// Gets or sets a revision that changes when the containing viewport changes size.
    /// </summary>
    [Parameter]
    public int ViewportRevision { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of nodes displayed in one physical row.
    /// </summary>
    [Parameter]
    public int NodesPerRow { get; set; } = EvolutionGraphSettings.DefaultNodesPerRow;

    /// <summary>
    /// Gets or sets whether each logical level displays one selectable BST range.
    /// </summary>
    [Parameter]
    public bool CompactRows { get; set; }

    /// <summary>
    /// Gets or sets whether nodes should be grouped and styled by executable evolution reachability.
    /// </summary>
    [Parameter]
    public bool ReachabilityViewActive { get; set; }

    /// <summary>
    /// Gets or sets the stable identifiers participating in executable evolution relationships in the current graph view.
    /// </summary>
    [Parameter]
    public IReadOnlySet<string> ReachableSpeciesIds { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the callback invoked when the number of rendered graph nodes changes.
    /// </summary>
    [Parameter]
    public EventCallback<int> VisibleNodeCountChanged { get; set; }

    /// <summary>
    /// Gets the current canvas dimensions.
    /// </summary>
    private string CanvasStyle => string.Create(CultureInfo.InvariantCulture, $"width: {_canvasWidth:0.##}px; height: {_canvasHeight:0.##}px;");

    /// <summary>
    /// Gets the SVG coordinate system.
    /// </summary>
    private string SvgViewBox => string.Create(CultureInfo.InvariantCulture, $"0 0 {_canvasWidth:0.##} {_canvasHeight:0.##}");

    /// <summary>
    /// Recalculates staged rows while retaining the current pan and zoom during progressive loading.
    /// </summary>
    protected override async Task OnParametersSetAsync()
    {
        string focusSpeciesId = FocusedSpeciesId ?? Current.SpeciesId;
        bool focusChanged = _observedFocusSpeciesId != focusSpeciesId;
        bool compactRowsChanged = _observedCompactRows != CompactRows;
        _orderedNodes = [.. Nodes.OrderBy(node => node.StageLevel).ThenBy(node => node.BaseStatTotal).ThenBy(node => node.SpeciesName, StringComparer.CurrentCultureIgnoreCase).ThenBy(node => node.SpeciesId, StringComparer.OrdinalIgnoreCase)];
        BuildLayout(focusChanged || compactRowsChanged);
        _geometryChanged = true;
        if (_observedFocusSpeciesId != focusSpeciesId || _observedViewportRevision != ViewportRevision)
        {
            _observedFocusSpeciesId = focusSpeciesId;
            _observedViewportRevision = ViewportRevision;
            _viewNeedsReset = true;
        }

        _observedCompactRows = CompactRows;
        if (_reportedVisibleNodeCount != _visibleNodes.Count)
        {
            _reportedVisibleNodeCount = _visibleNodes.Count;
            await VisibleNodeCountChanged.InvokeAsync(_visibleNodes.Count);
        }
    }

    /// <summary>
    /// Centers the original node after the graph or containing viewport changes.
    /// </summary>
    /// <param name="firstRender">Whether this is the first render.</param>
    /// <returns>A task representing viewport measurement.</returns>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_geometryChanged)
            return;

        bool resetView = _viewNeedsReset;
        _geometryChanged = false;
        _viewNeedsReset = false;
        try
        {
            await JavaScript.InvokeVoidAsync("ironmonEvolutionGraph.update", _viewport, _canvas, _canvasWidth, _focusX, _focusY, _minimumScale, _maximumScale, resetView);
        }
        catch (JSException)
        {
        }
    }

    /// <summary>
    /// Gets whether one node is the branch currently emphasized by the user.
    /// </summary>
    /// <param name="speciesId">The node species identifier.</param>
    /// <returns>Whether the node is focused.</returns>
    private bool IsFocused(string speciesId)
        => speciesId.Equals(FocusedSpeciesId ?? Current.SpeciesId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets whether one node should use the normal evolution-reachable treatment.
    /// </summary>
    /// <param name="speciesId">The stable species identifier.</param>
    /// <returns>Whether the reachability view is disabled or the species participates in an executable evolution relationship.</returns>
    private bool IsReachable(string speciesId)
        => !ReachabilityViewActive || ReachableSpeciesIds.Contains(speciesId);

    /// <summary>
    /// Gets whether one relationship belongs to the focused branch.
    /// </summary>
    /// <param name="edge">The graph relationship.</param>
    /// <returns>Whether the relationship should use the accent treatment.</returns>
    private bool IsActive(EvolutionGraphEdge edge)
        => !BranchSelectionEnabled || IsFocused(edge.SourceSpeciesId) || IsFocused(edge.TargetSpeciesId);

    /// <summary>
    /// Gets whether one relationship requirement should be shown.
    /// </summary>
    /// <param name="edge">The graph relationship.</param>
    /// <returns>Whether the label belongs to the selected branch.</returns>
    private bool ShouldShowLabel(EvolutionGraphEdge edge)
        => IsActive(edge) && IsReachable(edge.SourceSpeciesId) && IsReachable(edge.TargetSpeciesId);

    /// <summary>
    /// Gets the positioned style for one node.
    /// </summary>
    /// <param name="speciesId">The node species identifier.</param>
    /// <returns>The invariant CSS position.</returns>
    private string GetNodeStyle(string speciesId)
    {
        (double x, double y) = _positions[speciesId];
        return string.Create(CultureInfo.InvariantCulture, $"left: {x:0.##}px; top: {y:0.##}px; width: {_nodeWidth:0.##}px; height: {_nodeHeight:0.##}px;");
    }

    /// <summary>
    /// Gets the positioned style for one logical level band.
    /// </summary>
    /// <param name="top">The band's vertical canvas coordinate.</param>
    /// <param name="height">The band's canvas height.</param>
    /// <returns>The invariant CSS position.</returns>
    private string GetLevelBandStyle(double top, double height)
        => string.Create(CultureInfo.InvariantCulture, $"top: {top:0.##}px; width: {_canvasWidth:0.##}px; height: {height:0.##}px;");

    /// <summary>
    /// Gets the positioned style for one BST range label or selector.
    /// </summary>
    /// <param name="x">The horizontal canvas coordinate.</param>
    /// <param name="y">The vertical canvas coordinate.</param>
    /// <returns>The invariant CSS position.</returns>
    private static string GetRangeStyle(double x, double y)
        => string.Create(CultureInfo.InvariantCulture, $"left: {x:0.##}px; top: {y:0.##}px; width: {_rangeButtonWidth:0.##}px; height: {_rangeButtonHeight:0.##}px;");

    /// <summary>
    /// Gets the display label for one stable BST-ordered range.
    /// </summary>
    /// <param name="level">The logical graph level.</param>
    /// <param name="pageIndex">The zero-based range index.</param>
    /// <returns>The localized BST range label.</returns>
    private string GetRangeLabel(int level, int pageIndex)
        => _rangeLabels[(level, pageIndex)];

    /// <summary>
    /// Gets the descriptive tooltip for one stable BST-ordered range.
    /// </summary>
    /// <param name="level">The logical graph level.</param>
    /// <param name="pageIndex">The zero-based range index.</param>
    /// <returns>The localized range description.</returns>
    private string GetRangeTitle(int level, int pageIndex)
        => _rangeTitles[(level, pageIndex)];

    /// <summary>
    /// Gets whether one compact BST range is displayed.
    /// </summary>
    /// <param name="level">The logical graph level.</param>
    /// <param name="pageIndex">The zero-based range index.</param>
    /// <returns>Whether the range is active.</returns>
    private bool IsRangeActive(int level, int pageIndex)
        => _activePageIndexes.TryGetValue(level, out int activePageIndex) && activePageIndex == pageIndex;

    /// <summary>
    /// Gets whether one BST range contains evolution-reachable nodes or reachability grouping is disabled.
    /// </summary>
    /// <param name="level">The logical graph level.</param>
    /// <param name="pageIndex">The zero-based range index.</param>
    /// <returns>Whether the range uses the normal reachable treatment.</returns>
    private bool IsRangeReachable(int level, int pageIndex)
        => !ReachabilityViewActive || _levelPages[level][pageIndex].Any(node => IsReachable(node.SpeciesId));

    /// <summary>
    /// Gets whether one BST range contains the graph's focused Pokemon.
    /// </summary>
    /// <param name="level">The logical graph level.</param>
    /// <param name="pageIndex">The zero-based range index.</param>
    /// <returns>Whether the range contains the focused Pokemon.</returns>
    private bool IsFocusRange(int level, int pageIndex)
    {
        string focusSpeciesId = FocusedSpeciesId ?? Current.SpeciesId;
        return _nodePageIndexes.TryGetValue(focusSpeciesId, out (int Level, int PageIndex) location) && location.Level == level && location.PageIndex == pageIndex;
    }

    /// <summary>
    /// Gets the number of currently represented hidden connections for one range portal.
    /// </summary>
    /// <param name="level">The logical graph level.</param>
    /// <param name="pageIndex">The zero-based range index.</param>
    /// <returns>The hidden connection count.</returns>
    private int GetPortalCount(int level, int pageIndex)
        => _portalCounts.GetValueOrDefault((level, pageIndex));

    /// <summary>
    /// Selects one BST range without changing the focused Pokemon.
    /// </summary>
    /// <param name="level">The logical graph level.</param>
    /// <param name="pageIndex">The zero-based range index.</param>
    /// <returns>A task representing visible-count notification.</returns>
    private async Task SelectRangeAsync(int level, int pageIndex)
    {
        if (!_levelPages.TryGetValue(level, out IReadOnlyList<IReadOnlyList<EvolutionTargetSnapshot>>? pages) || pageIndex < 0 || pageIndex >= pages.Count || IsRangeActive(level, pageIndex))
            return;

        _activePageIndexes[level] = pageIndex;
        BuildLayout(selectFocusRanges: false);
        _geometryChanged = true;
        if (_reportedVisibleNodeCount != _visibleNodes.Count)
        {
            _reportedVisibleNodeCount = _visibleNodes.Count;
            await VisibleNodeCountChanged.InvokeAsync(_visibleNodes.Count);
        }
    }

    /// <summary>
    /// Gets the curved connector between one visible Pokemon and a hidden BST range portal.
    /// </summary>
    /// <param name="connection">The aggregated compact connection.</param>
    /// <returns>The SVG path.</returns>
    private string GetPortalPath((string Key, string VisibleSpeciesId, int HiddenLevel, int HiddenPageIndex, bool HiddenIsSource, int Count, bool Active) connection)
    {
        (double nodeX, double nodeY) = _positions[connection.VisibleSpeciesId];
        (double portalX, double portalY) = _rangePositions[(connection.HiddenLevel, connection.HiddenPageIndex)];
        double nodeCenterX = nodeX + (_nodeWidth / 2);
        double nodeCenterY = nodeY + (_nodeHeight / 2);
        double portalCenterX = portalX + (_rangeButtonWidth / 2);
        double portalCenterY = portalY + (_rangeButtonHeight / 2);
        double nodeAnchorY = portalCenterY >= nodeCenterY ? nodeY + _nodeHeight : nodeY;
        double startX = connection.HiddenIsSource ? portalCenterX : nodeCenterX;
        double startY = connection.HiddenIsSource ? portalCenterY : nodeAnchorY;
        double endX = connection.HiddenIsSource ? nodeCenterX : portalCenterX;
        double endY = connection.HiddenIsSource ? nodeAnchorY : portalCenterY;
        double middleY = (startY + endY) / 2;
        return FormatPath(startX, startY, startX, middleY, endX, middleY, endX, endY);
    }

    /// <summary>
    /// Gets the curved connector for one directed relationship.
    /// </summary>
    /// <param name="edge">The graph relationship.</param>
    /// <returns>The SVG path.</returns>
    private string GetPath(EvolutionGraphEdge edge)
    {
        (double sourceX, double sourceY) = _positions[edge.SourceSpeciesId];
        (double targetX, double targetY) = _positions[edge.TargetSpeciesId];
        double startX = sourceX + (_nodeWidth / 2);
        double endX = targetX + (_nodeWidth / 2);
        bool downward = targetY >= sourceY;
        double startY = sourceY + (downward ? _nodeHeight : 0);
        double endY = targetY + (downward ? -5 : _nodeHeight + 5);
        double middleY = (startY + endY) / 2;
        return FormatPath(startX, startY, startX, middleY, endX, middleY, endX, endY);
    }

    /// <summary>
    /// Gets the horizontal coordinate for one active relationship label.
    /// </summary>
    /// <param name="edge">The graph relationship.</param>
    /// <returns>The label coordinate.</returns>
    private double GetLabelX(EvolutionGraphEdge edge)
    {
        string neighborSpeciesId = IsFocused(edge.TargetSpeciesId) ? edge.SourceSpeciesId : edge.TargetSpeciesId;
        (double neighborX, _) = _positions[neighborSpeciesId];
        return neighborX + ((_nodeWidth - _edgeLabelWidth) / 2);
    }

    /// <summary>
    /// Gets the vertical coordinate for one active relationship label.
    /// </summary>
    /// <param name="edge">The graph relationship.</param>
    /// <returns>The label coordinate.</returns>
    private double GetLabelY(EvolutionGraphEdge edge)
    {
        (_, double sourceY) = _positions[edge.SourceSpeciesId];
        (_, double targetY) = _positions[edge.TargetSpeciesId];
        double y = IsFocused(edge.TargetSpeciesId)
            ? targetY >= sourceY ? sourceY + _nodeHeight + 12 : sourceY - _edgeLabelHeight - 12
            : targetY >= sourceY ? targetY - _edgeLabelHeight - 12 : targetY + _nodeHeight + 12;

        return Math.Clamp(y, 4, _canvasHeight - _edgeLabelHeight - 4);
    }

    /// <summary>
    /// Gets the marker reference for one relationship treatment.
    /// </summary>
    /// <param name="active">Whether the relationship is active.</param>
    /// <returns>The SVG marker reference.</returns>
    private string GetArrowMarkerReference(bool active)
        => $"url(#{(active ? _activeArrowMarkerId : _faintArrowMarkerId)})";

    /// <summary>
    /// Formats a cubic SVG connector path.
    /// </summary>
    /// <param name="startX">The starting horizontal coordinate.</param>
    /// <param name="startY">The starting vertical coordinate.</param>
    /// <param name="controlOneX">The first control-point horizontal coordinate.</param>
    /// <param name="controlOneY">The first control-point vertical coordinate.</param>
    /// <param name="controlTwoX">The second control-point horizontal coordinate.</param>
    /// <param name="controlTwoY">The second control-point vertical coordinate.</param>
    /// <param name="endX">The ending horizontal coordinate.</param>
    /// <param name="endY">The ending vertical coordinate.</param>
    /// <returns>The invariant SVG path.</returns>
    private static string FormatPath(double startX, double startY, double controlOneX, double controlOneY, double controlTwoX, double controlTwoY, double endX, double endY)
        => string.Create(CultureInfo.InvariantCulture, $"M {startX:0.##} {startY:0.##} C {controlOneX:0.##} {controlOneY:0.##}, {controlTwoX:0.##} {controlTwoY:0.##}, {endX:0.##} {endY:0.##}");

    /// <summary>
    /// Increases graph magnification around the viewport center.
    /// </summary>
    /// <returns>A task representing the browser transform.</returns>
    private Task ZoomInAsync()
        => JavaScript.InvokeVoidAsync("ironmonEvolutionGraph.zoom", _viewport, _zoomStep).AsTask();

    /// <summary>
    /// Decreases graph magnification around the viewport center.
    /// </summary>
    /// <returns>A task representing the browser transform.</returns>
    private Task ZoomOutAsync()
        => JavaScript.InvokeVoidAsync("ironmonEvolutionGraph.zoom", _viewport, 1 / _zoomStep).AsTask();

    /// <summary>
    /// Centers the selected node and returns the graph to its default readable scale.
    /// </summary>
    /// <returns>A task representing viewport measurement.</returns>
    private Task ResetViewAsync()
        => JavaScript.InvokeVoidAsync("ironmonEvolutionGraph.reset", _viewport).AsTask();

    /// <summary>
    /// Releases browser event handlers when the graph leaves the render tree.
    /// </summary>
    /// <returns>A value task representing browser cleanup.</returns>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await JavaScript.InvokeVoidAsync("ironmonEvolutionGraph.dispose", _viewport);
        }
        catch (JSDisconnectedException)
        {
        }

        GC.SuppressFinalize(this);
    }
}
