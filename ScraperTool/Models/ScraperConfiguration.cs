namespace ScraperTool.Models;

public sealed class ScraperConfiguration
{
    public string? ApiPricingUrl { get; set; }

    public int CompletionCellIndex { get; set; } = 2;

    public int ModelCellIndex { get; set; }

    public EPriceUnit PriceUnit { get; set; } = EPriceUnit.Per1M;

    public int PromptCellIndex { get; set; } = 1;

    public string ProviderId { get; set; } = string.Empty;

    public int RowOffset { get; set; }

    public string? TableXPath { get; set; }
}
