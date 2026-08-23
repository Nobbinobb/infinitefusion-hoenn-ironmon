namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Builds expanded and compact layouts for the generated evolution graph.
/// </summary>
public partial class EvolutionGraph
{
    /// <summary>
    /// Builds centered, width-bounded physical rows inside stable logical level bands.
    /// </summary>
    /// <param name="selectFocusRanges">Whether ranges should be selected around the current focus.</param>
    private void BuildLayout(bool selectFocusRanges)
    {
        _positions.Clear();
        _rangePositions.Clear();
        _portalCounts.Clear();
        int nodesPerRow = Math.Clamp(NodesPerRow, EvolutionGraphSettings.MinimumNodesPerRow, EvolutionGraphSettings.MaximumNodesPerRow);
        int[] orderedLevels = [.. _orderedNodes.Select(node => node.StageLevel).Distinct().Order()];
        BuildLevelPages(orderedLevels, nodesPerRow);
        SelectActivePages(orderedLevels, selectFocusRanges);
        int widestRow = _levelPages.Values.SelectMany(pages => pages).Select(page => page.Count).DefaultIfEmpty(1).Max();
        _canvasWidth = Math.Max(_canvasMinimumWidth, (_canvasPadding * 2) + (widestRow * _nodeWidth) + (Math.Max(0, widestRow - 1) * _nodeGap));
        if (!CompactRows)
            _canvasWidth += _rangeButtonWidth + _rangeButtonGap;

        if (CompactRows)
        {
            BuildCompactLayout(orderedLevels, widestRow);
        }
        else
        {
            BuildExpandedLayout(orderedLevels);
        }

        BuildVisibleConnections();
        double contentBottom = _levelBands.Count == 0 ? 0 : _levelBands[^1].Top + _levelBands[^1].Height;
        _canvasHeight = Math.Max(_canvasMinimumHeight, contentBottom + _canvasPadding);
        string focusSpeciesId = FocusedSpeciesId ?? Current.SpeciesId;
        if (_positions.TryGetValue(focusSpeciesId, out (double X, double Y) position))
        {
            _focusX = position.X + (_nodeWidth / 2);
            _focusY = position.Y + (_nodeHeight / 2);
        }
        else
        {
            _focusX = _canvasWidth / 2;
            _focusY = _canvasHeight / 2;
        }
    }

    /// <summary>
    /// Partitions every logical level into stable BST-ordered ranges.
    /// </summary>
    /// <param name="orderedLevels">The stable logical levels.</param>
    /// <param name="nodesPerRow">The maximum number of nodes in one range.</param>
    private void BuildLevelPages(IReadOnlyList<int> orderedLevels, int nodesPerRow)
    {
        _levelPages.Clear();
        _nodeLookup.Clear();
        _nodePageIndexes.Clear();
        _rangeLabels.Clear();
        foreach (int level in orderedLevels)
        {
            EvolutionTargetSnapshot[] levelNodes = [.. _orderedNodes.Where(node => node.StageLevel == level)];
            IReadOnlyList<IReadOnlyList<EvolutionTargetSnapshot>> pages = [.. levelNodes.Chunk(nodesPerRow).Select(page => (IReadOnlyList<EvolutionTargetSnapshot>)page)];
            _levelPages[level] = pages;
            (int Minimum, int Maximum)[] ranges = [.. pages.Select(page => (page.Min(node => node.BaseStatTotal), page.Max(node => node.BaseStatTotal)))];
            Dictionary<(int Minimum, int Maximum), int> duplicateTotals = ranges.GroupBy(range => range).ToDictionary(group => group.Key, group => group.Count());
            Dictionary<(int Minimum, int Maximum), int> duplicateIndexes = [];
            for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                foreach (EvolutionTargetSnapshot node in pages[pageIndex])
                {
                    _nodeLookup[node.SpeciesId] = node;
                    _nodePageIndexes[node.SpeciesId] = (level, pageIndex);
                }

                (int minimum, int maximum) = ranges[pageIndex];
                string label = minimum == maximum ? Text["Lookup.Graph.BstValue", minimum] : Text["Lookup.Graph.BstRange", minimum, maximum];
                int duplicateCount = duplicateTotals[(minimum, maximum)];
                if (duplicateCount > 1)
                {
                    int duplicateIndex = duplicateIndexes.GetValueOrDefault((minimum, maximum)) + 1;
                    duplicateIndexes[(minimum, maximum)] = duplicateIndex;
                    label = Text["Lookup.Graph.DuplicateBstRange", label, duplicateIndex, duplicateCount];
                }

                _rangeLabels[(level, pageIndex)] = label;
            }
        }

