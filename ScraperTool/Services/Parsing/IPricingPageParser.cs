using HtmlAgilityPack;

using ScraperTool.Models;

namespace ScraperTool.Services.Parsing;

/// <summary>
/// Strategy interface for parsing pricing data from a fetched page.
/// Implementations handle different page layouts (HTML tables, div grids, plain text, etc.).
/// Follows Open/Closed Principle — add new parsers without modifying existing code.
/// </summary>
public interface IPricingPageParser
{
    /// <summary>
    /// Determines whether this parser can handle the given HTML document structure.
    /// </summary>
    bool CanParse(HtmlNode root, ScraperConfiguration config);

    /// <summary>
    /// Parses pricing data from the HTML node.
    /// </summary>
    ParseResult Parse(HtmlNode root, ScraperConfiguration config);
}
