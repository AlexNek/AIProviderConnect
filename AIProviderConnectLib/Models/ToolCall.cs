namespace AIProviderConnect.Models;

/// <summary>
/// Represents a tool call returned by the AI.
/// </summary>
public sealed record ToolCall
{
    /// <summary>
    /// Gets the arguments to pass to the function, as a JSON string.
    /// </summary>
    public required string Arguments { get; init; }

    /// <summary>
    /// Gets the unique identifier for this tool call.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the name of the function to call.
    /// </summary>
    public required string Name { get; init; }
}
