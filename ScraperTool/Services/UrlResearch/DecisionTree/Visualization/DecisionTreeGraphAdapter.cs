using AiCleverness.Models.DecisionTree;

using GraphVisualization.Model;
using GraphVisualization.Styling;

using DecisionTreeModel = AiCleverness.Models.DecisionTree.DecisionTree;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Visualization;

/// <summary>
/// Maps an AiCleverness decision tree onto the generic GraphVisualization model:
/// node shapes/colors per node type and verdict, edge colors per transition condition.
/// </summary>
public static class DecisionTreeGraphAdapter
{
    public static GraphVisualModel ToVisualModel(DecisionTreeModel tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var nodes = new List<NodeVisual>();
        var edges = new List<EdgeVisual>();

        foreach (var (nodeId, node) in tree.Nodes)
        {
            var hasRetry = HasRetrySelfLoop(nodeId, node);
            var subtitle = hasRetry
                ? $"[{node.Type.ToString().ToUpperInvariant()}] \u21BB retry"
                : $"[{node.Type.ToString().ToUpperInvariant()}]";

            nodes.Add(new NodeVisual(
                nodeId,
                nodeId,
                subtitle,
                GetDetail(node),
                GetNodeStyle(node.Type, node.Verdict)));

            if (node.Transitions is null)
                continue;

            foreach (var transition in node.Transitions)
            {
                // transientFailure self-loops are retry mechanisms, not
                // decision branches — omit them from the diagram.
                if (IsRetrySelfLoop(nodeId, transition))
                    continue;

                edges.Add(new EdgeVisual(
                    nodeId,
                    transition.NextNodeId,
                    transition.Condition,
                    GetEdgeColorHex(transition.Condition)));
            }
        }

        return new GraphVisualModel(
            tree.TreeId,
            $"{tree.TreeId} (v{tree.Version})",
            nodes,
            edges,
            tree.StartNodeId);
    }

    private static NodeStyle GetNodeStyle(EDecisionNodeType type, string? verdict)
    {
        return type switch
        {
            EDecisionNodeType.Action => new NodeStyle(ENodeShape.RoundedBox, "#E8F4FD", "#0078D4"),
            EDecisionNodeType.Classify => new NodeStyle(ENodeShape.Hexagon, "#FFF3CD", "#B8860B"),
            EDecisionNodeType.Condition => new NodeStyle(ENodeShape.Diamond, "#F8D7DA", "#DC3545"),
            EDecisionNodeType.Terminal => verdict switch
            {
                "winner" or "keep" => new NodeStyle(ENodeShape.RoundedBox, "#D4EDDA", "#28A745"),
                "update" => new NodeStyle(ENodeShape.RoundedBox, "#FFF3CD", "#FFC107"),
                _ => new NodeStyle(ENodeShape.RoundedBox, "#D4EDDA", "#28A745")
            },
            _ => new NodeStyle(ENodeShape.RoundedBox, "#FFFFFF", "#666666")
        };
    }

    private static string GetEdgeColorHex(string condition)
    {
        return condition.ToLowerInvariant() switch
        {
            "success" or "true" => "#28A745",
            "transientfailure" => "#FFC107",
            "permanentfailure" or "false" => "#DC3545",
            "unknown" => "#6C757D",
            _ => "#007BFF"
        };
    }

    private static string GetDetail(DecisionNode node)
    {
        return node.Type switch
        {
            EDecisionNodeType.Action => node.ActionKey ?? "?",
            EDecisionNodeType.Classify => Truncate(node.Task ?? "?", 26),
            EDecisionNodeType.Terminal => node.Verdict ?? "?",
            EDecisionNodeType.Condition => node.PredicateKey ?? "?",
            _ => "?"
        };
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;

        return text[..maxLength] + "…";
    }

    /// <summary>Returns true when the node has a transientFailure self-loop.</summary>
    private static bool HasRetrySelfLoop(string nodeId, DecisionNode node)
    {
        if (node.Transitions is null)
            return false;

        foreach (var transition in node.Transitions)
        {
            if (IsRetrySelfLoop(nodeId, transition))
                return true;
        }

        return false;
    }

    /// <summary>A retry self-loop is a transientFailure edge whose target is the same node.</summary>
    private static bool IsRetrySelfLoop(string nodeId, DecisionTransition transition)
    {
        return transition.Condition.Equals("transientFailure", StringComparison.OrdinalIgnoreCase)
               && string.Equals(transition.NextNodeId, nodeId, StringComparison.OrdinalIgnoreCase);
    }
}
