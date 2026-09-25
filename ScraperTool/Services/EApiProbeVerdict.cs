namespace ScraperTool.Services;

/// <summary>
/// What probing a stored API address established about it.
/// <para>
/// <see cref="NotEvaluated"/> is deliberately not a verdict in favour of the address: it means the
/// reply carried no evidence either way, which is normal for a base URL whose root answers 401 or
/// 404 while the routes below it work. It must never be reported as though the address had been
/// verified — the caller keeps the stored value and names the abstention in the message, because
/// <see cref="ValidationStage"/> has no member for "could not be evaluated". Measured over every
/// provider the validator enumerates, none lands here: a base that reaches the probe at all is
/// settled by its root reply.
/// </para>
/// </summary>
public enum EApiProbeVerdict
{
    /// <summary>The reply is data an API client can read.</summary>
    IsApi,

    /// <summary>The reply shows the address is not an API endpoint.</summary>
    NotApi,

    /// <summary>The reply showed nothing: an error status, no body, or no reply at all.</summary>
    NotEvaluated
}
