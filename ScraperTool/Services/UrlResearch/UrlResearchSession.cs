namespace ScraperTool.Services.UrlResearch;

public sealed class ResearchSession
{
    private readonly Dictionary<string, string> _findings = new(StringComparer.OrdinalIgnoreCase);

    public void Clear() => _findings.Clear();

    public IReadOnlyDictionary<string, string> GetAllFindings() => _findings;

    public string? GetFindingReason(string field, string value)
    {
        return _findings.TryGetValue($"{field}:{value}", out var reason) ? reason : null;
    }

    public bool HasFinding(string field, string value)
    {
        return _findings.ContainsKey($"{field}:{value}");
    }

    public void RecordFinding(string field, string value, string reason)
    {
        _findings[$"{field}:{value}"] = reason;
    }
}
