using AiCleverness.Abstractions;

using GraphVisualization;

using DecisionTreeModel = AiCleverness.Models.DecisionTree.DecisionTree;

namespace ScraperTool.Visualization;

/// <summary>
/// Generates a GraphViz DOT representation of a decision tree by projecting it
/// into the generic graph model and delegating to <see cref="GraphDotExporter"/>.
/// </summary>
public sealed class DecisionTreeDotExporter
{
    private readonly IDecisionTreeLoader _treeLoader;

    private readonly GraphDotExporter _graphDotExporter = new();

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

        // DOT labels tolerate longer detail text than the WPF nodes
        var model = DecisionTreeVisualBuilder.Build(tree, detailMaxLength: 40);
        return _graphDotExporter.ExportToDot(model);
    }
}
