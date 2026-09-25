namespace ScraperTool.Services.Validation;

/// <summary>
/// Reads a provider manifest from disk.
/// <para>
/// A manifest that cannot be read is a validation finding, not an exception: the run has to say
/// which file was missing or unreadable and move on to the next one, so the failures are filed
/// with the rest of the results instead of being thrown at the caller.
/// </para>
/// </summary>
public interface IProviderManifestReader
{
    /// <summary>
    /// Reads the manifest text, or returns <c>null</c> after filing the reason it could not be
    /// read — <c>FileNotFound</c> or <c>ReadError</c> — with the run's issue sink.
    /// </summary>
    string? Read(string filePath, IValidationIssueSink sink);
}
