using GraphVisualization.Styling;

namespace GraphVisualization.Model;

/// <summary>A fully styled, render-ready graph node; domain-agnostic.</summary>
public sealed record NodeVisual(
    string Id,
    string Title,
    string Subtitle,
    string Detail,
    NodeStyle Style);
