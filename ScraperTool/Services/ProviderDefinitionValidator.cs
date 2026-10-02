using System.IO;
using System.Text.Json;
using System.Windows.Threading;

using ScraperTool.Models;
using ScraperTool.Services.Validation;
using ScraperTool.Services.Validation.Checks;

namespace ScraperTool.Services;

public sealed class ProviderDefinitionValidator
{
    private const string SidecarLabel = "sidecar";

    internal static readonly string[] UrlFields =
        [
            ProviderJsonFields.Website, ProviderJsonFields.LoginUrl,
            ProviderJsonFields.ApiPricingUrl, ProviderJsonFields.SubscriptionPricingUrl,
            ProviderJsonFields.DocumentationUrl,
            ProviderJsonFields.BaseUrl
        ];

    private readonly IDuplicateIdChecker _duplicateIdChecker;

    private readonly IProviderManifestReader _manifestReader;

    private readonly IValidationMetadataService _metadataService;

    private readonly IProviderSchemaValidator _schemaValidator;

    private readonly IServiceRetirementProbe _retirementProbe;

    private readonly ISelfHostedApplicabilityEvaluator _selfHostedApplicability;

    private readonly ISubscriptionConfiguredChecker _subscriptionConfiguredChecker;

    private readonly IUrlFieldChecker _fieldChecker;

    public ProviderDefinitionValidator(
        IProviderSchemaValidator schemaValidator,
        IDuplicateIdChecker duplicateIdChecker,
        IValidationMetadataService metadataService,
        IProviderManifestReader manifestReader,
        ISelfHostedApplicabilityEvaluator selfHostedApplicability,
        ISubscriptionConfiguredChecker subscriptionConfiguredChecker,
        IServiceRetirementProbe retirementProbe,
        IUrlFieldChecker fieldChecker)
    {
        _schemaValidator = schemaValidator ?? throw new ArgumentNullException(nameof(schemaValidator));
        _duplicateIdChecker = duplicateIdChecker ?? throw new ArgumentNullException(nameof(duplicateIdChecker));
        _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
        _manifestReader = manifestReader ?? throw new ArgumentNullException(nameof(manifestReader));
        _selfHostedApplicability = selfHostedApplicability ?? throw new ArgumentNullException(nameof(selfHostedApplicability));
        _subscriptionConfiguredChecker = subscriptionConfiguredChecker ?? throw new ArgumentNullException(nameof(subscriptionConfiguredChecker));
        _retirementProbe = retirementProbe ?? throw new ArgumentNullException(nameof(retirementProbe));
        _fieldChecker = fieldChecker ?? throw new ArgumentNullException(nameof(fieldChecker));
    }

    /// <summary>
    /// Reads validation metadata from a sidecar .validation.json file.
    /// </summary>
    public ValidationMetadata? ReadValidationMetadata(string providerFilePath)
    {
        return _metadataService.Read(providerFilePath);
    }

    public async Task<List<ValidationIssue>> ValidateDirectoryAsync(
        string directoryPath,
        bool useLocalProviders = false,
        int revalidationDays = 0,
        IProgress<ValidationProgress>? progress = null,
        CancellationToken ct = default)

