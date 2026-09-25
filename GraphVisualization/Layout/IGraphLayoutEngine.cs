namespace GraphVisualization.Layout;

/// <summary>Positions the nodes and edges of a directed graph in 2D space.</summary>
public interface IGraphLayoutEngine
{
    GraphLayoutResult Layout(
        IReadOnlyList<LayoutNode> nodes,
        IReadOnlyList<LayoutEdge> edges,
        string startNodeId,
        LayoutOptions? options = null);
}
