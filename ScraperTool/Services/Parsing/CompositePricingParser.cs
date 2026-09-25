using HtmlAgilityPack;

using ScraperTool.Models;

namespace ScraperTool.Services.Parsing;

/// <summary>
/// Orchestrates the parsing pipeline:
/// 1. Tries standard parsers in order (HTML table → div layout)
/// 2. If all fail and AI is available, asks AI to analyze the layout
/// 3. Validates results — rejects entries with unreasonable prices
/// </summary>
public sealed class CompositePricingParser
{
    private readonly IAiLayoutAnalyzer? _aiAnalyzer;

    private readonly IReadOnlyList<IPricingPageParser> _parsers;

    private readonly TextContentPricingParser _textParser;

    private readonly IPriceResultValidator _validator;

    public CompositePricingParser(
        IAiLayoutAnalyzer? aiAnalyzer = null,
        IEnumerable<IPricingPageParser>? parsers = null,
        IPriceResultValidator? validator = null)
    {
        _parsers = parsers?.ToList() ?? DefaultParsers();
        _textParser = new TextContentPricingParser();
        _validator = validator ?? new PriceResultValidator();
        _aiAnalyzer = aiAnalyzer;
    }

    /// <summary>
    /// Tries each HTML-aware parser in order, then optionally AI-guided parsing.
    /// Validates all results before returning.
    /// </summary>
    public ParseResult Parse(HtmlNode root, ScraperConfiguration config)
    {
        // Try standard parsers first
        foreach (var parser in _parsers)
        {
            if (parser.CanParse(root, config))
            {
                var result = parser.Parse(root, config);
                if (result.Success)
                {
                    var validated = ValidateAndAnnotate(result);
                    if (validated.Success)
                        return validated;
                }
            }
        }

        return ParseResult.Empty("No parser could extract prices from the HTML structure.");
    }

    /// <summary>
    /// Parses pricing from plain text content (e.g. Playwright-rendered text).
    /// </summary>
    public ParseResult ParseText(string textContent, ScraperConfiguration config)
    {
        var result = _textParser.ParseText(textContent, config);
        if (!result.Success)
            return result;

        return ValidateAndAnnotate(result);
    }

    /// <summary>
    /// AI-assisted parsing: asks AI to analyze the page, then parses with AI-determined selectors.
    /// Call this when standard parsing fails.
    /// </summary>
    public async Task<ParseResult> ParseWithAiAsync(
        HtmlNode root,
        string rawHtml,
        ScraperConfiguration config,
        CancellationToken ct = default)
    {
        if (_aiAnalyzer is null)
            return ParseResult.Empty("AI analyzer not configured.");

        var layout = await _aiAnalyzer.AnalyzeAsync(rawHtml, config.ProviderId, ct);
        if (layout is null || layout.Confidence < 0.4)
            return ParseResult.Empty(
                layout is null
                    ? "AI could not analyze the page layout."
                    : $"AI confidence too low ({layout.Confidence:P0}): {layout.Reasoning}");

        var aiParser = new AiGuidedPricingParser(layout);
        if (!aiParser.CanParse(root, config))
            return ParseResult.Empty(
                $"AI selectors didn't match any rows. Row selector: {layout.RowSelector}");

        var result = aiParser.Parse(root, config);
        if (!result.Success)
            return result;

        return ValidateAndAnnotate(result);
    }

    private static List<IPricingPageParser> DefaultParsers() =>
        [
            new HtmlTablePricingParser(),
            new DivLayoutPricingParser(),
        ];

    /// <summary>
    /// Validates parsed results and annotates the diagnostic with any issues.
    /// </summary>
    private ParseResult ValidateAndAnnotate(ParseResult raw)
    {
        var validation = _validator.Validate(raw.Prices.ToList());

        var diagnostic = raw.Diagnostic;
        if (validation.Issues.Count > 0)
        {
            var errors = validation.Issues.Count(i => i.Severity == PriceIssueSeverity.Error);
            var warnings = validation.Issues.Count(i => i.Severity == PriceIssueSeverity.Warning);
            diagnostic += $" Validation: {errors} rejected, {warnings} warnings.";

            foreach (var issue in validation.Issues
                         .Where(i => i.Severity == PriceIssueSeverity.Error).Take(3))
            {
                diagnostic += $" [{issue.Entry.ModelDisplayName}: {issue.Reason}]";
            }
        }

        return new ParseResult(validation.Valid, diagnostic);
    }
}
