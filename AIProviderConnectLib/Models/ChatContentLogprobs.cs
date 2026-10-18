namespace AIProviderConnect.Models;

/// <summary>
/// The parsed <c>logprobs</c> block of a chat completion response, one entry per generated content
/// token. A provider whose API carries no logprobs leaves the block unset rather than inventing a
/// distribution.
/// </summary>
public sealed record ChatContentLogprobs
{
    /// <summary>
    /// Gets the generated content tokens with their log probabilities, in generation order.
    /// </summary>
    public IReadOnlyList<ChatTokenLogprob> Content { get; init; } = [];
}
