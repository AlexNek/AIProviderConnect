using AIProviderConnect.Models;

namespace AIProviderConnect.Abstractions;

/// <summary>
/// Consumer-supplied source of <see cref="ModelOverride"/> entries, keyed by provider id.
/// The library ships an in-memory default; a consumer may implement this to back overrides with
/// their own store (code, JSON, database). The library hardcodes no data location.
/// </summary>
public interface IModelOverrideStore
{
    /// <summary>
    /// Returns the overrides registered for <paramref name="providerId"/>, or an empty list when
    /// there are none.
    /// </summary>
    /// <param name="providerId">The provider id whose overrides to return.</param>
    IReadOnlyList<ModelOverride> Get(string providerId);
}
