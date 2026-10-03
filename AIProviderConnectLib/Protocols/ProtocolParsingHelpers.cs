using System.Text.Json;

using AIProviderConnect.Constants;
using AIProviderConnect.Models;

namespace AIProviderConnect.Protocols;

/// <summary>
/// Shared helpers for wire-protocol parsing and configuration extraction.
/// </summary>
internal static class ProtocolParsingHelpers
{
    /// <summary>
    /// The protocol-configuration key whose value names the custom auth header
    /// (consumed by MessagesApi and KeyQuery protocols).
    /// </summary>
    internal const string ApiKeyHeaderNameKey = "apiKeyHeaderName";

    /// <summary>
    /// The protocol-configuration key whose value is a dotted path to the capabilities field
    /// in each model entry of the provider's /models response.
    /// </summary>
    internal const string CapabilitiesPathKey = "capabilitiesPath";

    /// <summary>
    /// The protocol-configuration key whose value describes how capabilities are encoded:
    /// "array" for a JSON array of capability-name strings,
    /// "flags-string" for a comma-separated string of capability names.
    /// Defaults to "array" when absent.
    /// </summary>
    internal const string CapabilitiesFormatKey = "capabilitiesFormat";

    /// <summary>
    /// Reads a string property from a <see cref="JsonElement"/>, returning
    /// <see cref="string.Empty"/> when the property is absent or null.
    /// </summary>
    internal static string SafeGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>
    /// Extracts the <c>apiKeyHeaderName</c> value from a protocol configuration dictionary,
    /// assigning it to <paramref name="headerName"/> when present and non-empty.
    /// </summary>
    /// <returns><c>true</c> when a non-empty value was found; otherwise <c>false</c>.</returns>
    internal static bool TryExtractApiKeyHeaderName(
        IReadOnlyDictionary<string, string>? protocolConfiguration,
        out string headerName)
    {
        if (protocolConfiguration is not null
            && protocolConfiguration.TryGetValue(ApiKeyHeaderNameKey, out var raw)
            && !string.IsNullOrWhiteSpace(raw))
        {
            headerName = raw;
            return true;
        }

        headerName = string.Empty;
        return false;
    }

    /// <summary>
    /// Maps a list of <see cref="ContentPart"/> items by dispatching each part to either
    /// <paramref name="imageMapper"/> (for image parts) or <paramref name="textMapper"/> (for text).
    /// Eliminates the repeated <c>ContentPartTypes.ImageUrl or ContentPartTypes.Image</c> switch pattern.
    /// </summary>
    internal static T[] MapContentParts<T>(
        IReadOnlyList<ContentPart> parts,
        Func<ImageContent?, T> imageMapper,
        Func<string, T> textMapper)
    {
        return parts
            .Select(p => p.Type switch
            {
                ContentPartTypes.ImageUrl or ContentPartTypes.Image => imageMapper(p.Image),
                _ => textMapper(p.Text ?? string.Empty)
            })
            .ToArray();
    }

    /// <summary>
    /// Shared outer scaffolding for all <c>ParseModels</c> implementations: extracts the model
    /// array from a root property (or uses the root element itself when <paramref name="rootProperty"/>
    /// is null), validates the array kind, applies the per-element <paramref name="mapper"/>,
    /// filters empty IDs, and returns the materialized list.
    /// </summary>
    /// <param name="json">The root JSON element of the response.</param>
    /// <param name="rootProperty">
    /// Name of the nested property that holds the model array (e.g. <c>"data"</c>, <c>"models"</c>).
    /// When null, <paramref name="json"/> is itself the array (used by <c>CatalogWireProtocol</c>).
    /// </param>
    /// <param name="providerId">The provider id to stamp on each parsed model.</param>
    /// <param name="mapper">Per-element mapping function that populates an <see cref="AIModel"/>.</param>
    internal static IReadOnlyList<AIModel> ParseModelArray(
        JsonElement json,
        string? rootProperty,
        string providerId,
        Func<JsonElement, AIModel> mapper)
    {
        JsonElement arrayElement;
        if (rootProperty is not null)
        {
            if (!json.TryGetProperty(rootProperty, out arrayElement)
                || arrayElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }
        }
        else
        {
            if (json.ValueKind != JsonValueKind.Array)
                return [];
            arrayElement = json;
        }

        return arrayElement.EnumerateArray()
            .Select(mapper)
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .ToList();
    }

    /// <summary>
    /// Parses capability flags from a model JSON element using a configurable path and format.
    /// Returns <see langword="null"/> when no path is configured, the path does not exist,
    /// or the format is unknown — never throws.
    /// </summary>
    /// <param name="model">The per-model JSON element from the provider's /models response.</param>
    /// <param name="protocolConfiguration">
    /// Provider configuration dictionary; must contain <see cref="CapabilitiesPathKey"/>
    /// for this method to attempt parsing.
    /// </param>
    /// <returns>
    /// Parsed capability flags, or <see langword="null"/> if unconfigured or unparseable.
    /// </returns>
    internal static EModelCapability? ParseCapabilities(
        JsonElement model,
        IReadOnlyDictionary<string, string>? protocolConfiguration)
    {
        if (protocolConfiguration is null
            || !protocolConfiguration.TryGetValue(CapabilitiesPathKey, out var path)
            || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        // Navigate the dotted path within the model element.
        var current = model;
        foreach (var segment in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(segment, out var next))
                return null;
            current = next;
        }

        // Determine format: defaults to "array" when not specified.
        var format = "array";
        if (protocolConfiguration.TryGetValue(CapabilitiesFormatKey, out var fmt)
            && !string.IsNullOrWhiteSpace(fmt))
        {
            format = fmt.Trim().ToLowerInvariant();
        }

        return format switch
        {
            "array" => ParseCapabilitiesFromArray(current),
            "flags-string" => ParseCapabilitiesFromFlagsString(current),
            _ => null // Unknown format — treat as unreported
        };
    }

    private static EModelCapability? ParseCapabilitiesFromArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return null;

        var result = EModelCapability.None;
        foreach (var item in element.EnumerateArray())
        {
            var name = item.GetString();
            if (string.IsNullOrWhiteSpace(name))
                continue;
            if (TryParseCapabilityName(name.Trim(), out var flag))
                result |= flag;
        }

        return result;
    }

    private static EModelCapability? ParseCapabilitiesFromFlagsString(JsonElement element)
    {
        var text = element.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var result = EModelCapability.None;
        foreach (var part in text.Split(','))
        {
            var name = part.Trim();
            if (string.IsNullOrEmpty(name))
                continue;
            if (TryParseCapabilityName(name, out var flag))
                result |= flag;
        }

        return result;
    }

    private static bool TryParseCapabilityName(string name, out EModelCapability flag)
    {
        // Match against known enum member names (case-insensitive).
        foreach (var value in Enum.GetValues<EModelCapability>())
        {
            if (value == EModelCapability.None)
                continue;
            if (string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase))
            {
                flag = value;
                return true;
            }
        }

        flag = EModelCapability.None;
        return false;
    }
}
