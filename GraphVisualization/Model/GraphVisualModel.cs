namespace GraphVisualization.Model;

/// <summary>
/// A complete render-ready directed graph: styled nodes and edges plus the
/// start node used for layout rooting and emphasis.
/// </summary>
public sealed record GraphVisualModel(
    string Name,
    string? Title,
    IReadOnlyList<NodeVisual> Nodes,
    IReadOnlyList<EdgeVisual> Edges,
    string StartNodeId);
