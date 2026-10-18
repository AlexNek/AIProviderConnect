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

    /// <summary>
    /// Gets or sets an explicit API-key requirement for this provider. When set, it wins over the
    /// value declared by the provider definition; when unset, the definition value applies and,
    /// failing that, the requirement is derived from the configured address (a loopback host does
    /// not require a key). Set it through <c>Configure&lt;TOptions&gt;</c> for a catalog-shipped entry.
    /// </summary>
    public bool? RequiresApiKey { get; set; }

    /// <summary>
    /// Gets or sets whether this provider answers a decision request by reading the option tokens'
    /// logprobs from its own chat surface. Defaults to true. Setting it to false restores a refusal
    /// with <see cref="Exceptions.AiErrorCodes.DecisionNotSupported"/> for a provider that must never
    /// send a decision over a chat turn; a provider that declares a native decisions surface is
    /// unaffected because its own implementation overrides the read-out.
    /// </summary>
    public bool DecisionReadoutEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets an optional template for the read-out prompt, replacing the built-in render.
    /// Placeholders: <c>{state}</c>, <c>{instructions}</c>, and <c>{options}</c>. When null, the
    /// built-in prompt is used. This is a host convention override, never a precondition for support.
    /// </summary>
    public string? DecisionReadoutPromptTemplate { get; set; }

    /// <summary>
    /// Gets or sets the token style that prefixes each option in the read-out prompt:
    /// <c>uppercaseLetter</c> (<c>A.</c>), <c>lowercaseLetter</c> (<c>a.</c>), or
    /// <c>levelIndex</c> (<c>0.</c>). Defaults to <c>uppercaseLetter</c>.
    /// </summary>
    public string DecisionReadoutOptionTokenStyle { get; set; } = "uppercaseLetter";

    /// <summary>
    /// Gets or sets the <c>top_logprobs</c> count requested at the answer position. It is also the
    /// read-out's own option ceiling: an option that cannot appear in the response cannot be read.
    /// Defaults to 20.
    /// </summary>
    public int DecisionReadoutTopLogprobs { get; set; } = 20;

    /// <summary>
    /// Gets or sets the two comma-separated tokens used for a Noul (yes/no) read-out. Defaults to
    /// <c>yes,no</c>.
    /// </summary>
    public string DecisionReadoutBoolTokens { get; set; } = "yes,no";

    /// <summary>
    /// Gets or sets the sampling temperature for a read-out call. The read-out uses the option
    /// tokens' logprobs rather than the sampled token, so the default is 0.
    /// </summary>
    public float DecisionReadoutTemperature { get; set; }

    /// <summary>
    /// Gets or sets an optional Choice-option ceiling declared by the host. When set, a Choice
    /// question with more options is refused with <see cref="Exceptions.AiErrorCodes.ConfigurationError"/>
    /// before any request leaves the process. When null, no ceiling beyond the wire limit applies.
    /// </summary>
    public int? DecisionsMaxOptions { get; set; }
}
