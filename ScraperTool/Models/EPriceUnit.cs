namespace ScraperTool.Models;

/// <summary>
/// Represents the unit of measurement for AI model pricing.
/// Replaces magic strings like "Per1M", "Per1K", "PerToken".
/// </summary>
public enum EPriceUnit
{
    /// <summary>
    /// Price per single token.
    /// </summary>
    PerToken,

    /// <summary>
    /// Price per 1,000 tokens.
    /// </summary>
    Per1K,

    /// <summary>
    /// Price per 1,000,000 tokens (most common for modern LLMs).
    /// </summary>
    Per1M
}
