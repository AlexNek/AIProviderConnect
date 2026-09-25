using System.Net;
using System.Text;

namespace AIProviderConnect.Tests.TestDoubles;

/// <summary>
/// A test <see cref="HttpMessageHandler"/> that captures the outgoing request body and returns a
/// canned JSON response, so provider request mapping can be asserted without any network access.
/// </summary>
public sealed class CapturingHttpMessageHandler : HttpMessageHandler
{
    private readonly string _responseJson;

    public CapturingHttpMessageHandler(string responseJson)
    {
        _responseJson = responseJson;
    }

    /// <summary>
    /// Gets the raw JSON body of the last request, or null when the request had no content.
    /// </summary>
    public string? CapturedBody { get; private set; }

    public HttpRequestMessage? LastRequest { get; private set; }

    public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        CapturedBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(StatusCode)
        {
            Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
        };
    }
}
