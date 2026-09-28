namespace AIProviderConnect.Models;

/// <summary>
/// Per-call overrides for a provider's credentials and model.
/// Each field is nullable: <c>null</c> means "use the provider's configured value", and a record
/// whose three fields are all unset is equivalent to no override at all — never to "clear the value".
/// The record is immutable and safe to pass from any thread.
/// </summary>
/// <remarks>
/// <see cref="BaseUrl"/> must be the full API base <strong>including the version segment</strong>
/// (for example <c>https://test.example.com/v1/</c>), because the library appends endpoint paths to
/// it verbatim. This type must never be logged or serialized with an unmasked
/// <see cref="ApiKey"/>; the overridden <see cref="ToString"/> masks the key.
/// </remarks>
public sealed record RequestCredentials
{
    /// <summary>
    /// Gets the API key to use for the call, or <c>null</c> to use the provider's configured key.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Gets the base URL to use for the call, or <c>null</c> to use the provider's configured base URL.
    /// Must include the version segment (for example <c>https://test.example.com/v1/</c>).
    /// </summary>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// Gets the model to use for the call, or <c>null</c> to fall back to the request's model and then
    /// the provider's configured default model.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Returns a diagnostic representation that masks <see cref="ApiKey"/>, exposing at most its last
    /// four characters so the key never appears in clear text in logs, exception messages, or test output.
    /// </summary>
    public override string ToString() =>
        $"{nameof(RequestCredentials)} {{ ApiKey = {MaskApiKey(ApiKey)}, BaseUrl = {BaseUrl}, Model = {Model} }}";

    private static string MaskApiKey(string? apiKey)
    {
        if (apiKey is null)
            return "null";

        if (apiKey.Length == 0)
            return "empty";

        return apiKey.Length > 4 ? $"****{apiKey[^4..]}" : "****";
    }
}
