namespace AIProviderConnect.Models;

public sealed record ChatMessage
{
    /// <summary>
    /// Plain-text content. When <see cref="ContentParts"/> is set, this is
    /// ignored for wire serialization (content parts take precedence).
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// Multimodal content parts. When set, takes precedence over
    /// <see cref="Content"/> during wire serialization. Null means
    /// "use the <see cref="Content"/> property".
    /// </summary>
    public IReadOnlyList<ContentPart>? ContentParts { get; init; }

    public required EChatRole Role { get; init; }

    public string? ToolCallId { get; init; }

    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }
}
