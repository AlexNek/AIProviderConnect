using System.IO;

namespace ScraperTool.Services.Validation;

/// <summary>
/// Reads a provider manifest from disk, filing the two findings that stand in for its text.
/// </summary>
public sealed class ProviderManifestReader : IProviderManifestReader
{
    /// <inheritdoc />
    public string? Read(string filePath, IValidationIssueSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        var fileName = Path.GetFileName(filePath);

        if (!File.Exists(filePath))
        {
            sink.Append(new ValidationIssue(fileName, "FileNotFound", "File does not exist."));
            return null;
        }

        try
        {
            return File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            // A file that exists but cannot be read is normally a concurrent write or a locked
            // handle, which the operator has to see in the log rather than only as a finding.
            Serilog.Log.Warning(ex, "Could not read provider file {FilePath}", filePath);
            sink.Append(new ValidationIssue(fileName, "ReadError", "Could not read file."));
            return null;
        }
    }
}
