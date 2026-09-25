namespace GraphVisualization.Layout;

/// <summary>Output of the layout engine: node positions, routed edges, and content size.</summary>
public sealed record GraphLayoutResult(
    IReadOnlyDictionary<string, NodePosition> Nodes,
    IReadOnlyList<RoutedEdge> Edges,
    double Width,
    double Height);
