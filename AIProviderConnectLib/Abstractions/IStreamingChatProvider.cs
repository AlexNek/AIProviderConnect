using AIProviderConnect.Models;

namespace AIProviderConnect.Abstractions;

/// <summary>
/// Optional runtime capability — not all providers support streaming.
/// Use <c>is IStreamingChatProvider</c> to check at the call site.
/// </summary>
public interface IStreamingChatProvider
{
    IAsyncEnumerable<StreamingChatChunk> StreamAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default);
}
