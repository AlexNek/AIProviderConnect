# Embeddings

Embeddings are an optional provider capability exposed through a separate
interface:

```csharp
public interface IEmbeddingProvider
{
    Task<EmbeddingResponse> EmbedAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default);
}
```

Only providers that serve an `/embeddings` endpoint implement the interface.
Check at the call site:

```csharp
if (provider is IEmbeddingProvider embeddingProvider)
{
    var response = await embeddingProvider.EmbedAsync(request, ct);
    // response.Data[i].Embedding is the float[] vector for input[i]
}
```

## EmbeddingRequest

| Property | Type | Default | Notes |
| --- | --- | --- | --- |
| `Model` | `string` | `""` | Embedding model ID; falls back to `DefaultEmbeddingModel` when empty |
| `Input` | `IReadOnlyList<string>` | *required* | One or more text strings to embed |

## EmbeddingResponse

| Property | Type | Description |
| --- | --- | --- |
| `Data` | `IReadOnlyList<EmbeddingData>` | One entry per input string, ordered by `Index` |
| `Model` | `string` | Model ID that produced the vectors |
| `Usage` | `EmbeddingUsage` | Token accounting for the batch |

### EmbeddingData

| Property | Type | Description |
| --- | --- | --- |
| `Index` | `int` | Position in the input batch |
| `Embedding` | `float[]` | The embedding vector |

`Data` is always re-sorted by `Index` before returning, so the caller
receives vectors in input order regardless of provider response order.

### EmbeddingUsage

| Property | Type | Description |
| --- | --- | --- |
| `PromptTokens` | `int` | Input tokens consumed by the batch |

## Configuring the Embedding Model

The embedding model is configured separately from the chat model via
`DefaultEmbeddingModel` on the provider's named options:

```csharp
services.Configure<OpenAICompatibleProviderOptions>("openai", options =>
{
    options.BaseUrl = "https://api.example.com/v1";
    options.ApiKey = "fake-api-key";
    options.DefaultEmbeddingModel = "text-embedding-3-small";
});
```

Resolution order:

1. `EmbeddingRequest.Model` — when non-empty, used directly.
2. `DefaultEmbeddingModel` from options — used when the request model is empty.
3. When both are empty, `EmbedAsync` throws
   `AiException(AiErrorCodes.EmbeddingModelNotConfigured)`.

## Error Codes

| Code | Meaning |
| --- | --- |
| `ai/embedding-failed` | The provider returned an error response (HTTP 400/404/422 from the embeddings endpoint, or a malformed response body) |
| `ai/embedding-model-not-configured` | Both `Model` and `DefaultEmbeddingModel` are empty |
| `ai/invalid-request` | `Input` is null, empty, or contains only whitespace strings |

## Provider Support

The OpenAI-compatible wire protocol (`OpenAICompatibleProvider`) supports
embeddings via `POST {BaseUrl}{EmbeddingsEndpoint}` (default `embeddings`).
The `HybridGateway` protocol is served by the same provider type and
supports embeddings identically.

`MessagesApiProvider`, `KeyQueryProvider`, and `ModelCatalogProvider` do not
implement `IEmbeddingProvider`.
