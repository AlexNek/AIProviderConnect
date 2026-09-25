using System.Text;

using GraphVisualization.Model;
using GraphVisualization.Styling;

namespace GraphVisualization;

/// <summary>
/// Generates a GraphViz DOT representation of any <see cref="GraphVisualModel"/>.
/// Node shapes: RoundedBox=box, Diamond=diamond, Hexagon=hexagon.
/// </summary>
public sealed class GraphDotExporter
{
    public string ExportToDot(GraphVisualModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var sb = new StringBuilder();
        sb.AppendLine($"digraph \"{Escape(model.Name)}\" {{");
        sb.AppendLine("    rankdir=TB;");
        sb.AppendLine("    node [fontname=\"Helvetica\", fontsize=10];");
        sb.AppendLine("    edge [fontname=\"Helvetica\", fontsize=8];");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(model.Title))
        {
            sb.AppendLine("    labelloc=\"t\";");
            sb.AppendLine($"    label=\"{Escape(model.Title)}\";");
            sb.AppendLine();
        }

        foreach (var node in model.Nodes)
        {
            var shape = GetDotShape(node.Style.Shape);
            var label = BuildNodeLabel(node);
            sb.AppendLine($"    \"{Escape(node.Id)}\" [shape={shape}, label=\"{label}\", style=filled, fillcolor=\"{node.Style.FillHex}\"];");
        }

        sb.AppendLine();

        foreach (var edge in model.Edges)
        {
            sb.AppendLine($"    \"{Escape(edge.From)}\" -> \"{Escape(edge.To)}\" [label=\"{Escape(edge.Label)}\", color=\"{edge.ColorHex}\"];");
        }

        sb.AppendLine();
        sb.AppendLine($"    \"{Escape(model.StartNodeId)}\" [penwidth=3.0, color=\"darkblue\"];");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GetDotShape(ENodeShape shape)
    {
        return shape switch
        {
            ENodeShape.Diamond => "diamond",
            ENodeShape.Hexagon => "hexagon",
            _ => "box"
        };
    }

    private static string BuildNodeLabel(NodeVisual node)
    {
        var parts = new[] { node.Title, node.Subtitle, node.Detail }
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(Escape);
        return string.Join("\\n", parts);
    }

    private static string Escape(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "");
    }
}
