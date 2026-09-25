namespace ScraperTool.Services;

public enum ValidationStage
{
    CheckingUrl,

    CheckingContent,

    CheckingPricingContent,

    CheckPassed,

    CheckFailed
}
