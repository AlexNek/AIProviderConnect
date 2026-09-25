using System.Text.Json;

using ScraperTool.Models;

using WebTools.NET.Abstractions;
using WebTools.NET.Models;

namespace ScraperTool.Services.Validation.Checks;

/// <summary>
/// The website-content read behind <see cref="IServiceRetirementProbe"/>.
/// </summary>
public sealed class ServiceRetirementProbe : IServiceRetirementProbe
{
    /// <summary>
    /// Phrases that unambiguously indicate a service has been retired or discontinued.
    /// At least two distinct phrases must appear to trigger a positive detection.
    /// </summary>
    private static readonly string[] RetirementSignalPhrases =
    [
        "has been retired",
        "has been discontinued",
        "has been sunset",
        "no longer available",
        "service has ended",
        "end of life",
        "no longer supported",
        "service is no longer",
        "been fully retired",
        "shutting down"
    ];

    private readonly IWebContentFetcher _fetcher;

    public ServiceRetirementProbe(IWebContentFetcher fetcher)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
    }

    /// <inheritdoc />
    public async Task CheckServiceRetiredAsync(
        JsonElement root,
        string fileName,
        IValidationIssueSink sink,
        IReadOnlyList<string> urlFields,
        CancellationToken ct)
    {
        var website = root.TryGetProperty(ProviderJsonFields.Website, out var webProp)
                       && webProp.ValueKind == JsonValueKind.String
                           ? webProp.GetString()
                           : null;

        if (string.IsNullOrWhiteSpace(website))
            return;

        if (!Uri.TryCreate(website, UriKind.Absolute, out var uri))
            return;

        try
        {
            var result = await _fetcher.FetchAsAsync(website, EContentFormat.Markdown, ct: ct);
            if (result is null || !result.Success || string.IsNullOrWhiteSpace(result.Content))
                return;

            var contentLower = result.Content.ToLowerInvariant();
            var matchedSignals = RetirementSignalPhrases
                .Where(phrase => contentLower.Contains(phrase))
                .ToList();

            // Require at least 2 matching signals to reduce false positives.
            if (matchedSignals.Count < 2)
                return;

            var signalSummary = string.Join(", ", matchedSignals.Select(s => $"\"{s}\""));
            var providerName = root.TryGetProperty(ProviderJsonFields.DisplayName, out var nameProp)
                               && nameProp.ValueKind == JsonValueKind.String
                                   ? nameProp.GetString()
                                   : fileName;

            sink.Report(
                fileName,
                ProviderJsonFields.Website,
                website,
                ValidationStage.CheckFailed,
                $"Service retired (signals: {signalSummary})");

            // Add a ServiceRetired issue for each URL field so the AI fix phase
            // processes each one and can suggest "-" (not applicable).
            foreach (var field in urlFields)
            {
                var fieldValue = root.TryGetProperty(field, out var fProp)
                                 && fProp.ValueKind == JsonValueKind.String
                                     ? fProp.GetString()
                                     : null;

                var hasValue = !string.IsNullOrWhiteSpace(fieldValue)
                               && fieldValue != ProviderJsonFields.NotApplicable;

                var message = hasValue
                    ? $"Service '{providerName}' appears retired/deprecated — field '{field}' should be set to {ProviderJsonFields.NotApplicable} (signals: {signalSummary})"
                    : $"Service '{providerName}' appears retired/deprecated — set '{field}' to {ProviderJsonFields.NotApplicable} (signals: {signalSummary})";

                sink.Append(
                    new ValidationIssue(
                        fileName,
                        ValidationIssueCodes.ServiceRetired,
                        message)
                    {
                        Field = field,
                        CurrentValue = fieldValue ?? ""
                    });
            }

            sink.Report(
                fileName,
                ProviderJsonFields.Website,
                website,
                ValidationStage.CheckFailed,
                $"Service retired — {urlFields.Count} field(s) flagged for AI fix");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Retirement check is best-effort — don't fail validation on fetch errors.
        }
    }
}
