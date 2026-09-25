using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using AIProviderConnect.Models;

using ScraperTool.Models;

namespace ScraperTool.Services;

/// <summary>
/// Owns all provider JSON file patching logic: reads a provider JSON file,
/// applies field-level overrides from approved AI suggestions, and writes it back.
/// Supports both updating existing fields and adding new fields (e.g., regionalEndpoints).
/// No UI or DB concerns here.
/// </summary>
public sealed class ProviderJsonPatchService
{
    /// <summary>
    /// JSON field name to declared field type, for the fields the model reads back as a number
    /// or a bool. A suggestion is always carried as a string, and the library deserializes
    /// <c>minModelCount</c> into an <see cref="int"/> with no string-to-number tolerance — so a
    /// field written with the wrong JSON type makes the whole provider file unreadable, not just
    /// that one field. The declared type, not the element currently on disk, has to decide what
    /// gets written: when a required field is <em>absent</em> (the state that raised the issue in
    /// the first place) there is nothing on disk to copy the type from.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, TypeCode> ScalarFieldTypes = BuildScalarFieldTypes();

    private readonly string _manifestPath;

    public ProviderJsonPatchService(string manifestPath)
    {
        _manifestPath = manifestPath;
    }

    private static IReadOnlyDictionary<string, TypeCode> BuildScalarFieldTypes()
    {
        var types = new Dictionary<string, TypeCode>(StringComparer.OrdinalIgnoreCase);

        // The on-disk manifest is flat: it carries the runtime definition and the research
        // metadata side by side, so the numeric/bool fields (minModelCount, hasFreeTier, ...)
        // are declared across both records and both have to be scanned.
        foreach (var type in new[] { typeof(ProviderDefinition), typeof(ProviderResearchMetadata) })
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var declared = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

                // An enum is written as a string and mapped by its own converter, so it is not a
                // scalar this writer may coerce.
                if (declared.IsEnum)
                    continue;

                var code = Type.GetTypeCode(declared);
                var isNumeric = code
                    is TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64
                        or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64
                        or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;

                if (isNumeric || code == TypeCode.Boolean)
                    types[JsonFieldName(property)] = code;
            }
        }

        return types;
    }

    private static string JsonFieldName(PropertyInfo property) =>
        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;

    /// <summary>
    /// Applies each approved suggestion to its corresponding JSON file.
    /// Preserves all existing fields (round-trip safe).
    /// If the field doesn't exist, it will be added.
    /// Special handling for regionalEndpoints which is a dictionary of region -> URL mappings.
    /// </summary>
    public ProviderPatchResult Apply(IReadOnlyList<AiSuggestion> approved)
    {
        var errors = new List<string>();
        var updatedCount = 0;
        var appliedCount = 0;

        // Group by file first
        var groups = approved.GroupBy(s => s.ProviderId);

        foreach (var group in groups)
        {
            var filePath = Path.Combine(_manifestPath, $"{group.Key}.json");
            if (!File.Exists(filePath))
            {
                errors.Add($"[{group.Key}] File not found, skipping.");
                continue;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                using var doc = JsonDocument.Parse(json);

                var mutable = new Dictionary<string, JsonElement?>();
                foreach (var prop in doc.RootElement.EnumerateObject())
                    mutable[prop.Name] = prop.Value.Clone();

                // Check if any suggestion is for regionalEndpoints
                var regionalEndpointSuggestions = group.Where(s => string.Equals(
                    s.Field,
                    ProviderJsonFields.RegionalEndpoints,
                    StringComparison.OrdinalIgnoreCase)).ToList();
                var otherSuggestions = group.Where(s => !string.Equals(
                                                            s.Field,
                                                            ProviderJsonFields.RegionalEndpoints,
                                                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Process regionalEndpoints first (as a dictionary merge)
                if (regionalEndpointSuggestions.Count > 0)
                {
                    // Get existing regionalEndpoints or create new
                    Dictionary<string, string>? existingRegional = null;
                    if (mutable.TryGetValue(
                            ProviderJsonFields.RegionalEndpoints,
                            out var existingNode) && existingNode.HasValue
                                                  && existingNode.Value.ValueKind
                                                  == JsonValueKind.Object)
                    {
                        existingRegional = new Dictionary<string, string>();
                        foreach (var prop in existingNode.Value.EnumerateObject())
                        {
                            existingRegional[prop.Name] = prop.Value.GetString() ?? string.Empty;
                        }
                    }

                    if (existingRegional is null)
                    {
                        existingRegional = new Dictionary<string, string>();
                    }

                    // Merge new suggestions
                    foreach (var suggestion in regionalEndpointSuggestions)
                    {
                        if (suggestion.SuggestedValue is not null)
                        {
                            // Parse the JSON string to extract region mappings
                            using var suggestionDoc = JsonDocument.Parse(suggestion.SuggestedValue);
                            var root = suggestionDoc.RootElement;

                            if (root.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var prop in root.EnumerateObject())
                                {
                                    var region = prop.Name;
                                    var url = prop.Value.GetString() ?? string.Empty;
                                    existingRegional[region] = url;
                                    appliedCount++;
                                }
                            }
                            else if (root.ValueKind == JsonValueKind.String)
                            {
                                // Single URL - try to extract region from suggestion reason or use detected region
                                var url = root.GetString() ?? string.Empty;
                                if (!string.IsNullOrEmpty(url))
                                {
                                    // Use the user's detected region
                                    existingRegional[suggestion.ProviderId] = url;
                                    appliedCount++;
                                }
                            }
                        }
                    }

                    // Write back regionalEndpoints as JSON object
                    mutable[ProviderJsonFields.RegionalEndpoints] = JsonDocument
                        .Parse(JsonSerializer.Serialize(existingRegional)).RootElement.Clone();
                }

                // Process other field updates
                foreach (var suggestion in otherSuggestions)
                {
                    if (suggestion.SuggestedValue is null) continue;

                    var existingIsNumber = mutable.TryGetValue(suggestion.Field, out var existingEl)
                                           && existingEl.HasValue
                                           && existingEl.Value.ValueKind == JsonValueKind.Number;

                    ScalarFieldTypes.TryGetValue(suggestion.Field, out var declaredType);

                    if (IsNumericType(declaredType) || existingIsNumber)
                    {
                        if (!TryParseAsNumber(suggestion.SuggestedValue, out var numericElement))
                        {
                            // Writing the suggestion as a string would produce a field the model
                            // cannot read, which costs the whole provider rather than the field.
                            errors.Add(
                                $"[{group.Key}] Field '{suggestion.Field}' takes a number — "
                                + $"suggestion '{suggestion.SuggestedValue}' is not one, skipped.");
                            continue;
                        }

                        mutable[suggestion.Field] = numericElement;
                        appliedCount++;
                        continue;
                    }

                    if (declaredType == TypeCode.Boolean)
                    {
                        if (!TryParseAsBoolean(suggestion.SuggestedValue, out var boolElement))
                        {
                            errors.Add(
                                $"[{group.Key}] Field '{suggestion.Field}' takes true or false — "
                                + $"suggestion '{suggestion.SuggestedValue}' is neither, skipped.");
                            continue;
                        }

                        mutable[suggestion.Field] = boolElement;
                        appliedCount++;
                        continue;
                    }

                    mutable[suggestion.Field] = JsonDocument
                        .Parse(JsonSerializer.Serialize(suggestion.SuggestedValue))
                        .RootElement
                        .Clone();

                    appliedCount++;
                }

                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(
                           stream,
                           new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    foreach (var kvp in mutable)
                        if (kvp.Value.HasValue)
                        {
                            writer.WritePropertyName(kvp.Key);
                            kvp.Value.Value.WriteTo(writer);
                        }

                    writer.WriteEndObject();
                }

                File.WriteAllBytes(filePath, stream.ToArray());
                updatedCount++;
            }
            catch (Exception ex)
            {
                errors.Add($"[{group.Key}] Failed to patch: {ex.Message}");
            }
        }

        return new ProviderPatchResult(updatedCount, appliedCount, errors);
    }

    /// <summary>
    /// Attempts to parse a string value as a JSON number.
    /// A suggestion arrives as a string for every field, so this is what keeps a numeric field a
    /// JSON number — both when the provider already stores one and, more importantly, when the
    /// field is absent or holds a stale value of the wrong kind.
    /// </summary>
    private static bool TryParseAsNumber(string value, out JsonElement element)
    {
        element = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longVal))
        {
            var json = JsonDocument.Parse(longVal.ToString(CultureInfo.InvariantCulture));
            element = json.RootElement.Clone();
            return true;
        }

        if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleVal))
        {
            var json = JsonDocument.Parse(doubleVal.ToString("R", CultureInfo.InvariantCulture));
            element = json.RootElement.Clone();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Attempts to parse a string value as a JSON boolean.
    /// </summary>
    private static bool TryParseAsBoolean(string value, out JsonElement element)
    {
        element = default;

        if (!bool.TryParse(value?.Trim(), out var parsed))
            return false;

        var json = JsonDocument.Parse(parsed ? "true" : "false");
        element = json.RootElement.Clone();
        return true;
    }

    private static bool IsNumericType(TypeCode code) =>
        code is TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64
            or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64
            or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;
}
