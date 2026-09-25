namespace ScraperTool.Services.Parsing;

/// <summary>
/// Result of AI analysis of a pricing page's HTML structure.
/// Describes how to find model rows, model names, and price cells.
/// </summary>
public sealed class LayoutAnalysis
{
    /// <summary>Confidence score from 0.0 to 1.0.</summary>
    public double Confidence { get; set; }

    /// <summary>XPath or CSS selector for the input/prompt price within a row.</summary>
    public string InputPriceSelector { get; set; } = string.Empty;

    /// <summary>Whether the layout uses HTML tables or div-based structure.</summary>
    public string LayoutType { get; set; } = "unknown";

    /// <summary>XPath or CSS selector for the model name within a row.</summary>
    public string ModelNameSelector { get; set; } = string.Empty;

    /// <summary>XPath or CSS selector for the output/completion price within a row.</summary>
    public string OutputPriceSelector { get; set; } = string.Empty;

    /// <summary>AI's reasoning about the page structure.</summary>
    public string Reasoning { get; set; } = string.Empty;

    /// <summary>XPath or CSS selector for each pricing row/item.</summary>
    public string RowSelector { get; set; } = string.Empty;
}
