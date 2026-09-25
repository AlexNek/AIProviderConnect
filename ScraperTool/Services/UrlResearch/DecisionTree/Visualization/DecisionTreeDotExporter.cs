using System.Text;
using System.Text.Json;

using AiCleverness.Abstractions;

using DecisionTreeModel = AiCleverness.Models.DecisionTree.DecisionTree;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Visualization;

/// <summary>
/// Generates GraphViz DOT representation of a decision tree for visualization.
/// Node shapes: action=box, question=diamond, terminal=box(rounded), condition=diamond.
/// </summary>
public sealed class DecisionTreeDotExporter
{
    private readonly IDecisionTreeLoader _treeLoader;

    public DecisionTreeDotExporter(IDecisionTreeLoader treeLoader)
    {
        _treeLoader = treeLoader ?? throw new ArgumentNullException(nameof(treeLoader));
    }

    /// <summary>
    /// Exports a decision tree JSON string to DOT format.
    /// </summary>
    public string ExportToJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var tree = _treeLoader.Load(json);
        return ExportToDot(tree);
    }

    /// <summary>
    /// Exports a loaded decision tree to DOT format.
    /// </summary>
    public string ExportToDot(DecisionTreeModel tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var sb = new StringBuilder();
        sb.AppendLine($"digraph \"{EscapeDot(tree.TreeId)}\" {{");
        sb.AppendLine("    rankdir=TB;");
        sb.AppendLine("    node [fontname=\"Helvetica\", fontsize=10];");
        sb.AppendLine("    edge [fontname=\"Helvetica\", fontsize=8];");
        sb.AppendLine();

        // Title
        sb.AppendLine($"    labelloc=\"t\";");
        sb.AppendLine($"    label=\"{EscapeDot(tree.TreeId)} (v{tree.Version})\";");
        sb.AppendLine();

        // Nodes
        foreach (var (nodeId, node) in tree.Nodes)
        {
            var shape = GetNodeShape(node.Type.ToString());
            var label = BuildNodeLabel(nodeId, node, HasRetrySelfLoop(nodeId, node));
            var color = GetNodeColor(node.Type.ToString());

            sb.AppendLine($"    \"{EscapeDot(nodeId)}\" [shape={shape}, label=\"{label}\", style=filled, fillcolor=\"{color}\"];");
        }

        sb.AppendLine();

        // Edges
        foreach (var (nodeId, node) in tree.Nodes)
        {
            if (node.Transitions is null)
                continue;

            foreach (var transition in node.Transitions)
            {
                // transientFailure self-loops are retry mechanisms, not
                // decision branches — omit them from the diagram.
                if (IsRetrySelfLoop(nodeId, transition))
                    continue;

                var edgeLabel = EscapeDot(transition.Condition);
                var edgeColor = GetEdgeColor(transition.Condition);
                sb.AppendLine($"    \"{EscapeDot(nodeId)}\" -> \"{EscapeDot(transition.NextNodeId)}\" [label=\"{edgeLabel}\", color=\"{edgeColor}\"];");
            }
        }

        // Highlight start node
        sb.AppendLine();
        sb.AppendLine($"    \"{EscapeDot(tree.StartNodeId)}\" [penwidth=3.0, color=\"darkblue\"];");

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GetNodeShape(string nodeType)
    {
        return nodeType switch
        {
            "Action" => "box",
            "Question" => "hexagon",
            "Terminal" => "box",     // rounded via style
            "Condition" => "diamond",
            _ => "ellipse"
        };
    }

    private static string GetNodeColor(string nodeType)
    {
        return nodeType switch
        {
            "Action" => "#E8F4FD",       // light blue
            "Question" => "#FFF3CD",     // light yellow
            "Terminal" => "#D4EDDA",     // light green
            "Condition" => "#F8D7DA",    // light pink
            _ => "#FFFFFF"
        };
    }

    private static string GetEdgeColor(string condition)
    {
        return condition.ToLowerInvariant() switch
        {
            "success" or "true" => "#28A745",       // green
            "transientfailure" => "#FFC107",         // yellow/amber
            "permanentfailure" or "false" => "#DC3545", // red
            "unknown" => "#6C757D",                  // gray
            _ => "#007BFF"                            // blue (default for answer branches)
        };
    }

    private static string BuildNodeLabel(string nodeId, AiCleverness.Models.DecisionTree.DecisionNode node, bool hasRetry)
    {
        var type = node.Type.ToString().ToUpperInvariant();
        var typeLabel = hasRetry ? $"{type} \u21BB retry" : type;
        var detail = node.Type.ToString() switch
        {
            "Action" => node.ActionKey ?? "?",
            "Classify" => Truncate(node.Task ?? "?", 40),
            "Terminal" => node.Verdict ?? "?",
            "Condition" => node.PredicateKey ?? "?",
            _ => "?"
        };

        return $"{EscapeDot(nodeId)}\\n[{typeLabel}]\\n{EscapeDot(detail)}";
    }

    private static string EscapeDot(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "");
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        return text[..maxLength] + "…";
    }

    /// <summary>Returns true when the node has a transientFailure self-loop.</summary>
    private static bool HasRetrySelfLoop(string nodeId, AiCleverness.Models.DecisionTree.DecisionNode node)
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
    private static bool IsRetrySelfLoop(string nodeId, AiCleverness.Models.DecisionTree.DecisionTransition transition)
    {
        return transition.Condition.Equals("transientFailure", StringComparison.OrdinalIgnoreCase)
               && string.Equals(transition.NextNodeId, nodeId, StringComparison.OrdinalIgnoreCase);
    }
}
