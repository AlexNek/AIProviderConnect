namespace ScraperTool.Services.Validation;

/// <summary>
/// Validates the structural schema of a provider definition JSON file.
/// Checks required fields, protocol values, URL formats, deserialization, and model count.
/// </summary>
public interface IProviderSchemaValidator
{
    /// <summary>
    /// Validates the JSON structure and required fields of a provider definition.
    /// </summary>
    /// <param name="json">The raw JSON string.</param>
    /// <param name="fileName">File name for issue reporting.</param>
    /// <returns>List of structural validation issues.</returns>
    List<ValidationIssue> ValidateSchema(string json, string fileName);
}
