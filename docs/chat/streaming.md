# Streaming

Streaming is an optional provider capability exposed through a separate
interface:

```csharp
public interface IStreamingChatProvider
{
    IAsyncEnumerable<StreamingChatChunk> StreamAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default);
}
```

All four chat-family provider implementations support streaming. `DecisionProvider` is not a chat provider and does not implement streaming.

## Checking for Support

Streaming is not on `IAIProvider`, so test at the call site:

```csharp
if (provider is not IStreamingChatProvider streaming)
{
    // fall back to ChatAsync
    return await provider.ChatAsync(request, cancellationToken);
}

await foreach (var chunk in streaming.StreamAsync(request, cancellationToken))
{
    Console.Write(chunk.Content);
}
```

## StreamingChatChunk

| Property | Type | Description |
| --- | --- | --- |
| `Content` | `string` | Text fragment for this chunk (empty on non-text events) |
| `ReasoningContent` | `string?` | Incremental chain-of-thought reasoning fragment (some providers only) |
| `ToolCalls` | `IReadOnlyList<StreamingToolCallDelta>?` | Incremental tool-call fragments (OpenAI-compatible and Messages API protocols) |
| `IsCompleted` | `bool` | `true` on the final chunk |

Chunks with no usable content are filtered out by the provider, so you only
receive chunks that carry text or the completion signal.

## How It Works

- The request is sent with `HttpCompletionOption.ResponseHeadersRead`, so
  processing starts as soon as headers arrive.
- The response body is parsed as server-sent events: `data:` lines are
  decoded until the stream ends or the `[DONE]` sentinel appears
  (OpenAI-style protocols); the KeyQuery protocol appends `alt=sse` and the
  Messages API protocol sets `stream: true` in the payload.
- Cancelling the `CancellationToken` stops reading immediately.

!!! note
    `StreamAsync` runs the same pre-flight checks as `ChatAsync` —
    a disabled or unconfigured provider throws `AiException` before any
    connection is opened.
