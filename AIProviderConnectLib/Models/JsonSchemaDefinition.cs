using System.Text.Json;

namespace AIProviderConnect.Models;

/// <summary>
/// Defines a JSON schema for structured output.
/// </summary>
public sealed record JsonSchemaDefinition
{
    /// <summary>
    /// Gets the name of the schema.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the JSON schema definition.
    /// </summary>
    public required JsonElement Schema { get; init; }

    /// <summary>
    /// Gets whether strict mode is enabled.
    /// </summary>
    public bool Strict { get; init; } = true;
}
