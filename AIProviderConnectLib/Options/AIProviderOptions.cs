namespace AIProviderConnect.Options;

/// <summary>
/// Base configuration options for AI providers.
/// </summary>
public abstract class AIProviderOptions
{
    /// <summary>
    /// Gets or sets the API key for authentication.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base URL of the provider's API.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets default headers to include with each request.
    /// </summary>
    public Dictionary<string, string> DefaultHeaders { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the default model to use when none is specified.
    /// </summary>
    public string DefaultModel { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether this provider is enabled. Defaults to true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets protocol-specific configuration key-value pairs.
    /// Seeded from the provider definition manifest and consumed by each protocol's
    /// <c>ApplyProtocolConfiguration</c> method. Unrecognized keys are silently ignored.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ProtocolConfiguration { get; set; }

    /// <summary>
    /// Gets or sets the custom header name for API key authentication.
    /// Set by the protocol's ApplyProtocolConfiguration from ProtocolConfiguration.
    /// When null and an API key is present, the provider throws
    /// <see cref="System.InvalidOperationException"/> at first request.
    /// </summary>
    public string? CustomAuthHeaderName { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for transient failures
    /// (rate-limit and server errors). Defaults to 0 (no retries).
    /// </summary>
    public int MaxRetryCount { get; set; }

    /// <summary>
    /// Gets or sets the base delay between retry attempts. Defaults to 1 second.
    /// Actual delay uses exponential backoff with jitter.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
}
