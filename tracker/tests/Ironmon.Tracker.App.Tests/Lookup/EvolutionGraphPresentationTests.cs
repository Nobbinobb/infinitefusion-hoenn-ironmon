using System.Reflection;
using System.Resources;
using Ironmon.Tracker.App.Components.Lookup;
using Ironmon.Tracker.Protocol.Lookup;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Ironmon.Tracker.App.Tests.Lookup;

/// <summary>
/// Checks dense graph layouts, independent display controls, and complete hidden-range route details.
/// </summary>
public sealed class EvolutionGraphPresentationTests
{
    private const string _memberShowAllLoaded = "_showAllLoaded";
    private const string _memberCompactRows = "_compactRows";
    private const string _memberNodes = "_nodes";
    private const string _memberNeighbors = "_neighbors";
    private const string _memberFocusedSpeciesId = "_focusedSpeciesId";
    private const string _memberExpansionDepth = "_expansionDepth";
    private const string _memberToggleGraphScope = "ToggleGraphScope";
    private const string _memberToggleRowMode = "ToggleRowMode";
    private const string _memberGraphNodes = "GraphNodes";
    private const string _memberText = "Text";
    private const string _memberOnParametersSetAsync = "OnParametersSetAsync";
    private const string _memberPositions = "_positions";
    private const string _memberRangePositions = "_rangePositions";
    private const string _memberExpandedRangeLabels = "_expandedRangeLabels";
    private const string _memberGetFocusedRoutes = "GetFocusedRoutes";
    private const string _memberSelectRangeAsync = "SelectRangeAsync";
    private const BindingFlags _members = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string _nodePrefix = "node-";
    private const string _method = "Use a Water Stone at night with high friendship";
    private const string _resources = "Ironmon.Tracker.App.Resources.Localization.TrackerResources";

    /// <summary>
    /// Verifies all four scope and row combinations retain the configured graph-neighbor distance.
    /// </summary>
    /// <param name="all">Whether all loaded nodes should be displayed.</param>
    /// <param name="single">Whether each logical level should display one BST range.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ScopeAndRowsRemainIndependent(bool all, bool single)
    {
        EvolutionGraphDialog dialog = Activator.CreateInstance<EvolutionGraphDialog>();
        Assert.False(Field<bool>(dialog, _memberShowAllLoaded));
        Assert.False(Field<bool>(dialog, _memberCompactRows));
        Dictionary<string, EvolutionTargetSnapshot> nodes = Field<Dictionary<string, EvolutionTargetSnapshot>>(dialog, _memberNodes);
        Dictionary<string, HashSet<string>> neighbors = Field<Dictionary<string, HashSet<string>>>(dialog, _memberNeighbors);
        for (int index = 0; index < 6; index++)
        {
            string id = _nodePrefix + index;
            nodes[id] = new() { SpeciesId = id, SpeciesName = id, StageLevel = index * 8 };
            neighbors[id] = [];
            if (index == 0)
                continue;

            neighbors[id].Add(_nodePrefix + (index - 1));
            neighbors[_nodePrefix + (index - 1)].Add(id);
        }

        SetField(dialog, _memberFocusedSpeciesId, _nodePrefix + 0);
        SetField(dialog, _memberExpansionDepth, 2);
        Invoke(dialog, _memberToggleGraphScope, all);
        Invoke(dialog, _memberToggleRowMode, single);
        Assert.Equal(all, Field<bool>(dialog, _memberShowAllLoaded));
        Assert.Equal(single, Field<bool>(dialog, _memberCompactRows));
        var visible = (IReadOnlyList<EvolutionTargetSnapshot>)typeof(EvolutionGraphDialog).GetProperty(_memberGraphNodes, _members)!.GetValue(dialog)!;
        Assert.Equal(all ? 6 : 3, visible.Count);
    }

