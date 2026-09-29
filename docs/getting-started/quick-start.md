# Quick Start

This walkthrough registers every catalog provider, configures OpenAI, and
sends one chat request.

## 1. Register Providers

`AddAiProviders()` registers one `IAIProvider` singleton per catalog entry,
keyed by provider ID. Providers resolve a plain `HttpClient` from the
container, so add HTTP services as well:

```csharp
using AIProviderConnect.DependencyInjection;

builder.Services.AddHttpClient();
builder.Services.AddAiProviders();
```

`AddHttpClient()` comes from the `Microsoft.Extensions.Http` package. As an
alternative, register an `HttpClient` instance directly — providers only
need `HttpClient` to be resolvable from the container.

## 2. Configure a Provider

Each provider has **named options** under its catalog ID
(`"openai"`, `"anthropic"`, `"ollama"`, ...). The options class depends on
the provider's [wire protocol](../concepts/wire-protocols.md) — OpenAI is
OpenAI-compatible:

```csharp
using AIProviderConnect.Options;

builder.Services.Configure<OpenAICompatibleProviderOptions>("openai", o =>
{
    o.Enabled = true;
    o.BaseUrl = "https://api.openai.com/v1/";
    o.ApiKey = "fake-api-key";
    o.DefaultModel = "gpt-4o";
});
```

!!! note
    A provider must be `Enabled = true` and have a non-empty `BaseUrl`
    (and `ApiKey` where required), otherwise calls throw `AiException`
    before any HTTP traffic is generated.

## 3. Pick a Provider and Chat

All providers are registered as `IAIProvider`, so resolve the factory
and pick by ID:

```csharp
using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;

public class MyService(IAIProviderFactory factory)
{
    public async Task<string> AskAsync(string question)
    {
        var provider = factory.GetProvider("openai");

        var response = await provider.ChatAsync(new ChatCompletionRequest
        {
            Model = "gpt-4o",                       // required on every request
            Messages = [new ChatMessage { Role = EChatRole.User, Content = question }]
        });

        return response.Content;
    }
}
```

`ChatCompletionResponse` also carries `FinishReason`, `Model`, `Usage`
(token counts), and `ToolCalls` when the model invoked tools.

## 4. Streaming

Providers that stream implement `IStreamingChatProvider` — all four
chat-family implementations do (`DecisionProvider` is not a chat provider):

```csharp
if (provider is IStreamingChatProvider streaming)
{
    await foreach (var chunk in streaming.StreamAsync(request))
    {
        Console.Write(chunk.Content);
    }
}
```

## 5. Runtime Model Discovery

```csharp
if (provider is IModelDiscoveryProvider discovery && discovery.SupportsModelDiscovery)
{
    IReadOnlyList<AIModel> models = await discovery.GetModelsAsync();
}
```

## Next Steps

- [Dependency Injection](dependency-injection.md) — how registration and
  named options work in detail.
- [Provider Catalog](../concepts/provider-catalog.md) — browse the
  built-in provider definitions.
- [Error Handling](../concepts/error-handling.md) — what the error codes mean.
