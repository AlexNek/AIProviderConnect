namespace GraphVisualization.Layout;

/// <summary>An edge annotated by the layout engine; back-edges close cycles and render differently.</summary>
public sealed record RoutedEdge(string From, string To, string Label, bool IsBackEdge);
