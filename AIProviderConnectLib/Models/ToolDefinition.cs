using System.Text.Json;

namespace AIProviderConnect.Models;

/// <summary>
/// Defines a tool (function) that the AI can call.
/// </summary>
public sealed record ToolDefinition
{
    /// <summary>
    /// Gets the description of what the tool does.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets the name of the tool.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the JSON schema for the tool's parameters.
    /// </summary>
    public required JsonElement Parameters { get; init; }
}