    {
        var allIssues = new List<ValidationIssue>();
        if (!Directory.Exists(directoryPath)) return allIssues;

        var files = Directory.GetFiles(directoryPath, "*.json")
            .Where(f => !f.EndsWith(".validation.json", StringComparison.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            var issues = await ValidateFileAsync(
                             file,
                             useLocalProviders: useLocalProviders,
                             revalidationDays: revalidationDays,
                             progress: progress,
                             ct: ct);

            allIssues.AddRange(issues);

            await Dispatcher.Yield(DispatcherPriority.Background);
        }

        var duplicateIssues = _duplicateIdChecker.CheckDuplicateIds(directoryPath);
        allIssues.AddRange(duplicateIssues);

        return allIssues;
    }

    public async Task<List<ValidationIssue>> ValidateFileAsync(
        string filePath,
        bool useLocalProviders = false,
        int revalidationDays = 0,
        IProgress<ValidationProgress>? progress = null,
        bool persistValidation = true,
        CancellationToken ct = default,
        bool forceRevalidation = false)

    {
        var issues = new List<ValidationIssue>();
        var sink = new ValidationIssueSink(issues, progress);
        var fileName = Path.GetFileName(filePath);

        // Skip re-validation for manually validated providers (preserve human review)
        // unless the revalidation period has expired (site may have changed)
        // or forceRevalidation is set (e.g. explicit user revalidation request).
        var existing = _metadataService.Read(filePath);
        if (!forceRevalidation && existing is not null && existing.Level >= ValidationLevel.ManuallyValidated)
        {
            var skip = true;
            if (revalidationDays > 0 && existing.LastValidatedAt is not null)
            {
                var elapsed = DateTime.UtcNow - existing.LastValidatedAt.Value;
                if (elapsed.TotalDays >= revalidationDays)
                {
                    skip = false;
                    sink.Progress(
                        fileName,
                        SidecarLabel,
                        filePath,
                        ValidationStage.CheckingUrl,
                        $"Manual validation expired ({elapsed.Days}d > {revalidationDays}d) — re-validating");
                }
            }

            if (skip)
            {
                var skipMessage = existing.ValidatedBy switch
                    {
                        "manual" => "Skipped (manually validated)",
                        _ => $"Skipped (validated by {existing.ValidatedBy})"
                    };

                if (persistValidation)
                {
                    sink.Progress(
                        fileName,
                        SidecarLabel,
                        filePath,
                        ValidationStage.CheckPassed,
                        skipMessage);
                }

                // Still run structural completeness checks that may not have existed
                // when the manual/auto validation was performed
                string? structuralJson = _manifestReader.Read(filePath, sink);
                if (structuralJson is not null)
                {
                    using var structuralDoc = JsonDocument.Parse(structuralJson);
                    await _subscriptionConfiguredChecker.ValidateAsync(
                        structuralDoc.RootElement,
                        fileName,
                        sink,
                        ct);
                }

                return issues;
            }
        }

        string? json = _manifestReader.Read(filePath, sink);
        if (json is null) return issues;

        issues.AddRange(_schemaValidator.ValidateSchema(json, fileName));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // A self-hosted provider runs on the user's machine behind a private base address, so there
        // is no hosted account to sign in to and no plans or hosted API pricing to price: an address
        // in any of those three fields is a structural defect, not a URL to probe. Each decision
        // tree reaches the same answer through its is-self-hosted predicate, which is what turns the
        // findings back into '-'; the flags keep the loop from re-probing a field already settled.
        var applicability = _selfHostedApplicability.Evaluate(root, fileName, sink);

        foreach (var field in UrlFields)
        {
            ct.ThrowIfCancellationRequested();

            if (root.TryGetProperty(field, out var urlProp)
                && urlProp.ValueKind == JsonValueKind.String)
            {
                var url = urlProp.GetString()!;
                if (string.IsNullOrWhiteSpace(url)) continue;

                // Skip not-applicable sentinel (self-hosted/localhost providers)
                if (url == ProviderJsonFields.NotApplicable)
                {
                    sink.Pass(fileName, field, url, "N/A (self-hosted)");
                    continue;
                }

                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) continue;

                var context = new FieldCheckContext(
                    fileName, field, url, uri, root, sink, ct)
                {
                    UseLocalProviders = useLocalProviders,
                    Applicability = applicability
                };

                await _fieldChecker.CheckFieldAsync(context);
            }
        }

        // Per-operation endpoint overrides: each entry's composed URL — its baseUrl
        // override when present, otherwise the definition baseUrl, plus its path — is
        // checked with the root baseUrl's API-surface treatment (2xx API probe and
        // reachability classifications, never the web-page/pricing/login judgments and
        // no models-endpoint fallback) under the label 'endpoints.<operation>.path',
        // so the finding reaches the sidecar, the editor's validation state, and the
        // AI-fix pipeline like any root field.
        if (root.TryGetProperty(ProviderJsonFields.Endpoints, out var endpointsEl)
            && endpointsEl.ValueKind == JsonValueKind.Object)
        {
            string? definitionBaseUrl =
                root.TryGetProperty(ProviderJsonFields.BaseUrl, out var baseEl)
                && baseEl.ValueKind == JsonValueKind.String
                    ? baseEl.GetString()
                    : null;

            foreach (var entry in endpointsEl.EnumerateObject())
            {
                ct.ThrowIfCancellationRequested();

                if (entry.Value.ValueKind != JsonValueKind.Object) continue;

                var path =
                    entry.Value.TryGetProperty("path", out var pathEl)
                    && pathEl.ValueKind == JsonValueKind.String
                        ? pathEl.GetString()
                        : null;
                if (string.IsNullOrWhiteSpace(path)) continue;

                var baseUrlOverride =
                    entry.Value.TryGetProperty("baseUrl", out var overrideEl)
                    && overrideEl.ValueKind == JsonValueKind.String
                        ? overrideEl.GetString()
                        : null;

                var composedBase = string.IsNullOrWhiteSpace(baseUrlOverride)
                    ? definitionBaseUrl
                    : baseUrlOverride;
                if (string.IsNullOrWhiteSpace(composedBase)
                    || !Uri.TryCreate(composedBase, UriKind.Absolute, out var baseUri)) continue;

                var composed = new Uri(new Uri(baseUri, path.TrimStart('/')).AbsoluteUri);

                var context = new FieldCheckContext(
                    fileName,
                    $"{ProviderJsonFields.Endpoints}.{entry.Name}.path",
                    composed.AbsoluteUri,
                    composed,
                    root,
                    sink,
                    ct)
                {
                    UseLocalProviders = useLocalProviders,
                    Applicability = applicability
                };

                await _fieldChecker.CheckFieldAsync(context);
            }
        }

        // Check that non-self-hosted providers have subscriptionPricingUrl configured
        await _subscriptionConfiguredChecker.ValidateAsync(root, fileName, sink, ct);

        // Check if the provider's service has been retired/deprecated.
        // When detected, adds ServiceRetired issues for each URL field so the AI fix
        // can suggest "-" (not applicable) — a retired service has no valid URLs.
        await _retirementProbe.CheckServiceRetiredAsync(root, fileName, sink, UrlFields, ct);

        // Persist validation results to sidecar file.
        // Delete the old sidecar first so that SaveFromValidationAsync's manual-validation
        // guard does not block the overwrite (relevant when forceRevalidation is true).
        // The deletion happens here — AFTER all validation work — so that if the operation
        // is cancelled before this point the previous sidecar (with its error information)
        // is preserved on disk.
        if (persistValidation)
        {
            _metadataService.Delete(filePath);
            await _metadataService.SaveFromValidationAsync(filePath, issues, force: true);
        }

        return issues;
    }

    public List<ValidationIssue> ValidateFileSync(string filePath)
    {
        var fileName = Path.GetFileName(filePath);

        var issues = new List<ValidationIssue>();
        var sink = new ValidationIssueSink(issues, progress: null);
        string? json = _manifestReader.Read(filePath, sink);
        if (json is null) return issues;

        issues.AddRange(_schemaValidator.ValidateSchema(json, fileName));
        return issues;
    }

}

