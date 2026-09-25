using System.Text;

using AiCleverness.Models.DecisionTree;

using GraphVisualization.Model;
using GraphVisualization.Styling;

using DecisionTreeModel = AiCleverness.Models.DecisionTree.DecisionTree;

namespace ScraperTool.Visualization;

/// <summary>
/// Projects an AiCleverness decision tree into the generic
/// <see cref="GraphVisualModel"/> rendered by GraphVisualization.
/// </summary>
public static class DecisionTreeVisualBuilder
{
    public static GraphVisualModel Build(DecisionTreeModel tree, int detailMaxLength = 26)
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
                Title: GetTitle(nodeId, node),
                Subtitle: subtitle,
                Detail: GetDetail(node, detailMaxLength),
                Style: GetNodeStyle(node.Type, node.Verdict)));

            if (node.Transitions is null)
                continue;

            foreach (var transition in node.Transitions)
            {
                // transientFailure self-loops are retry mechanisms, not
                // decision branches — omit them from the diagram.
                if (IsRetrySelfLoop(nodeId, transition))
                    continue;

                edges.Add(new EdgeVisual(nodeId, transition.NextNodeId, transition.Condition, GetEdgeColorHex(transition.Condition), GetExitSide(node.Type, transition.Condition)));
            }
        }

        var graphTitle = !string.IsNullOrWhiteSpace(tree.Name)
            ? $"{tree.Name} (v{tree.Version})"
            : $"{tree.TreeId} (v{tree.Version})";

        return new GraphVisualModel(
            Name: tree.TreeId,
            Title: graphTitle,
            nodes,
            edges,
            tree.StartNodeId);
    }

    private static string GetTitle(string nodeId, DecisionNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.Name))
            return node.Name!;

        var name = node.Type switch
        {
            EDecisionNodeType.Action => node.ActionKey ?? nodeId,
            EDecisionNodeType.Condition => node.PredicateKey ?? nodeId,
            _ => nodeId
        };

        return Humanize(name);
    }

    /// <summary>Turns kebab-case/camelCase identifiers into a readable label ("hasCandidates" → "Has candidates").</summary>
    private static string Humanize(string identifier)
    {
        var words = new List<string>();
        var current = new StringBuilder();

        foreach (var c in identifier)
        {
            if (c is '-' or '_' or ' ')
            {
                Flush();
            }
            else if (char.IsUpper(c))
            {
                Flush();
                current.Append(char.ToLowerInvariant(c));
            }
            else
            {
                current.Append(c);
            }
        }

        Flush();

        if (words.Count == 0)
            return identifier;

        words[0] = char.ToUpperInvariant(words[0][0]) + words[0][1..];
        return string.Join(' ', words);

        void Flush()
        {
            if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }
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

    private static string GetDetail(DecisionNode node, int maxLength)
    {
        if (!string.IsNullOrWhiteSpace(node.Description))
        {
            var desc = node.Description!;
            return desc.Length <= maxLength ? desc : desc[..maxLength] + "…";
        }

        var detail = node.Type switch
        {
            EDecisionNodeType.Action => node.ActionKey ?? "?",
            EDecisionNodeType.Classify => node.Task ?? "?",
            EDecisionNodeType.Terminal => node.Verdict ?? "?",
            EDecisionNodeType.Condition => node.PredicateKey ?? "?",
            _ => "?"
        };

        return detail.Length <= maxLength ? detail : detail[..maxLength] + "…";
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
                _ => new NodeStyle(ENodeShape.RoundedBox, "#E2E3E5", "#6C757D")
            },
            _ => new NodeStyle(ENodeShape.RoundedBox, "#FFFFFF", "#666666")
        };
    }

    /// <summary>Condition branches follow flowchart convention: "yes" leaves the bottom, "no" the right side.</summary>
    private static EEdgeExitSide GetExitSide(EDecisionNodeType type, string condition)
    {
        if (type != EDecisionNodeType.Condition)
            return EEdgeExitSide.Auto;

        return condition.Equals("true", StringComparison.OrdinalIgnoreCase)
            ? EEdgeExitSide.Bottom
            : EEdgeExitSide.Right;
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
}
