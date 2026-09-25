namespace GraphVisualization.Layout;

/// <summary>
/// Hierarchical (Sugiyama-style) layout: DFS back-edge detection, longest-path
/// layering, barycenter crossing reduction, and centered row positioning.
/// Pure geometry — no UI dependencies.
/// </summary>
public sealed class HierarchicalGraphLayoutEngine : IGraphLayoutEngine
{
    public GraphLayoutResult Layout(
        IReadOnlyList<LayoutNode> nodes,
        IReadOnlyList<LayoutEdge> edges,
        string startNodeId,
        LayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentException.ThrowIfNullOrWhiteSpace(startNodeId);

        var opt = options ?? new LayoutOptions();

        var ids = nodes
            .Select(n => n.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var idSet = new HashSet<string>(ids, StringComparer.Ordinal);
        var validEdges = edges
            .Where(e => idSet.Contains(e.From) && idSet.Contains(e.To))
            .ToList();

        var forward = new List<LayoutEdge>();
        var back = new List<LayoutEdge>();
        ClassifyEdges(ids, validEdges, startNodeId, forward, back);

        var layer = ids.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var order = ids.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        AssignLayers(layer, forward);
        AssignOrder(ids, layer, order, forward);

        var (positions, width, height) = AssignPositions(ids, layer, order, opt);

        var routed = forward
            .Select(e => new RoutedEdge(e.From, e.To, e.Label, false))
            .Concat(back.Select(e => new RoutedEdge(e.From, e.To, e.Label, true)))
            .ToList();

        return new GraphLayoutResult(positions, routed, width, height);
    }

    /// <summary>DFS from the start node; edges to nodes still on the stack are back-edges.</summary>
    private static void ClassifyEdges(
        IReadOnlyList<string> ids,
        List<LayoutEdge> edges,
        string startNodeId,
        List<LayoutEdge> forward,
        List<LayoutEdge> back)
    {
        // 0 = unvisited, 1 = visiting, 2 = done
        var state = ids.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var outEdges = edges
            .GroupBy(e => e.From, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        void Visit(string id)
        {
            state[id] = 1;
            if (outEdges.TryGetValue(id, out var list))
            {
                foreach (var edge in list)
                {
                    if (state[edge.To] == 1)
                    {
                        back.Add(edge);
                    }
                    else
                    {
                        forward.Add(edge);
                        if (state[edge.To] == 0)
                            Visit(edge.To);
                    }
                }
            }

            state[id] = 2;
        }

        if (state.ContainsKey(startNodeId))
            Visit(startNodeId);

        // Unreachable leftovers still get forward edges
        foreach (var id in ids)
        {
            if (state[id] == 0)
                Visit(id);
        }
    }

    /// <summary>Longest-path layering over forward edges (Kahn-style relaxation).</summary>
    private static void AssignLayers(
        IDictionary<string, int> layer,
        List<LayoutEdge> forward)
    {
        // Relax until stable (forward edges are acyclic; graphs are small)
        for (var pass = 0; pass < layer.Count + 1; pass++)
        {
            var changed = false;
            foreach (var edge in forward)
            {
                if (layer[edge.To] < layer[edge.From] + 1)
                {
                    layer[edge.To] = layer[edge.From] + 1;
                    changed = true;
                }
            }

            if (!changed)
                break;
        }
    }

    /// <summary>Barycenter ordering within each layer to reduce edge crossings.</summary>
    private static void AssignOrder(
        IReadOnlyList<string> ids,
        IDictionary<string, int> layer,
        IDictionary<string, int> order,
        List<LayoutEdge> forward)
    {
        var layers = ids
            .GroupBy(id => layer[id])
            .OrderBy(g => g.Key)
            .Select(g => g.ToList())
            .ToList();

        // Initial order: alphabetical for determinism
        foreach (var row in layers)
        {
            var i = 0;
            foreach (var id in row.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                order[id] = i++;
        }

        for (var sweep = 0; sweep < 4; sweep++)
        {
            // Down sweep: order by mean of parent orders
            foreach (var row in layers.Skip(1))
            {
                foreach (var id in row)
                {
                    var parents = forward
                        .Where(e => e.To == id)
                        .Select(e => order[e.From])
                        .ToList();
                    if (parents.Count > 0)
                        order[id] = (int)(parents.Average() * 1000);
                }

                Renormalize(row, order);
            }

            // Up sweep: order by mean of child orders
            for (var li = layers.Count - 2; li >= 0; li--)
            {
                foreach (var id in layers[li])
                {
                    var children = forward
                        .Where(e => e.From == id)
                        .Select(e => order[e.To])
                        .ToList();
                    if (children.Count > 0)
                        order[id] = (int)(children.Average() * 1000);
                }

                Renormalize(layers[li], order);
            }
        }
    }

    private static void Renormalize(List<string> row, IDictionary<string, int> order)
    {
        var sorted = row.OrderBy(id => order[id]).ToList();
        for (var i = 0; i < sorted.Count; i++)
            order[sorted[i]] = i;
    }

    private static (Dictionary<string, NodePosition> Positions, double Width, double Height) AssignPositions(
        IReadOnlyList<string> ids,
        IDictionary<string, int> layer,
        IDictionary<string, int> order,
        LayoutOptions opt)
    {
        var layers = ids
            .GroupBy(id => layer[id])
            .OrderBy(g => g.Key)
            .ToList();

        var maxRowWidth = 0.0;
        foreach (var row in layers)
        {
            var count = row.Count();
            var rowWidth = count * opt.NodeWidth + (count - 1) * opt.HorizontalGap;
            if (rowWidth > maxRowWidth)
                maxRowWidth = rowWidth;
        }

        var positions = new Dictionary<string, NodePosition>(StringComparer.Ordinal);
        foreach (var row in layers)
        {
            var ordered = row.OrderBy(id => order[id]).ToList();
            var rowWidth = ordered.Count * opt.NodeWidth + (ordered.Count - 1) * opt.HorizontalGap;
            var offsetX = (maxRowWidth - rowWidth) / 2 + opt.NodeWidth / 2 + opt.Margin;

            for (var i = 0; i < ordered.Count; i++)
            {
                positions[ordered[i]] = new NodePosition(
                    offsetX + i * (opt.NodeWidth + opt.HorizontalGap),
                    row.Key * (opt.NodeHeight + opt.VerticalGap) + opt.NodeHeight / 2 + opt.Margin,
                    row.Key);
            }
        }

        var width = maxRowWidth + 2 * opt.Margin;
        var height = (layers.Count == 0 ? 1 : layers.Max(l => l.Key) + 1) * (opt.NodeHeight + opt.VerticalGap) + 2 * opt.Margin;
        return (positions, width, height);
    }
}
