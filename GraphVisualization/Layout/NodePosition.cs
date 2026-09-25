namespace GraphVisualization.Layout;

/// <summary>Center position of a laid-out node, plus the layer it was placed on.</summary>
public sealed record NodePosition(double X, double Y, int Layer);
