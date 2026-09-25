namespace GraphVisualization.Styling;

/// <summary>Which side of the source node an edge leaves from.</summary>
public enum EEdgeExitSide
{
    /// <summary>Viewer chooses: bottom for downward edges, right for same-layer/back edges.</summary>
    Auto,

    /// <summary>Edge always leaves the source's bottom side (primary branch, e.g. a condition's "yes").</summary>
    Bottom,

    /// <summary>Edge always leaves the source's right side (secondary branch, e.g. a condition's "no").</summary>
    Right
}
