namespace AIProviderConnect.Tests.TestDoubles;

/// <summary>
/// A test <see cref="HttpMessageHandler"/> that always throws the supplied exception,
/// so network-level failure translation can be asserted without any network access.
/// </summary>
public sealed class ThrowingHttpMessageHandler : HttpMessageHandler
{
    private readonly Exception _exception;

    public ThrowingHttpMessageHandler(Exception exception)
    {
        _exception = exception;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        throw _exception;
}
