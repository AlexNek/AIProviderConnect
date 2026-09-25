using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

using FluentAssertions;

using Moq;

using ScraperTool.Visualization;

using DecisionTreeModel = AiCleverness.Models.DecisionTree.DecisionTree;

namespace ScraperTool.Tests;

public class DecisionTreeDotExporterTests
{
    private static DecisionTreeModel MakeTree(
        string treeId = "test-tree",
        int version = 1,
        string startNodeId = "root")
    {
        return new DecisionTreeModel
        {
            TreeId = treeId,
            Version = version,
            StartNodeId = startNodeId,
            Nodes = new Dictionary<string, DecisionNode>()
        };
    }

    [Fact]
    public void ExportToDot_EmptyTree_ContainsDigraphHeader()
    {
        var mockLoader = new Mock<IDecisionTreeLoader>();
        var exporter = new DecisionTreeDotExporter(mockLoader.Object);
        var tree = MakeTree();

        var dot = exporter.ExportToDot(tree);

        dot.Should().Contain("digraph \"test-tree\"");
        dot.Should().Contain("rankdir=TB");
        dot.Should().Contain("}");
    }

    [Fact]
    public void ExportToDot_WithActionNode_ContainsBoxShape()
    {
        var mockLoader = new Mock<IDecisionTreeLoader>();
        var exporter = new DecisionTreeDotExporter(mockLoader.Object);
        var tree = MakeTree();
        tree = tree with
        {
            Nodes = new Dictionary<string, DecisionNode>
            {
                ["node1"] = new DecisionNode
                {
                    Type = EDecisionNodeType.Action,
                    ActionKey = "fetchPricing"
                }
            }
        };

        var dot = exporter.ExportToDot(tree);

        dot.Should().Contain("shape=box");
        dot.Should().Contain("fetchPricing");
        dot.Should().Contain("ACTION");
    }

    [Fact]
    public void ExportToDot_WithQuestionNode_ContainsHexagonShape()
    {
        var mockLoader = new Mock<IDecisionTreeLoader>();
        var exporter = new DecisionTreeDotExporter(mockLoader.Object);
        var tree = MakeTree();
        tree = tree with
        {
            Nodes = new Dictionary<string, DecisionNode>
            {
                ["q1"] = new DecisionNode
                {
                    Type = EDecisionNodeType.Classify,
                    Task = "What pricing model?"
                }
            }
        };

        var dot = exporter.ExportToDot(tree);

        dot.Should().Contain("shape=hexagon");
        dot.Should().Contain("CLASSIFY");
    }

    [Fact]
    public void ExportToDot_WithTerminalNode_ContainsGreenColor()
    {
        var mockLoader = new Mock<IDecisionTreeLoader>();
        var exporter = new DecisionTreeDotExporter(mockLoader.Object);
        var tree = MakeTree();
        tree = tree with
        {
            Nodes = new Dictionary<string, DecisionNode>
            {
                ["end1"] = new DecisionNode
                {
                    Type = EDecisionNodeType.Terminal,
                    Verdict = "winner"
                }
            }
        };

        var dot = exporter.ExportToDot(tree);

        dot.Should().Contain("#D4EDDA"); // green fill
        dot.Should().Contain("TERMINAL");
    }

    [Fact]
    public void ExportToDot_WithConditionNode_ContainsDiamondShape()
    {
        var mockLoader = new Mock<IDecisionTreeLoader>();
        var exporter = new DecisionTreeDotExporter(mockLoader.Object);
        var tree = MakeTree();
        tree = tree with
        {
            Nodes = new Dictionary<string, DecisionNode>
            {
                ["cond1"] = new DecisionNode
                {
                    Type = EDecisionNodeType.Condition,
                    PredicateKey = "hasCandidates"
                }
            }
        };

        var dot = exporter.ExportToDot(tree);

        dot.Should().Contain("shape=diamond");
        dot.Should().Contain("hasCandidates");
    }

    [Fact]
    public void ExportToDot_WithTransitions_ContainsEdges()
    {
        var mockLoader = new Mock<IDecisionTreeLoader>();
        var exporter = new DecisionTreeDotExporter(mockLoader.Object);
        var tree = MakeTree();
        tree = tree with
        {
            Nodes = new Dictionary<string, DecisionNode>
            {
                ["root"] = new DecisionNode
                {
                    Type = EDecisionNodeType.Condition,
                    PredicateKey = "hasCandidates",
                    Transitions = new List<DecisionTransition>
                    {
                        new() { Condition = "true", NextNodeId = "end-ok" },
                        new() { Condition = "false", NextNodeId = "end-fail" }
                    }
                },
                ["end-ok"] = new DecisionNode { Type = EDecisionNodeType.Terminal, Verdict = "ok" },
                ["end-fail"] = new DecisionNode { Type = EDecisionNodeType.Terminal, Verdict = "fail" }
            }
        };

        var dot = exporter.ExportToDot(tree);

        dot.Should().Contain("\"root\" -> \"end-ok\"");
        dot.Should().Contain("\"root\" -> \"end-fail\"");
        dot.Should().Contain("label=\"true\"");
        dot.Should().Contain("label=\"false\"");
    }

