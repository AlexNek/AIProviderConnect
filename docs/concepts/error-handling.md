# Error Handling

AIProviderConnect reports failures by throwing `AiException`
(`AIProviderConnect.Exceptions`). Every exception carries a stable machine-
readable `Code` plus a human-readable message.

```csharp
using AIProviderConnect.Exceptions;

try
{
    var response = await provider.ChatAsync(request);
}
catch (AiException ex)
{
    if (ex.Code == AiErrorCodes.RateLimited)
    {
        // back off and retry
    }
}
```

## Pre-flight Errors

Before any HTTP request is sent, each provider validates its configuration:

| Condition | Error code |
| --- | --- |
| Options `Enabled` is `false` | `ai/provider-disabled` |
| `BaseUrl` is empty | `ai/no-base-url` |
| `ApiKey` is empty (providers that require one) | `ai/no-api-key` |

## HTTP Status Mapping

Non-success responses are translated by `AIProviderBase.ThrowIfErrorAsync`:

| HTTP status | Error code |
| --- | --- |
| 400 | `ai/invalid-request` |
| 401 | `ai/unauthorized` |
| 403 | `ai/forbidden` |
| 404 | `ai/endpoint-not-found` |
| 429 | `ai/rate-limited` |
| 500 and above | `ai/no-server` |
| Any other error status | `ai/provider-call-failed` |

If the error body contains an OpenAI-style `{ "error": { "message": ... } }`
payload, that message is appended to the exception text.

## Network Errors

Network-level failures (DNS failure, connection refused, TLS error, HttpClient
timeout) are translated to `AiException` at all three transport paths — chat
(`SendChatAndParseAsync`), model discovery (`SendGetModelsAndParseAsync`), and
streaming (`StreamCoreAsync`):

| Exception | Error code |
| --- | --- |
| `HttpRequestException` | `ai/no-connection` |
| `TaskCanceledException` (not user-initiated) | `ai/timeout` |

User-initiated cancellation (`CancellationToken` cancelled by the caller)
propagates as `OperationCanceledException` unchanged. Translated failures
participate in the Polly retry pipeline when `MaxRetryCount > 0`.

## All Error Codes

| Constant | Value | Meaning |
| --- | --- | --- |
| `ConfigurationError` | `ai/configuration-error` | Invalid provider configuration |
| `EndpointNotFound` | `ai/endpoint-not-found` | Requested endpoint not found (404) |
| `Forbidden` | `ai/forbidden` | Key lacks permissions (403) |
| `InvalidRequest` | `ai/invalid-request` | Invalid request (400) |
| `ModelDiscoveryNotSupported` | `ai/model-discovery-not-supported` | `GetModelsAsync` called on a provider without a discovery API |
| `NoApiKey` | `ai/no-api-key` | API key not configured |
| `NoBaseUrl` | `ai/no-base-url` | Base URL not configured |
| `NoConnection` | `ai/no-connection` | Could not connect to the provider |
| `NoServer` | `ai/no-server` | Provider server unavailable (5xx) |
| `ProviderCallFailed` | `ai/provider-call-failed` | Generic call failure |
| `ProviderDisabled` | `ai/provider-disabled` | Provider not enabled in options |
| `ProviderError` | `ai/provider-error` | Provider returned an error |
| `ProviderMissingConfiguration` | `ai/provider-missing-configuration` | Retained for compatibility; built-in providers now emit `NoBaseUrl` or `NoApiKey` |
| `ProviderNotFound` | `ai/provider-not-found` | Requested provider has not been registered |
| `RateLimited` | `ai/rate-limited` | Provider throttled the request (429) |
| `Timeout` | `ai/timeout` | Request timed out |
| `Unauthorized` | `ai/unauthorized` | Invalid credentials (401) |

!!! tip
    Switch on `ex.Code` (a `string`) rather than parsing the message —
    messages are for humans, codes are for logic.
