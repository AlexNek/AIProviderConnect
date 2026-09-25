using AIProviderConnect.Models;

namespace AIProviderConnect.DependencyInjection;

/// <summary>
/// Fail-fast validation for consumer-supplied <see cref="ProviderDefinition"/> instances.
/// Only custom definitions are validated; embedded definitions are trusted as shipped.
/// </summary>
internal static class CustomProviderDefinitionValidator
{
    /// <summary>
    /// Validates a set of consumer-supplied definitions, throwing <see cref="ArgumentException"/>
    /// on the first invalid entry.
    /// </summary>
    /// <param name="customProviders">The consumer-supplied definitions to validate.</param>
    public static void Validate(IEnumerable<ProviderDefinition> customProviders)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in customProviders)
        {
            if (string.IsNullOrWhiteSpace(definition.Id))
            {
                throw new ArgumentException(
                    "A custom provider definition has a null, empty, or whitespace Id.",
                    nameof(customProviders));
            }

            if (string.IsNullOrWhiteSpace(definition.BaseUrl))
            {
                throw new ArgumentException(
                    $"Custom provider definition '{definition.Id}' has a null, empty, or whitespace BaseUrl.",
                    nameof(customProviders));
            }

            if (!seen.Add(definition.Id))
            {
                throw new ArgumentException(
                    $"Duplicate custom provider definition id '{definition.Id}'.",
                    nameof(customProviders));
            }
        }
    }
}
