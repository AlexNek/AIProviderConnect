using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;

namespace AIProviderConnect.Tests.TestDoubles;

/// <summary>
/// An <see cref="IAIProvider"/> that returns scripted chat responses and records every request, so
/// the read-out can be exercised without HTTP. A queued exception is thrown instead of a response,
/// which lets a test assert that the first failure aborts the whole call.
/// </summary>
public sealed class ScriptedChatProvider : IAIProvider
{
    private readonly Queue<Func<ChatCompletionResponse>> _responses = new();

    public ScriptedChatProvider(string id = "scripted-readout") => Id = id;

    public string Id { get; }

    public bool IsEnabled => true;

    public EProviderProtocol Protocol => EProviderProtocol.OpenAICompatible;

    public List<ChatCompletionRequest> Requests { get; } = [];

    public ScriptedChatProvider Enqueue(ChatCompletionResponse response)
    {
        _responses.Enqueue(() => response);
        return this;
    }

    public ScriptedChatProvider EnqueueFailure(Exception exception)
    {
        _responses.Enqueue(() => throw exception);
        return this;
    }

    public Task<ChatCompletionResponse> ChatAsync(
        ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);

        if (_responses.Count == 0)
            throw new InvalidOperationException("No scripted chat response remains.");

        return Task.FromResult(_responses.Dequeue()());
    }
}
