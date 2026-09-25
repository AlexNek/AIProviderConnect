using ScraperTool.Models;

namespace ScraperTool.Services.Parsing;

/// <summary>
/// Validates scraped prices for sanity:
/// - Prices should be in a reasonable range for AI model pricing (per 1M tokens)
/// - Both prompt and completion prices should ideally be present
/// - Extremely high prices are likely parsing errors (picked up TPS, token counts, etc.)
/// - Output price should generally be >= input price
/// </summary>
public sealed class PriceResultValidator : IPriceResultValidator
{
    // If price exceeds this, it's almost certainly a parse error
    private const decimal AlmostCertainlyWrongPrice = 500m;

    private const decimal MaxReasonablePrice = 200m;

    // Reasonable bounds for per-1M-token pricing (as of 2026)
    // Cheapest models: ~$0.01/1M, most expensive: ~$100/1M
    private const decimal MinReasonablePrice = 0.001m;

    public ValidationResult Validate(IReadOnlyList<ProviderPriceResult> results)
    {
        var valid = new List<ProviderPriceResult>();
        var issues = new List<PriceValidationIssue>();

        foreach (var entry in results)
        {
            var issue = ValidateEntry(entry);
            if (issue is null)
            {
                valid.Add(entry);
            }
            else if (issue.Severity == PriceIssueSeverity.Warning)
            {
                valid.Add(entry); // Include but flag
                issues.Add(issue);
            }
            else
            {
                issues.Add(issue); // Exclude
            }
        }

        return new ValidationResult(valid, issues);
    }

    private static PriceValidationIssue MakeIssue(
        ProviderPriceResult entry,
        string reason,
        PriceIssueSeverity severity) =>
        new() { Entry = entry, Reason = reason, Severity = severity };

    private static PriceValidationIssue? ValidateEntry(ProviderPriceResult entry)
    {
        var prompt = entry.PromptPrice;
        var completion = entry.CompletionPrice;

        // Both prices missing — should not happen (parser already filters this)
        if (prompt is null && completion is null)
            return MakeIssue(
                entry,
                "Both prompt and completion prices are missing.",
                PriceIssueSeverity.Error);

        // Extremely high price — almost certainly parsed wrong value (TPS, token count, etc.)
        if (prompt > AlmostCertainlyWrongPrice)
            return MakeIssue(
                entry,
                $"Prompt price ${prompt:N2}/1M is unreasonably high — likely a parsing error.",
                PriceIssueSeverity.Error);

        if (completion > AlmostCertainlyWrongPrice)
            return MakeIssue(
                entry,
                $"Completion price ${completion:N2}/1M is unreasonably high — likely a parsing error.",
                PriceIssueSeverity.Error);

        // One price missing — suspicious but possible (some providers only list input)
        if (prompt is null && completion is not null)
            return MakeIssue(
                entry,
                "Prompt price is missing — may indicate incorrect column mapping.",
                PriceIssueSeverity.Warning);

        if (prompt is not null && completion is null)
            return MakeIssue(
                entry,
                "Completion price is missing — may indicate incorrect column mapping.",
                PriceIssueSeverity.Warning);

        // Both present — check relative sanity
        if (prompt is not null && completion is not null)
        {
            // Completion price more than 20x prompt is unusual
            if (completion > prompt * 20 && prompt > 0)
                return MakeIssue(
                    entry,
                    $"Completion/prompt ratio is {completion / prompt:N1}x — unusually high.",
                    PriceIssueSeverity.Warning);

            // Prompt much higher than completion (> 10x) is very unusual
            if (prompt > completion * 10 && completion > 0)
                return MakeIssue(
                    entry,
                    $"Prompt price is {prompt / completion:N1}x higher than completion — prices may be swapped.",
                    PriceIssueSeverity.Warning);
        }

        return null;
    }
}
