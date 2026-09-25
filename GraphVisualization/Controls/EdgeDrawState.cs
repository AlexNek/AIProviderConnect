using System.Windows.Controls;
using System.Windows.Shapes;

using GraphVisualization.Layout;
using GraphVisualization.Styling;

namespace GraphVisualization.Controls;

/// <summary>View state for one drawn edge so node drags can re-route it without a full rebuild.</summary>
internal sealed class EdgeDrawState
{
    public required RoutedEdge Edge { get; init; }

    public required EEdgeExitSide ExitSide { get; init; }

    public double SourceAnchor { get; init; }

    public double TargetAnchor { get; init; }

    public ENodeShape SourceShape { get; init; }

    public ENodeShape TargetShape { get; init; }

    public required Path Path { get; init; }

    public required Polygon Arrow { get; init; }

    public required TextBlock Label { get; init; }
}
