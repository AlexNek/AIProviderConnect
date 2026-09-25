using ScraperTool.Models;
using System.IO;
using System.Text.Json;

using AIProviderConnect.Models;

namespace ScraperTool.Services.Validation;

/// <summary>
/// Default implementation of <see cref="IDuplicateIdChecker"/>.
/// </summary>
public sealed class DuplicateIdChecker : IDuplicateIdChecker
{
    public List<ValidationIssue> CheckDuplicateIds(string directoryPath)
    {
        var issues = new List<ValidationIssue>();
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.GetFiles(directoryPath, "*.json"))
        {
            if (file.EndsWith(".validation.json", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var json = File.ReadAllText(file);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(ProviderJsonFields.Id, out var idProp)
                    && idProp.ValueKind == JsonValueKind.String)
                {
                    var id = idProp.GetString()!;
                    var fileName = Path.GetFileName(file);
                    if (ids.TryGetValue(id, out var existingFile))
                    {
                        issues.Add(
                            new ValidationIssue(
                                fileName,
                                ValidationIssueCodes.DuplicateId,
                                $"ID '{id}' is duplicated (also in {existingFile})."));
                    }
                    else
                    {
                        ids[id] = fileName;
                    }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to check duplicate IDs in {File}", file);
            }
        }

        return issues;
    }
}
