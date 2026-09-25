using GraphVisualization.Styling;

namespace GraphVisualization.Model;

/// <summary>A fully styled, render-ready graph edge; domain-agnostic.</summary>
public sealed record EdgeVisual(
    string From,
    string To,
    string Label,
    string ColorHex,
    EEdgeExitSide ExitSide = EEdgeExitSide.Auto);
