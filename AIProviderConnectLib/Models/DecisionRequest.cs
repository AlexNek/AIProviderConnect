namespace AIProviderConnect.Models;

/// <summary>
/// A request to a decision model: application state plus one or more typed questions.
/// </summary>
public sealed record DecisionRequest
{
    /// <summary>
    /// Gets the model to use. When empty, the provider falls back to its configured
    /// <see cref="Options.AIProviderOptions.DefaultModel"/>.
    /// </summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// Gets the application state, serialized to the wire as-is. Accepts a plain text string,
    /// a JSON object (<see cref="IReadOnlyDictionary{TKey,TValue}"/> keyed by string), or an array
    /// of text (<see cref="IReadOnlyList{T}"/> of string). Must not be null.
    /// </summary>
    public object? State { get; init; }

    /// <summary>
    /// Gets the typed questions, keyed by question name. Must not be null or empty.
    /// </summary>
    public IReadOnlyDictionary<string, DecisionQuestion> Questions { get; init; } =
        new Dictionary<string, DecisionQuestion>();
}
