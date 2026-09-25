namespace AIProviderConnect.Models;

/// <summary>
/// Specifies constraints on the response format.
/// </summary>
public sealed record ResponseFormat
{
    /// <summary>
    /// Gets the JSON schema definition when using structured output.
    /// </summary>
    public JsonSchemaDefinition? JsonSchema { get; init; }

    /// <summary>
    /// Gets the type of response format. Default is "text".
    /// </summary>
    public string Type { get; init; } = "text";
}
