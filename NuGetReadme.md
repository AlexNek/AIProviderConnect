# AIProviderConnect

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE.txt)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com)

Unified .NET library for connecting to **35+ AI providers** — OpenAI, Anthropic, Gemini, Ollama, Groq, Mistral, xAI, DeepSeek, Cohere and more — through a single `IAIProvider` interface, driven by an embedded JSON provider catalog and four protocol implementations.

## Features

- Single `IAIProvider` interface for all providers
- Runtime model discovery (`IModelDiscoveryProvider.GetModelsAsync`)
- Streaming via `IStreamingChatProvider` / `IAsyncEnumerable<StreamingChatChunk>`
- Tool / function calling
- Structured output via `ResponseFormat` with JSON schema
- Structured errors via `AiException` with centralized `AiErrorCodes`
- One-line DI registration (`AddAiProviders()`) with named options per provider ID
- `appsettings.json` configuration support

## Usage

```csharp
// Program.cs
builder.Services.AddHttpClient();
builder.Services.AddAiProviders();

builder.Services.Configure<OpenAICompatibleProviderOptions>("openai", o =>
{
    o.Enabled = true;
    o.BaseUrl = "https://api.openai.com/v1/";
    o.ApiKey = "your-api-key";
    o.DefaultModel = "gpt-4o";
});
```

```csharp
// Chat — pick a provider by ID
var provider = factory.GetProvider("openai");
var response = await provider.ChatAsync(new ChatCompletionRequest
{
    Model = "gpt-4o",
    Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hello!" }]
});

// Stream
if (provider is IStreamingChatProvider streaming)
{
    await foreach (var chunk in streaming.StreamAsync(request))
        Console.Write(chunk.Content);
}
```

Developer manual: [alexnek.github.io/AIProviderConnect](https://alexnek.github.io/AIProviderConnect/)
Full documentation and source: [github.com/AlexNek/AIProviderConnect](https://github.com/AlexNek/AIProviderConnect)

## Changelog

See [CHANGELOG.md](CHANGELOG.md)
