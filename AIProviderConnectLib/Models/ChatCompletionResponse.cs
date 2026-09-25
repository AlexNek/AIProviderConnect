namespace AIProviderConnect.Models;

/// <summary>
/// Represents a chat completion response from an AI provider.
/// </summary>
public sealed record ChatCompletionResponse
{
    /// <summary>
    /// Gets the generated content.
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Gets the model's chain-of-thought reasoning content.
    /// Some providers (e.g. DeepSeek) return this as a separate field
    /// alongside the main response content.
    /// </summary>
    public string? ReasoningContent { get; init; }

    /// <summary>
    /// Gets the reason why the response finished generating.
    /// </summary>
    public string? FinishReason { get; init; }

    /// <summary>
    /// Gets the unique identifier for this response.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets the model used to generate this response.
    /// </summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// Gets the list of tool calls, if any.
    /// </summary>
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }

    /// <summary>
    /// Gets the token usage information.
    /// </summary>
    public UsageInfo Usage { get; init; } = new();
}
