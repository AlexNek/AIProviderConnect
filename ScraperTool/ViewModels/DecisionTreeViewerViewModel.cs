using System.IO;

using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GraphVisualization.Model;

using ScraperTool.Visualization;

using DecisionTreeModel = AiCleverness.Models.DecisionTree.DecisionTree;

namespace ScraperTool.ViewModels;

/// <summary>
/// ViewModel for the Decision Tree Viewer panel.
/// Loads decision-tree JSON files from Config/trees/, renders an interactive
/// graph of the tree, and keeps the raw JSON source / DOT export behind tabs.
/// </summary>
public sealed partial class DecisionTreeViewerViewModel : ObservableObject
{
    private readonly DecisionTreeDotExporter _dotExporter;

    private readonly IDecisionTreeLoader _treeLoader;

    private readonly string _treesDirectory;

    [ObservableProperty]
    private string _dotOutput = string.Empty;

    [ObservableProperty]
    private string _jsonSource = string.Empty;

    [ObservableProperty]
    private string _loadErrorMessage = string.Empty;

    [ObservableProperty]
    private DecisionTreeModel? _loadedTree;

    [ObservableProperty]
    private GraphVisualModel? _graphModel;

    [ObservableProperty]
    private IReadOnlyList<DecisionTreeEntry> _treeEntries = Array.Empty<DecisionTreeEntry>();

    [ObservableProperty]
    private string? _selectedTreeFileName;

    [ObservableProperty]
    private string _treeSummary = string.Empty;

    public DecisionTreeViewerViewModel(
        DecisionTreeDotExporter dotExporter,
        IDecisionTreeLoader treeLoader)
    {
        _dotExporter = dotExporter ?? throw new ArgumentNullException(nameof(dotExporter));
        _treeLoader = treeLoader ?? throw new ArgumentNullException(nameof(treeLoader));

        // Resolve Config/trees relative to the application base directory
        _treesDirectory = Path.Combine(AppContext.BaseDirectory, "Config", "trees");
        RefreshTreeList();
    }

    [RelayCommand]
    private void RefreshTreeList()
    {
        if (!Directory.Exists(_treesDirectory))
        {
            TreeEntries = Array.Empty<DecisionTreeEntry>();
            LoadErrorMessage = $"Trees directory not found: {_treesDirectory}";
            return;
        }

        var files = Directory.GetFiles(_treesDirectory, "*.json")
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var entries = new List<DecisionTreeEntry>();
        foreach (var fileName in files)
        {
            var displayName = ResolveTreeDisplayName(fileName);
            entries.Add(new DecisionTreeEntry(displayName, fileName));
        }

        TreeEntries = entries;
        LoadErrorMessage = string.Empty;

        // Auto-select the first tree if none is selected
        if (SelectedTreeFileName is null && entries.Count > 0)
        {
            SelectedTreeFileName = entries[0].FileName;
            LoadSelectedTree();
        }
    }

    private string ResolveTreeDisplayName(string fileName)
    {
        try
        {
            var filePath = Path.Combine(_treesDirectory, fileName);
            var json = File.ReadAllText(filePath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("name", out var nameProp)
                && !string.IsNullOrWhiteSpace(nameProp.GetString()))
            {
                return nameProp.GetString()!;
            }
        }
        catch
        {
            // Fall through to file-name fallback
        }

        return Path.GetFileNameWithoutExtension(fileName);
    }

    partial void OnSelectedTreeFileNameChanged(string? value)
    {
        if (value is not null)
        {
            LoadSelectedTree();
        }
    }

    private void LoadSelectedTree()
    {
        if (SelectedTreeFileName is null)
            return;

        var filePath = Path.Combine(_treesDirectory, SelectedTreeFileName);
        if (!File.Exists(filePath))
        {
            LoadErrorMessage = $"File not found: {filePath}";
            return;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            JsonSource = json;
            LoadErrorMessage = string.Empty;

            // Parse + validate the tree for the graph renderer
            LoadedTree = _treeLoader.Load(json);
            GraphModel = DecisionTreeVisualBuilder.Build(LoadedTree);

            // Generate DOT output
            DotOutput = _dotExporter.ExportToJson(json);

            // Build a brief summary using the loaded tree model
            TreeSummary = BuildTreeSummary(LoadedTree);
        }
        catch (Exception ex)
        {
            LoadErrorMessage = $"Failed to load tree: {ex.Message}";
            LoadedTree = null;
            GraphModel = null;
            DotOutput = string.Empty;
            TreeSummary = string.Empty;
        }
    }

    private static string BuildTreeSummary(DecisionTreeModel tree)
    {
        var displayName = !string.IsNullOrWhiteSpace(tree.Name) ? tree.Name : tree.TreeId;
        var nodeCount = tree.Nodes.Count;
        var actionCount = 0;
        var classifyCount = 0;
        var conditionCount = 0;
        var terminalCount = 0;

        foreach (var node in tree.Nodes.Values)
        {
            switch (node.Type)
            {
                case EDecisionNodeType.Action:
                    actionCount++;
                    break;
                case EDecisionNodeType.Classify:
                    classifyCount++;
                    break;
                case EDecisionNodeType.Condition:
                    conditionCount++;
                    break;
                case EDecisionNodeType.Terminal:
                    terminalCount++;
                    break;
            }
        }

        var stats = $"{displayName} v{tree.Version} | {nodeCount} nodes ({actionCount} action, {classifyCount} classify, {conditionCount} condition, {terminalCount} terminal) | start: {tree.StartNodeId}";

        if (!string.IsNullOrWhiteSpace(tree.Description))
        {
            return $"{stats}\n{tree.Description}";
        }

        return stats;
    }
}
