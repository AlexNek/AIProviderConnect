using System.Text;

namespace AIProviderConnect.Tests.TestDoubles;

/// <summary>
/// A test <see cref="HttpMessageHandler"/> that throws a supplied exception on the first call,
/// then returns a success response on subsequent calls. Used to verify that the Polly retry
/// pipeline retries translated network exceptions.
/// </summary>
public sealed class ScriptedThrowHttpMessageHandler : HttpMessageHandler
{
    private readonly Exception _exception;
    private readonly string _successResponseJson;

    public ScriptedThrowHttpMessageHandler(Exception exception, string successResponseJson)
    {
        _exception = exception;
        _successResponseJson = successResponseJson;
    }

    /// <summary>
    /// Gets the number of requests the handler has served.
    /// </summary>
    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;

        if (CallCount == 1)
            throw _exception;

        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(_successResponseJson, Encoding.UTF8, "application/json")
        });
    }
}
