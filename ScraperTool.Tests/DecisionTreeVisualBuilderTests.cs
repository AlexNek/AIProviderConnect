using AiCleverness.Models.DecisionTree;

using FluentAssertions;

using GraphVisualization.Model;

using ScraperTool.Visualization;

using DecisionTreeModel = AiCleverness.Models.DecisionTree.DecisionTree;

namespace ScraperTool.Tests;

public class DecisionTreeVisualBuilderTests
{
    private static DecisionTreeModel MakeTree(
        string treeId = "test-tree",
        string? name = null,
        string? description = null,
        int version = 1,
        string startNodeId = "root")
    {
        return new DecisionTreeModel
        {
            TreeId = treeId,
            Name = name,
            Description = description,
            Version = version,
            StartNodeId = startNodeId,
            Nodes = new Dictionary<string, DecisionNode>()
        };
    }

    // ── Node title precedence ──────────────────────────────────

    [Fact]
    public void Build_NodeWithName_UsesNameAsTitle()
    {
        var tree = MakeTree();
        tree = tree with
        {
            Nodes = new Dictionary<string, DecisionNode>
            {
                ["n1"] = new DecisionNode
                {
                    Type = EDecisionNodeType.Action,
                    ActionKey = "fetchPricing",
                    Name = "Fetch Pricing"
                }
            }
        };

        var model = DecisionTreeVisualBuilder.Build(tree);

        model.Nodes.Should().ContainSingle(n => n.Id == "n1")
            .Which.Title.Should().Be("Fetch Pricing");
    }

    [Fact]
    public void Build_NodeWithoutName_FallsBackToHumanizedKey()
    {
        var tree = MakeTree();
        tree = tree with
        {
            Nodes = new Dictionary<string, DecisionNode>
            {
                ["n1"] = new DecisionNode
                {
                    Type = EDecisionNodeType.Action,
                    ActionKey = "fetchPricing"
                }
            }
        };

        var model = DecisionTreeVisualBuilder.Build(tree);

        model.Nodes.Should().ContainSingle(n => n.Id == "n1")
            .Which.Title.Should().Be("Fetch pricing");
    }

    [Fact]
    public void Build_NodeWithWhitespaceName_FallsBackToHumanizedKey()
    {
        var tree = MakeTree();
        tree = tree with
        {
            Nodes = new Dictionary<string, DecisionNode>
            {
                ["n1"] = new DecisionNode
                {
                    Type = EDecisionNodeType.Condition,
                    PredicateKey = "hasCandidates",
                    Name = "   "
                }
            }
        };

        var model = DecisionTreeVisualBuilder.Build(tree);

        model.Nodes.Should().ContainSingle(n => n.Id == "n1")
            .Which.Title.Should().Be("Has candidates");
    }

    [Fact]
    public void Build_TerminalNodeWithName_UsesNameAsTitle()
    {
        var tree = MakeTree();
        tree = tree with
        {
            Nodes = new Dictionary<string, DecisionNode>
            {
                ["end"] = new DecisionNode
                {
                    Type = EDecisionNodeType.Terminal,
                    Verdict = "winner",
                    Name = "Success"
                }
            }
        };

        var model = DecisionTreeVisualBuilder.Build(tree);

        model.Nodes.Should().ContainSingle(n => n.Id == "end")
            .Which.Title.Should().Be("Success");
    }

    // ── Node detail (description) precedence ──────────────────

    [Fact]
    public void Build_NodeWithDescription_UsesDescriptionAsDetail()
    {
        var tree = MakeTree();
        tree = tree with
        {
            Nodes = new Dictionary<string, DecisionNode>
            {
                ["n1"] = new DecisionNode
                {
                    Type = EDecisionNodeType.Action,
                    ActionKey = "fetchPricing",
                    Description = "Fetches pricing data"
                }
            }
        };

        var model = DecisionTreeVisualBuilder.Build(tree);

        model.Nodes.Should().ContainSingle(n => n.Id == "n1")
            .Which.Detail.Should().Be("Fetches pricing data");
    }

    [Fact]
    public void Build_NodeWithoutDescription_FallsBackToTechnicalDetail()
    {
        var tree = MakeTree();
        tree = tree with
        {
            Nodes = new Dictionary<string, DecisionNode>
            {
                ["n1"] = new DecisionNode
                {
                    Type = EDecisionNodeType.Action,
                    ActionKey = "fetchPricing"
                }
            }
        };

        var model = DecisionTreeVisualBuilder.Build(tree);

        model.Nodes.Should().ContainSingle(n => n.Id == "n1")
            .Which.Detail.Should().Be("fetchPricing");
    }

    [Fact]
    public void Build_NodeWithLongDescription_TruncatesWithEllipsis()
    {
        var tree = MakeTree();
        tree = tree with
        {
            Nodes = new Dictionary<string, DecisionNode>
            {
                ["n1"] = new DecisionNode
                {
                    Type = EDecisionNodeType.Action,
                    ActionKey = "test",
                    Description = "This is a very long description that exceeds the maximum allowed length"
                }
            }
        };

        var model = DecisionTreeVisualBuilder.Build(tree, detailMaxLength: 20);

        var node = model.Nodes.Should().ContainSingle(n => n.Id == "n1").Subject;
        node.Detail.Should().HaveLength(21); // 20 chars + ellipsis
        node.Detail.Should().EndWith("…");
    }

    // ── Graph title precedence ─────────────────────────────────

    [Fact]
    public void Build_TreeWithName_UsesNameInGraphTitle()
    {
        var tree = MakeTree(treeId: "apiPricingUrl", name: "API Pricing URL Discovery", version: 2);

        var model = DecisionTreeVisualBuilder.Build(tree);

        model.Title.Should().Be("API Pricing URL Discovery (v2)");
    }

    [Fact]
    public void Build_TreeWithoutName_FallsBackToTreeId()
    {
        var tree = MakeTree(treeId: "apiPricingUrl", version: 3);

        var model = DecisionTreeVisualBuilder.Build(tree);

        model.Title.Should().Be("apiPricingUrl (v3)");
    }

    [Fact]
    public void Build_TreeWithWhitespaceName_FallsBackToTreeId()
    {
        var tree = MakeTree(treeId: "my-tree", name: "  ");

        var model = DecisionTreeVisualBuilder.Build(tree);

        model.Title.Should().Be("my-tree (v1)");
    }

    [Fact]
    public void Build_TreeNameIsUsedAsGraphName()
    {
        var tree = MakeTree(treeId: "my-tree-id", name: "Display Name");

        var model = DecisionTreeVisualBuilder.Build(tree);

        // GraphVisualModel.Name stays as the technical TreeId (used for DOT digraph label)
        model.Name.Should().Be("my-tree-id");
    }
}
