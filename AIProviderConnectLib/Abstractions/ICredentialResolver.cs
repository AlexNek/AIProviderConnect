using AIProviderConnect.Models;

namespace AIProviderConnect.Abstractions;

/// <summary>
/// Consumer-registered hook consulted once per public provider call to supply runtime
/// <see cref="RequestCredentials"/> (API key, base URL, model) without rebuilding the DI graph.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a singleton in DI and stateless, so an implementation must be safe for concurrent
/// use. Return <c>null</c> when no override is needed (use the configured values); return a populated
/// <see cref="RequestCredentials"/> to override one or more fields for the next call. The library
/// provides no default implementation — the consumer supplies the source (per-user context,
/// request scope, database lookup).
/// </para>
/// <para>
/// The library passes only the <c>providerId</c>, so a resolver that must distinguish
/// concurrent callers reads that identity from ambient state (for example <c>AsyncLocal&lt;T&gt;</c> or
/// <c>IHttpContextAccessor</c>) and creates its own DI scope to reach scoped storage such as a
/// <c>DbContext</c>.
/// </para>
/// </remarks>
public interface ICredentialResolver
{
    /// <summary>
    /// Resolves the per-call credential and model overrides for the given provider.
    /// </summary>
    /// <param name="providerId">The provider id the upcoming call targets.</param>
    /// <param name="cancellationToken">Cancellation token for the call.</param>
    /// <returns>
    /// A <see cref="RequestCredentials"/> to override one or more fields, or <c>null</c> to use the
    /// provider's configured values.
    /// </returns>
    ValueTask<RequestCredentials?> ResolveAsync(
        string providerId,
        CancellationToken cancellationToken = default);
}
