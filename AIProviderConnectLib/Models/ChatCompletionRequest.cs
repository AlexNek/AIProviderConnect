namespace AIProviderConnect.Models;

/// <summary>
/// Represents a request for chat completion.
/// </summary>
public sealed record ChatCompletionRequest
{
    /// <summary>
    /// Gets the maximum number of tokens to generate. Default is null (provider default).
    /// </summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// Gets the list of messages comprising the conversation.
    /// </summary>
    public IReadOnlyList<ChatMessage> Messages { get; init; } = [];

    /// <summary>
    /// Gets the model identifier to use for completion.
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// Gets the response format constraints.
    /// </summary>
    public ResponseFormat? ResponseFormat { get; init; }

    /// <summary>
    /// Gets the sampling temperature (0.0 to 2.0). Default is 0.7.
    /// </summary>
    public float Temperature { get; init; } = 0.7f;

    /// <summary>
    /// Gets the list of tools the model may call.
    /// </summary>
    public IReadOnlyList<ToolDefinition>? Tools { get; init; }

    /// <summary>
    /// Gets the stop sequences that signal the model to stop generating.
    /// </summary>
    public IReadOnlyList<string>? Stop { get; init; }

    /// <summary>
    /// Gets the nucleus sampling probability (0.0 to 1.0).
    /// </summary>
    public float? TopP { get; init; }

    /// <summary>
    /// Gets the frequency penalty (-2.0 to 2.0) that reduces repetition of token frequencies.
    /// </summary>
    public float? FrequencyPenalty { get; init; }

    /// <summary>
    /// Gets the presence penalty (-2.0 to 2.0) that encourages talking about new topics.
    /// </summary>
    public float? PresencePenalty { get; init; }

    /// <summary>
    /// Gets whether the provider should return token log probabilities for the completion. Unset
    /// omits the field from the wire request, so a provider that has no such API is never sent one.
    /// </summary>
    public bool? Logprobs { get; init; }

    /// <summary>
    /// Gets how many alternative tokens the provider should report at each position. Only
    /// meaningful together with <see cref="Logprobs"/>; unset omits the field from the wire request.
    /// </summary>
    public int? TopLogprobs { get; init; }
}
