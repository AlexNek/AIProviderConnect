namespace AIProviderConnect.Models;

/// <summary>
/// Represents a chunk of a streaming chat response.
/// </summary>
public sealed record StreamingChatChunk
{
    /// <summary>
    /// Gets the content of this chunk.
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Gets the incremental reasoning-content fragment in this chunk.
    /// Some providers (e.g. DeepSeek) stream chain-of-thought reasoning
    /// separately from the main content.
    /// </summary>
    public string? ReasoningContent { get; init; }

    /// <summary>
    /// Gets the incremental tool-call fragments in this chunk.
    /// </summary>
    public IReadOnlyList<StreamingToolCallDelta>? ToolCalls { get; init; }

    /// <summary>
    /// Gets whether this is the final chunk indicating completion.
    /// </summary>
    public bool IsCompleted { get; init; }
}