    [Fact]
    public void ExportToDot_StartNode_HasHighlight()
    {
        var mockLoader = new Mock<IDecisionTreeLoader>();
        var exporter = new DecisionTreeDotExporter(mockLoader.Object);
        var tree = MakeTree(startNodeId: "my-start");
        tree = tree with
        {
            Nodes = new Dictionary<string, DecisionNode>
            {
                ["my-start"] = new DecisionNode { Type = EDecisionNodeType.Action, ActionKey = "test" }
            }
        };

        var dot = exporter.ExportToDot(tree);

        dot.Should().Contain("\"my-start\" [penwidth=3.0");
    }

    [Fact]
    public void ExportToDot_VersionIncluded_InTitle()
    {
        var mockLoader = new Mock<IDecisionTreeLoader>();
        var exporter = new DecisionTreeDotExporter(mockLoader.Object);
        var tree = MakeTree(version: 3);

        var dot = exporter.ExportToDot(tree);

        dot.Should().Contain("label=\"test-tree (v3)\"");
    }

    [Fact]
    public void ExportToJson_DelegatesToLoader()
    {
        var tree = MakeTree();
        var mockLoader = new Mock<IDecisionTreeLoader>();
        mockLoader.Setup(l => l.Load(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(tree);

        var exporter = new DecisionTreeDotExporter(mockLoader.Object);
        var dot = exporter.ExportToJson("{}");

        dot.Should().Contain("digraph");
        mockLoader.Verify(l => l.Load("{}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ExportToDot_EscapesSpecialCharacters()
    {
        var mockLoader = new Mock<IDecisionTreeLoader>();
        var exporter = new DecisionTreeDotExporter(mockLoader.Object);
        var tree = MakeTree(treeId: "tree\"with\"quotes");
        tree = tree with
        {
            Nodes = new Dictionary<string, DecisionNode>
            {
                ["n1"] = new DecisionNode
                {
                    Type = EDecisionNodeType.Terminal,
                    Verdict = "line1\nline2"
                }
            }
        };

        var dot = exporter.ExportToDot(tree);

        dot.Should().Contain("\\\"");
        dot.Should().Contain("\\n");
    }

    [Fact]
    public void ExportToDot_SuccessEdge_IsGreen()
    {
        var mockLoader = new Mock<IDecisionTreeLoader>();
        var exporter = new DecisionTreeDotExporter(mockLoader.Object);
        var tree = MakeTree();
        tree = tree with
        {
            Nodes = new Dictionary<string, DecisionNode>
            {
                ["root"] = new DecisionNode
                {
                    Type = EDecisionNodeType.Action,
                    ActionKey = "test",
                    Transitions = new List<DecisionTransition>
                    {
                        new() { Condition = "success", NextNodeId = "end" }
                    }
                },
                ["end"] = new DecisionNode { Type = EDecisionNodeType.Terminal, Verdict = "ok" }
            }
        };

        var dot = exporter.ExportToDot(tree);

        dot.Should().Contain("color=\"#28A745\"");
    }

    [Fact]
    public void ExportToDot_TransientFailureSelfLoop_IsOmitted()
    {
        var mockLoader = new Mock<IDecisionTreeLoader>();
        var exporter = new DecisionTreeDotExporter(mockLoader.Object);
        var tree = MakeTree();
        tree = tree with
        {
            Nodes = new Dictionary<string, DecisionNode>
            {
                ["fetch"] = new DecisionNode
                {
                    Type = EDecisionNodeType.Action,
                    ActionKey = "fetchPage",
                    Transitions = new List<DecisionTransition>
                    {
                        new() { Condition = "success", NextNodeId = "end" },
                        new() { Condition = "transientFailure", NextNodeId = "fetch" },
                        new() { Condition = "permanentFailure", NextNodeId = "end" }
                    }
                },
                ["end"] = new DecisionNode { Type = EDecisionNodeType.Terminal, Verdict = "ok" }
            }
        };

        var dot = exporter.ExportToDot(tree);

        // success and permanentFailure edges are present
        dot.Should().Contain("\"fetch\" -> \"end\"");
        dot.Should().Contain("label=\"success\"");
        dot.Should().Contain("label=\"permanentFailure\"");
        // transientFailure self-loop edge is omitted
        dot.Should().NotContain("label=\"transientFailure\"");
        // but the node shows a retry badge
        dot.Should().Contain("\u21BB retry");
    }
}
