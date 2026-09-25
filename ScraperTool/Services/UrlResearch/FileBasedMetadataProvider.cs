using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using AIProviderConnect.Models;

using ScraperTool.Services.UrlFix;
using ScraperTool.Services.UrlResearch.Abstractions;
using ScraperTool.Services.UrlResearch.Models;

namespace ScraperTool.Services.UrlResearch;

/// <summary>
/// Loads investigation metadata from local JSON files instead of SQLite.
/// Desktop-friendly: editable, diffable, no migrations needed.
/// </summary>
public sealed class FileBasedMetadataProvider : IUrlIntelligenceRuleStore, IFieldResearchProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _configDirectory;

    private List<FileRuleEntry>? _rules;
    private DecisionKeywordSets? _decisionKeywords;
    private List<FieldResearchProfile>? _fieldProfiles;

    public FileBasedMetadataProvider(string? configDirectory = null)
    {
        _configDirectory = configDirectory
            ?? Path.Combine(AppContext.BaseDirectory, "Config");
    }

    public async Task<DecisionKeywordSets> GetDecisionKeywordsAsync(
        CancellationToken ct = default)
    {
        if (_decisionKeywords is not null) return _decisionKeywords;
        var path = Path.Combine(_configDirectory, "decision-keywords.json");
        // The JSON file is the single source of truth — fail loudly if missing
        // instead of silently running with empty keyword sets.
        _decisionKeywords = await LoadJsonAsync<DecisionKeywordSets>(path, ct)
                            ?? throw new FileNotFoundException(
                                "Decision keyword config is required.",
                                path);
        return _decisionKeywords;
    }

    /// <summary>
    /// Fail-fast check for required config files, intended to run at startup.
    /// The decision keywords and the per-field research profiles are required —
    /// the URL research trees route on them. The other config files degrade to
    /// empty defaults when missing.
    /// </summary>
    public async Task ValidateRequiredConfigAsync(CancellationToken ct = default)
    {
        await GetDecisionKeywordsAsync(ct);
        await GetFieldProfilesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<FieldResearchProfile?> GetProfileAsync(
        string fieldKind,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fieldKind))
            return null;

        var profiles = await GetFieldProfilesAsync(ct);
        return profiles.FirstOrDefault(
            p => string.Equals(p.FieldName, fieldKind, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<List<FieldResearchProfile>> GetFieldProfilesAsync(CancellationToken ct)
    {
        if (_fieldProfiles is not null) return _fieldProfiles;
        var path = Path.Combine(_configDirectory, "field-definitions.json");
        // The trees read their sibling ordering and relevance vocabulary from this file,
        // so a missing file is a configuration error rather than a silent loss of routing.
        _fieldProfiles = await LoadJsonAsync<List<FieldResearchProfile>>(path, ct)
                         ?? throw new FileNotFoundException(
                             "Field research profile config is required.",
                             path);
        return _fieldProfiles;
    }

    public async Task<IReadOnlyList<FileRuleEntry>> GetAllRulesAsync(
        CancellationToken ct = default)
    {
        var rules = await LoadRulesAsync(ct);
        return rules.OrderBy(r => r.Priority).ToList();
    }

    /// <summary>
    /// Persists updated rules back to the JSON file.
    /// </summary>
    public async Task SaveRulesAsync(
        IReadOnlyList<FileRuleEntry> rules,
        CancellationToken ct = default)
    {
        var path = Path.Combine(_configDirectory, "url-intelligence-rules.json");
        var json = JsonSerializer.Serialize(rules, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
        _rules = null; // invalidate cache
    }

    /// <summary>
    /// Checks whether a specific rule kind is enabled.
    /// </summary>
    public async Task<bool> IsRuleEnabledAsync(
        UrlIntelligenceRuleKind kind,
        CancellationToken ct = default)
    {
        var rules = await LoadRulesAsync(ct);
        var entry = rules.FirstOrDefault(r => r.RuleKind == kind);
        return entry?.IsEnabled ?? true;
    }

    /// <summary>
    /// Invalidates all cached data. Call after external file edits.
    /// </summary>
    public void InvalidateCache()
    {
        _rules = null;
        _decisionKeywords = null;
        _fieldProfiles = null;
    }

    private async Task<List<FileRuleEntry>> LoadRulesAsync(CancellationToken ct)
    {
        if (_rules is not null) return _rules;
        var path = Path.Combine(_configDirectory, "url-intelligence-rules.json");
        _rules = await LoadJsonAsync<List<FileRuleEntry>>(path, ct) ?? [];
        return _rules;
    }

    private static async Task<T?> LoadJsonAsync<T>(string path, CancellationToken ct)
        where T : class
    {
        if (!File.Exists(path))
            return null;

        // ConfigureAwait(false): this helper is also awaited from startup code that
        // blocks the WPF UI thread (GetAwaiter().GetResult in App.OnStartup) —
        // capturing the UI context there deadlocks the app before the window opens.
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct)
            .ConfigureAwait(false);
    }

}

