using System.Text.Json;

namespace ScraperTool.Services.Validation.Checks;

/// <summary>
/// Resolves the fields that cannot apply to a self-hosted provider, filing the structural finding
/// for each one that carries an address anyway.
/// </summary>
public interface ISelfHostedApplicabilityEvaluator
{
    /// <summary>
    /// Runs the three applicability checks in the order the validator has always run them and
    /// returns what the URL loop has to honour.
    /// </summary>
    SelfHostedApplicabilityFlags Evaluate(
        JsonElement root,
        string fileName,
        IValidationIssueSink sink);
}
