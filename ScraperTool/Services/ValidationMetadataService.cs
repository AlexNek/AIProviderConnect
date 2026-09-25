using System.IO;
using System.Text.Json;

using ScraperTool.Models;

namespace ScraperTool.Services;

/// <summary>
/// Default implementation of IValidationMetadataService.
/// </summary>
public sealed class ValidationMetadataService : IValidationMetadataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
                                                                    {
                                                                        WriteIndented = true,
                                                                        PropertyNameCaseInsensitive =
                                                                            true
                                                                    };

    /// <inheritdoc />
    public void Delete(string providerFilePath)
    {
        ArgumentNullException.ThrowIfNull(providerFilePath);

        try
        {
            var sidecarPath = GetSidecarPath(providerFilePath);
            if (File.Exists(sidecarPath))
            {
                File.Delete(sidecarPath);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(
                ex,
                "Failed to delete validation metadata for {FilePath}",
                providerFilePath);
        }
    }

    /// <inheritdoc />
    public ValidationMetadata? Read(string providerFilePath)
    {
        var sidecarPath = GetSidecarPath(providerFilePath);

        if (!File.Exists(sidecarPath))
            return null;

        try
        {
            var json = File.ReadAllText(sidecarPath);
            return JsonSerializer.Deserialize<ValidationMetadata>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            Serilog.Log.Warning(
                ex,
                "Failed to deserialize validation metadata from {SidecarPath}",
                sidecarPath);
            return null;
        }
        catch (IOException ex)
        {
            Serilog.Log.Warning(
                ex,
                "Failed to read validation metadata from {SidecarPath}",
                sidecarPath);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SaveFromValidationAsync(
        string providerFilePath,
        IReadOnlyList<ValidationIssue> issues,
        string validatedBy = ValidatorConstants.AutoValidator,
        string? notes = null,
        bool force = false)
    {
        ArgumentNullException.ThrowIfNull(providerFilePath);
        ArgumentNullException.ThrowIfNull(issues);

        // Never overwrite manually validated or owner-verified sidecars
        // unless the caller explicitly forces it (e.g. explicit revalidation).
        if (!force)
        {
            var existing = Read(providerFilePath);
            if (existing is not null && existing.Level >= ValidationLevel.ManuallyValidated)
                return;
        }

        var providerId = ExtractProviderId(providerFilePath);
        if (providerId is null)
            return;

        var metadata = CreateMetadataFromValidation(
            providerId,
            issues,
            validatedBy,
            notes);

        await WriteSidecarAsync(providerFilePath, metadata);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(
        string providerFilePath,
        ValidationLevel level,
        string validatedBy,
        string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(providerFilePath);
        ArgumentException.ThrowIfNullOrEmpty(validatedBy);

        var providerId = ExtractProviderId(providerFilePath);
        if (providerId is null)
            return;

        var metadata = CreateManualMetadata(
            providerId,
            level,
            validatedBy,
            notes);

        await WriteSidecarAsync(providerFilePath, metadata);
    }

    /// <summary>
    /// Creates validation metadata for manual updates.
    /// </summary>
    private static ValidationMetadata CreateManualMetadata(
        string providerId,
        ValidationLevel level,
        string validatedBy,
        string? notes)
    {
        return new ValidationMetadata
                   {
                       ProviderId = providerId,
                       Level = level,
                       LastValidatedAt = DateTime.UtcNow,
                       ValidationErrors = Array.Empty<string>(),
                       ValidatedBy = validatedBy,
                       Notes = notes
                   };
    }

    /// <summary>
    /// Creates validation metadata from validation results.
    /// </summary>
    private static ValidationMetadata CreateMetadataFromValidation(
        string providerId,
        IReadOnlyList<ValidationIssue> issues,
        string validatedBy,
        string? notes)
    {
        var hasErrors = issues.Count > 0;

        return new ValidationMetadata
                   {
                       ProviderId = providerId,
                       Level =
                           hasErrors
                               ? ValidationLevel.ValidationError
                               : ValidationLevel.AutoValidated,
                       LastValidatedAt = DateTime.UtcNow,
                       ValidationErrors = issues.Select(i => i.Message).ToArray(),
                       ValidatedBy = validatedBy,
                       Notes = notes ?? GetAutoValidationNotes(issues.Count)
                   };
    }

    /// <summary>
    /// Extracts the provider ID from a provider JSON file.
    /// </summary>
    private static string? ExtractProviderId(string providerFilePath)
    {
        try
        {
            var json = File.ReadAllText(providerFilePath);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("id", out var idProp)
                && idProp.ValueKind == JsonValueKind.String)
            {
                return idProp.GetString();
            }
        }
        catch (JsonException ex)
        {
            Serilog.Log.Warning(
                ex,
                "Failed to parse provider JSON from {FilePath}",
                providerFilePath);
        }
        catch (IOException ex)
        {
            Serilog.Log.Warning(
                ex,
                "Failed to read provider JSON from {FilePath}",
                providerFilePath);
        }

        return null;
    }

    /// <summary>
    /// Gets auto-generated notes based on validation result.
    /// </summary>
    private static string GetAutoValidationNotes(int issueCount)
    {
        return issueCount == 0
                   ? "Automatically validated - all checks passed"
                   : $"{issueCount} issue(s) found";
    }

    /// <summary>
    /// Gets the sidecar validation file path for a provider JSON file.
    /// </summary>
    private static string GetSidecarPath(string providerFilePath)
    {
        var directory = Path.GetDirectoryName(providerFilePath) ?? string.Empty;
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(providerFilePath);
        return Path.Combine(
            directory,
            $"{fileNameWithoutExt}{ValidatorConstants.SidecarExtension}");
    }

    /// <summary>
    /// Writes validation metadata to sidecar file.
    /// </summary>
    private async Task WriteSidecarAsync(string providerFilePath, ValidationMetadata metadata)
    {
        try
        {
            var sidecarPath = GetSidecarPath(providerFilePath);
            var sidecarJson = JsonSerializer.Serialize(metadata, JsonOptions);
            await File.WriteAllTextAsync(sidecarPath, sidecarJson);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(
                ex,
                "Failed to write validation metadata for {FilePath}",
                providerFilePath);
        }
    }
}
