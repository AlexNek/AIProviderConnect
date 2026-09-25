using System.Net;
using System.Text;

namespace AIProviderConnect.Tests.TestDoubles;

/// <summary>
/// A test <see cref="HttpMessageHandler"/> that replays a scripted sequence of status codes and
/// counts how many requests were sent, so retry behavior can be asserted without any network access.
/// Once the script is exhausted, the last scripted status is repeated.
/// </summary>
public sealed class ScriptedStatusHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode[] _statuses;
    private readonly string _responseJson;

    public ScriptedStatusHttpMessageHandler(string responseJson, params HttpStatusCode[] statuses)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(statuses.Length, 1);

        _responseJson = responseJson;
        _statuses = statuses;
    }

    /// <summary>
    /// Gets the number of requests the handler has served.
    /// </summary>
    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var status = _statuses[Math.Min(CallCount, _statuses.Length - 1)];
        CallCount++;

        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
        });
    }
}