        foreach (int removedLevel in _activePageIndexes.Keys.Except(orderedLevels).ToArray())
            _activePageIndexes.Remove(removedLevel);
    }

    /// <summary>
    /// Selects valid compact ranges, preferring the focused node and its direct connections.
    /// </summary>
    /// <param name="orderedLevels">The stable logical levels.</param>
    /// <param name="selectFocusRanges">Whether selection should follow a changed focus or display mode.</param>
    private void SelectActivePages(IReadOnlyList<int> orderedLevels, bool selectFocusRanges)
    {
        string focusSpeciesId = FocusedSpeciesId ?? Current.SpeciesId;
        HashSet<string> connectedSpeciesIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (EvolutionGraphEdge edge in Edges)
        {
            if (edge.SourceSpeciesId.Equals(focusSpeciesId, StringComparison.OrdinalIgnoreCase))
            {
                connectedSpeciesIds.Add(edge.TargetSpeciesId);
            }
            else if (edge.TargetSpeciesId.Equals(focusSpeciesId, StringComparison.OrdinalIgnoreCase))
            {
                connectedSpeciesIds.Add(edge.SourceSpeciesId);
            }
        }

        foreach (int level in orderedLevels)
        {
            IReadOnlyList<IReadOnlyList<EvolutionTargetSnapshot>> pages = _levelPages[level];
            int focusPageIndex = FindPageIndex(level, focusSpeciesId);
            if (selectFocusRanges && focusPageIndex >= 0)
            {
                _activePageIndexes[level] = focusPageIndex;
                continue;
            }

            if (!selectFocusRanges && _activePageIndexes.TryGetValue(level, out int retainedPageIndex) && retainedPageIndex >= 0 && retainedPageIndex < pages.Count)
                continue;

            if (focusPageIndex >= 0)
            {
                _activePageIndexes[level] = focusPageIndex;
                continue;
            }

            int bestPageIndex = 0;
            int bestConnectionCount = -1;
            for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                int connectionCount = pages[pageIndex].Count(node => connectedSpeciesIds.Contains(node.SpeciesId));
                if (connectionCount > bestConnectionCount)
                {
                    bestPageIndex = pageIndex;
                    bestConnectionCount = connectionCount;
                }
            }

            _activePageIndexes[level] = bestPageIndex;
        }
    }

    /// <summary>
    /// Positions every BST range inside its logical level band.
    /// </summary>
    /// <param name="orderedLevels">The stable logical levels.</param>
    private void BuildExpandedLayout(IReadOnlyList<int> orderedLevels)
    {
        List<(int Level, double Top, double Height)> levelBands = [];
        List<(int Level, int PageIndex, double X, double Y)> rangeLabels = [];
        double nextNodeY = _canvasPadding;
        foreach (int level in orderedLevels)
        {
            IReadOnlyList<IReadOnlyList<EvolutionTargetSnapshot>> pages = _levelPages[level];
            int physicalRowCount = pages.Count;
            double bandTop = nextNodeY - _levelBandPadding;
            double bandHeight = (_levelBandPadding * 2) + (physicalRowCount * _nodeHeight) + (Math.Max(0, physicalRowCount - 1) * _rowGap);
            levelBands.Add((level, bandTop, bandHeight));
            for (int physicalRowIndex = 0; physicalRowIndex < physicalRowCount; physicalRowIndex++)
            {
                IReadOnlyList<EvolutionTargetSnapshot> row = pages[physicalRowIndex];
                double rowWidth = (row.Count * _nodeWidth) + (Math.Max(0, row.Count - 1) * _nodeGap);
                double contentStartX = _rangeButtonWidth + _rangeButtonGap;
                double startX = contentStartX + ((_canvasWidth - contentStartX - rowWidth) / 2);
                double y = nextNodeY + (physicalRowIndex * (_nodeHeight + _rowGap));
                for (int index = 0; index < row.Count; index++)
                    _positions[row[index].SpeciesId] = (startX + (index * (_nodeWidth + _nodeGap)), y);

                rangeLabels.Add((level, physicalRowIndex, 10, y + ((_nodeHeight - _rangeButtonHeight) / 2)));
            }

            nextNodeY = bandTop + bandHeight + _levelBandGap + _levelBandPadding;
        }

        _levelBands = levelBands;
        _expandedRangeLabels = rangeLabels;
        _visibleNodes = _orderedNodes;
    }

    /// <summary>
    /// Positions one selected BST range and every clickable range portal per logical level.
    /// </summary>
    /// <param name="orderedLevels">The stable logical levels.</param>
    /// <param name="nodeSlotCapacity">The number of virtual Pokemon slots available across the canvas.</param>
    private void BuildCompactLayout(IReadOnlyList<int> orderedLevels, int nodeSlotCapacity)
    {
        List<(int Level, double Top, double Height)> levelBands = [];
        List<EvolutionTargetSnapshot> visibleNodes = [];
        double nextBandTop = _canvasPadding / 2;
        int buttonsPerRow = Math.Max(1, nodeSlotCapacity - 1);
        foreach (int level in orderedLevels)
        {
            IReadOnlyList<IReadOnlyList<EvolutionTargetSnapshot>> pages = _levelPages[level];
            int activePageIndex = _activePageIndexes[level];
            int selectorRowCount = (int)Math.Ceiling(pages.Count / (double)buttonsPerRow);
            double selectorHeight = (selectorRowCount * _rangeButtonHeight) + (Math.Max(0, selectorRowCount - 1) * _rangeButtonGap);
            double nodeY = nextBandTop + _levelBandPadding + selectorHeight + _rangeSelectorGap;
            double bandHeight = (_levelBandPadding * 2) + selectorHeight + _rangeSelectorGap + _nodeHeight;
            levelBands.Add((level, nextBandTop, bandHeight));
            IReadOnlyList<EvolutionTargetSnapshot> activePage = pages[activePageIndex];
            double rowWidth = (activePage.Count * _nodeWidth) + (Math.Max(0, activePage.Count - 1) * _nodeGap);
            double startX = (_canvasWidth - rowWidth) / 2;
            for (int index = 0; index < activePage.Count; index++)
                _positions[activePage[index].SpeciesId] = (startX + (index * (_nodeWidth + _nodeGap)), nodeY);

            for (int selectorRowIndex = 0; selectorRowIndex < selectorRowCount; selectorRowIndex++)
            {
                int firstPageIndex = selectorRowIndex * buttonsPerRow;
                int rowPageCount = Math.Min(buttonsPerRow, pages.Count - firstPageIndex);
                double[] gapCenters = GetCenteredGapPositions(activePage.Count, rowPageCount, selectorRowIndex);
                double selectorY = nextBandTop + _levelBandPadding + (selectorRowIndex * (_rangeButtonHeight + _rangeButtonGap));
                for (int rangeIndex = 0; rangeIndex < rowPageCount; rangeIndex++)
                {
                    int pageIndex = firstPageIndex + rangeIndex;
                    _rangePositions[(level, pageIndex)] = (gapCenters[rangeIndex] - (_rangeButtonWidth / 2), selectorY);
                }
            }

            visibleNodes.AddRange(activePage);
            nextBandTop += bandHeight + _levelBandGap;
        }

        _levelBands = levelBands;
        _expandedRangeLabels = [];
        _visibleNodes = visibleNodes;
    }

    /// <summary>
    /// Places selector centers on the gap lattice defined by the displayed Pokemon row.
    /// </summary>
    /// <param name="nodeCount">The number of displayed Pokemon slots.</param>
    /// <param name="chipCount">The number of range controls in this selector row.</param>
    /// <param name="selectorRowIndex">The selector row used to alternate unavoidable odd offsets.</param>
    /// <returns>The ordered horizontal chip centers.</returns>
    private double[] GetCenteredGapPositions(int nodeCount, int chipCount, int selectorRowIndex)
    {
        double pitch = _nodeWidth + _nodeGap;
        List<double> offsets = [];
        if (nodeCount % 2 == 0)
        {
            if (chipCount % 2 == 1)
                offsets.Add(0);

            for (int pairIndex = 1; offsets.Count < chipCount; pairIndex++)
            {
                offsets.Add(-pairIndex * pitch);
                if (offsets.Count < chipCount)
                    offsets.Add(pairIndex * pitch);
            }
        }
        else
        {
            int pairCount = chipCount / 2;
            for (int pairIndex = 0; pairIndex < pairCount; pairIndex++)
            {
                double offset = (pairIndex + 0.5) * pitch;
                offsets.Add(-offset);
                offsets.Add(offset);
            }

            if (chipCount % 2 == 1)
            {
                double extraOffset = (pairCount + 0.5) * pitch;
                offsets.Add(selectorRowIndex % 2 == 0 ? extraOffset : -extraOffset);
            }
        }

        double centerX = _canvasWidth / 2;
        return [.. offsets.Select(offset => centerX + offset).Order()];
    }

    /// <summary>
    /// Separates complete visible edges from compact connections represented by range portals.
    /// </summary>
    private void BuildVisibleConnections()
    {
        HashSet<string> visibleSpeciesIds = new(_visibleNodes.Select(node => node.SpeciesId), StringComparer.OrdinalIgnoreCase);
        List<EvolutionGraphEdge> visibleEdges = [];
        Dictionary<(string VisibleSpeciesId, int HiddenLevel, int HiddenPageIndex, bool HiddenIsSource), (int Count, bool Active)> portalConnections = [];
        foreach (EvolutionGraphEdge edge in Edges)
        {
            bool sourceVisible = visibleSpeciesIds.Contains(edge.SourceSpeciesId);
            bool targetVisible = visibleSpeciesIds.Contains(edge.TargetSpeciesId);
            if (sourceVisible && targetVisible)
            {
                visibleEdges.Add(edge);
                continue;
            }

            if (!CompactRows || sourceVisible == targetVisible)
                continue;

            string visibleSpeciesId = sourceVisible ? edge.SourceSpeciesId : edge.TargetSpeciesId;
            string hiddenSpeciesId = sourceVisible ? edge.TargetSpeciesId : edge.SourceSpeciesId;
            if (!_nodeLookup.TryGetValue(hiddenSpeciesId, out EvolutionTargetSnapshot? hiddenNode))
                continue;

            int hiddenPageIndex = FindPageIndex(hiddenNode.StageLevel, hiddenSpeciesId);
            if (hiddenPageIndex < 0)
                continue;

            bool hiddenIsSource = !sourceVisible;
            (string VisibleSpeciesId, int HiddenLevel, int HiddenPageIndex, bool HiddenIsSource) key = (visibleSpeciesId, hiddenNode.StageLevel, hiddenPageIndex, hiddenIsSource);
            (int count, bool active) = portalConnections.GetValueOrDefault(key);
            portalConnections[key] = (count + 1, active || IsActive(edge));
            (int Level, int PageIndex) portalKey = (hiddenNode.StageLevel, hiddenPageIndex);
            _portalCounts[portalKey] = _portalCounts.GetValueOrDefault(portalKey) + 1;
        }

        _visibleEdges = visibleEdges;
        _portalConnections = [.. portalConnections.Select(pair => ($"{pair.Key.VisibleSpeciesId}|{pair.Key.HiddenLevel}|{pair.Key.HiddenPageIndex}|{pair.Key.HiddenIsSource}", pair.Key.VisibleSpeciesId, pair.Key.HiddenLevel, pair.Key.HiddenPageIndex, pair.Key.HiddenIsSource, pair.Value.Count, pair.Value.Active))];
    }

    /// <summary>
    /// Finds the BST range containing one species in a logical level.
    /// </summary>
    /// <param name="level">The logical graph level.</param>
    /// <param name="speciesId">The stable species identifier.</param>
    /// <returns>The zero-based range index, or minus one when absent.</returns>
    private int FindPageIndex(int level, string speciesId)
        => _nodePageIndexes.TryGetValue(speciesId, out (int Level, int PageIndex) location) && location.Level == level ? location.PageIndex : -1;
}
