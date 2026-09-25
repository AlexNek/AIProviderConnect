namespace GraphVisualization.Layout;

/// <summary>A directed edge between two layout nodes, with a display label.</summary>
public sealed record LayoutEdge(string From, string To, string Label);
