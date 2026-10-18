namespace AIProviderConnect.Constants;

/// <summary>
/// Keys a provider definition may carry in <c>protocolConfiguration</c> to seed the
/// protocol-independent read-out members. They are how JSON states a value, never what a consumer
/// must learn: every one of them has a typed member on the options type.
/// </summary>
internal static class ReadoutConfigurationKeys
{
    /// <summary>Optional override of the built-in read-out prompt.</summary>
    internal const string PromptTemplate = "decisionReadoutPromptTemplate";

    /// <summary>Option-token style used when rendering the prompt.</summary>
    internal const string OptionTokenStyle = "decisionReadoutOptionTokenStyle";

    /// <summary>Requested <c>top_logprobs</c> count, also the read-out's option ceiling.</summary>
    internal const string TopLogprobs = "decisionReadoutTopLogprobs";

    /// <summary>Two comma-separated tokens for a Noul read-out.</summary>
    internal const string NoulTokens = "decisionReadoutNoulTokens";

    /// <summary>Sampling temperature for a read-out call.</summary>
    internal const string Temperature = "decisionReadoutTemperature";

    /// <summary>Declared Choice-option ceiling for either transport.</summary>
    internal const string MaxOptions = "decisionsMaxOptions";
}
