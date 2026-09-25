using ScraperTool.Services.UrlResearch.Models;

namespace ScraperTool.Services.UrlResearch.Abstractions;

/// <summary>
/// Read access to the editable per-field research profiles
/// (<c>Config/field-definitions.json</c>). Editing and persistence of the
/// definitions stays on the metadata provider for the settings UI.
/// </summary>
public interface IFieldResearchProfileStore
{
    /// <summary>
    /// Returns the research profile for a field kind, or <see langword="null"/> when the
    /// config declares none — callers then fall back to name-derived behaviour alone.
    /// </summary>
    Task<FieldResearchProfile?> GetProfileAsync(
        string fieldKind,
        CancellationToken ct = default);
}