    /// <summary>
    /// Verifies range labels avoid card attachment columns and routes retain hidden endpoints and full methods.
    /// </summary>
    /// <param name="single">Whether the graph shows one range per level.</param>
    /// <param name="perRow">The configured maximum number of cards per row.</param>
    /// <returns>A task representing production layout generation.</returns>
    [Theory]
    [InlineData(false, 5)]
    [InlineData(false, 6)]
    [InlineData(true, 5)]
    [InlineData(true, 6)]
    public async Task DenseRangesKeepAttachmentGapsAndCompleteRoutes(bool single, int perRow)
    {
        EvolutionTargetSnapshot[] nodes = [.. Enumerable.Range(0, 150).Select(index => new EvolutionTargetSnapshot
        {
            SpeciesId = _nodePrefix + index,
            SpeciesName = _nodePrefix + index,
            StageLevel = index / 25,
            BaseStatTotal = 200 + index
        })];
        EvolutionGraphEdge[] edges = [.. nodes.Skip(1).Select(node => new EvolutionGraphEdge(nodes[0].SpeciesId, node.SpeciesId, [_method], EvolutionCandidateSide.Head))];
        EvolutionGraph graph = new();
        ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(EvolutionGraph.Nodes)] = nodes,
            [nameof(EvolutionGraph.Edges)] = edges,
            [nameof(EvolutionGraph.Current)] = nodes[0],
            [nameof(EvolutionGraph.CompactRows)] = single,
            [nameof(EvolutionGraph.NodesPerRow)] = perRow,
            [nameof(EvolutionGraph.BranchSelectionEnabled)] = true
        }).SetParameterProperties(graph);
        typeof(EvolutionGraph).GetProperty(_memberText, _members)!.SetValue(graph, new GraphLocalizer());
        await (Task)Invoke(graph, _memberOnParametersSetAsync)!;
        var positions = Field<Dictionary<string, (double X, double Y)>>(graph, _memberPositions);
        Assert.Equal(single ? perRow * 6 : 150, positions.Count);
        IEnumerable<(double X, double Y)> labels = single
            ? Field<Dictionary<(int Level, int PageIndex), (double X, double Y)>>(graph, _memberRangePositions).Values
            : Field<IReadOnlyList<(int Level, int PageIndex, double X, double Y)>>(graph, _memberExpandedRangeLabels).Select(label => (label.X, label.Y));
        foreach ((double x, double y) in labels)
        {
            double nextRow = positions.Values.Where(position => position.Y > y).Min(position => position.Y);
            foreach (var position in positions.Values.Where(position => position.Y == nextRow))
                Assert.True(Math.Abs(x + 62 - (position.X + 71)) >= 62, "A range label crossed a card attachment column.");
        }

        var routes = (IReadOnlyList<EvolutionGraphEdge>)Invoke(graph, _memberGetFocusedRoutes, false)!;
        Assert.Equal(149, routes.Count);
        Assert.All(routes, route => Assert.Equal(_method, Assert.Single(route.EffectiveMethods)));
        if (single)
            Assert.Contains(routes, route => !positions.ContainsKey(route.TargetSpeciesId));
    }

    /// <summary>
    /// Keeps the selected species or manually browsed range visible when loading changes preceding BST pages.
    /// </summary>
    /// <param name="removeEarlierNodes">Whether a narrower neighborhood removes earlier pages instead of inserting new ones.</param>
    /// <param name="manualRange">Whether the user has deliberately browsed away from the focused Pokemon.</param>
    /// <returns>A task representing successive production layout updates.</returns>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RepartitioningRetainsSelectedRangeMembers(bool removeEarlierNodes, bool manualRange)
    {
        EvolutionTargetSnapshot[] nodes = [.. Enumerable.Range(0, 40).Select(index => new EvolutionTargetSnapshot
        {
            SpeciesId = _nodePrefix + index,
            SpeciesName = _nodePrefix + index,
            StageLevel = 1,
            BaseStatTotal = 300 + index * 5
        })];
        EvolutionGraph graph = new();
        ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(EvolutionGraph.Nodes)] = nodes,
            [nameof(EvolutionGraph.Current)] = nodes[0],
            [nameof(EvolutionGraph.CompactRows)] = true,
            [nameof(EvolutionGraph.NodesPerRow)] = 5
        }).SetParameterProperties(graph);
        typeof(EvolutionGraph).GetProperty(_memberText, _members)!.SetValue(graph, new GraphLocalizer());
        await (Task)Invoke(graph, _memberOnParametersSetAsync)!;
        ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(EvolutionGraph.FocusedSpeciesId)] = nodes[17].SpeciesId }).SetParameterProperties(graph);
        await (Task)Invoke(graph, _memberOnParametersSetAsync)!;
        if (manualRange)
            await (Task)Invoke(graph, _memberSelectRangeAsync, 1, 5)!;

        string retainedId = nodes[manualRange ? 25 : 17].SpeciesId;
        Assert.Contains(retainedId, Field<Dictionary<string, (double X, double Y)>>(graph, _memberPositions).Keys);
        EvolutionTargetSnapshot[] changedNodes = removeEarlierNodes
            ? [.. nodes.Skip(10)]
            : [.. nodes, .. Enumerable.Range(1, manualRange ? 10 : 3).Select(index => new EvolutionTargetSnapshot
            {
                SpeciesId = _nodePrefix + -index,
                SpeciesName = _nodePrefix + -index,
                StageLevel = 1,
                BaseStatTotal = 300 - index * 5
            })];
        ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(EvolutionGraph.Nodes)] = changedNodes }).SetParameterProperties(graph);
        await (Task)Invoke(graph, _memberOnParametersSetAsync)!;
        var visible = Field<Dictionary<string, (double X, double Y)>>(graph, _memberPositions);
        Assert.Contains(retainedId, visible.Keys);
        if (manualRange)
            Assert.DoesNotContain(nodes[17].SpeciesId, visible.Keys);
    }

    /// <summary>
    /// Reads component state for a behavior assertion.
    /// </summary>
    /// <typeparam name="T">The field value type.</typeparam>
    /// <param name="instance">The production component.</param>
    /// <param name="name">The private field name.</param>
    /// <returns>The current field value.</returns>
    private static T Field<T>(object instance, string name)
        => (T)instance.GetType().GetField(name, _members)!.GetValue(instance)!;

    /// <summary>
    /// Seeds a detached component without opening a game connection.
    /// </summary>
    /// <param name="instance">The component being prepared.</param>
    /// <param name="name">The field to seed.</param>
    /// <param name="value">The fixture value.</param>
    private static void SetField(object instance, string name, object value)
        => instance.GetType().GetField(name, _members)!.SetValue(instance, value);

    /// <summary>
    /// Invokes a production display interaction without desktop startup.
    /// </summary>
    /// <param name="instance">The component under test.</param>
    /// <param name="name">The interaction method.</param>
    /// <param name="arguments">The interaction inputs.</param>
    /// <returns>The interaction result.</returns>
    private static object? Invoke(object instance, string name, params object[] arguments)
        => instance.GetType().GetMethod(name, _members)!.Invoke(instance, arguments);

    /// <summary>
    /// Resolves the production graph labels in detached layout tests.
    /// </summary>
    private sealed class GraphLocalizer : IStringLocalizer<TrackerResources>
    {
        private readonly ResourceManager _manager = new(_resources, typeof(TrackerResources).Assembly);

        /// <summary>
        /// Gets a localized graph label.
        /// </summary>
        public LocalizedString this[string name] => new(name, _manager.GetString(name) ?? name);

        /// <summary>
        /// Gets a formatted graph label.
        /// </summary>
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(this[name].Value, arguments));

        /// <summary>
        /// Returns the unused enumeration contract for key-based tests.
        /// </summary>
        /// <param name="includeParentCultures">Whether parent resources should be included.</param>
        /// <returns>An empty resource sequence.</returns>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
