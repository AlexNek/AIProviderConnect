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
    private readonly List<string?> _authorizationValues = [];
    private readonly object _gate = new();

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

    /// <summary>
    /// Gets the <c>Authorization</c> header parameter recorded for every served request, in call
    /// order (null when the request carried none). Retry tests use it to assert that each attempt
    /// carries the credentials resolved for the original call.
    /// </summary>
    public IReadOnlyList<string?> AuthorizationValues
    {
        get { lock (_gate) return _authorizationValues.ToArray(); }
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var status = _statuses[Math.Min(CallCount, _statuses.Length - 1)];
        CallCount++;
        lock (_gate) _authorizationValues.Add(request.Headers.Authorization?.Parameter);

        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
        });
    }
}
