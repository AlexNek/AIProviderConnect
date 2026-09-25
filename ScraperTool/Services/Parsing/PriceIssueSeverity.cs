namespace ScraperTool.Services.Parsing;

public enum PriceIssueSeverity
{
    /// <summary>Likely wrong — excluded from results.</summary>
    Error,

    /// <summary>Suspicious but included with a flag.</summary>
    Warning
}
