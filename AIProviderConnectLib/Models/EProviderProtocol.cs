namespace AIProviderConnect.Models;

public enum EProviderProtocol
{
    /// <summary>
    /// Marker for consumer-supplied provider registrations with no built-in wire implementation.
    /// The DI registration throws if a provider declares this protocol; consumers must register
    /// their own <see cref="Abstractions.IAIProvider"/> implementation via
    /// <c>AddAiProviders(b =&gt; b.AddProvider&lt;TProvider&gt;(...))</c>.
    /// </summary>
    Native,

    OpenAICompatible,

    MessagesApi,

    KeyQuery,

    Catalog,

    HybridGateway,

    /// <summary>
    /// A decision model: answers typed questions about application state with probabilities
    /// rather than generated chat prose. Providers of this protocol implement
    /// <see cref="Abstractions.IDecisionProvider"/> and reject <see cref="Abstractions.IAIProvider.ChatAsync"/>.
    /// </summary>
    Decision
}
