namespace GraphVisualization.Layout;

/// <summary>Geometry knobs for the hierarchical layout.</summary>
public sealed record LayoutOptions(
    double NodeWidth = 190,
    double NodeHeight = 70,
    double HorizontalGap = 40,
    double VerticalGap = 70,
    double Margin = 40);
