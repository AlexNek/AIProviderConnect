namespace AIProviderConnect.Models;

/// <summary>
/// A response from a decision model: one typed answer per question, keyed by question name.
/// </summary>
public sealed record DecisionResponse
{
    /// <summary>
    /// Gets the response identifier.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets the model snapshot that served the request.
    /// </summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// Gets the serving provider name reported by the host.
    /// </summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>
    /// Gets the typed answers, keyed by question name (matching <see cref="DecisionRequest.Questions"/>).
    /// </summary>
    public IReadOnlyDictionary<string, DecisionAnswer> Answers { get; init; } =
        new Dictionary<string, DecisionAnswer>();

    /// <summary>
    /// Gets the usage information, including a per-call <see cref="UsageInfo.Cost"/> when reported.
    /// </summary>
    public UsageInfo Usage { get; init; } = new();
}
