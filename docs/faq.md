# FAQ

## Where are the `AddOpenAIProvider()` / `AddAnthropicProvider()` extensions?

They no longer exist. Registration is catalog-driven: one call to
`AddAiProviders()` registers every provider from the embedded catalog, and
you configure each one through **named options** keyed by provider ID:

```csharp
builder.Services.AddAiProviders();
builder.Services.Configure<OpenAICompatibleProviderOptions>("openai", o => { ... });
```

## Why do I get `ai/provider-disabled`?

`Enabled` defaults to `true` — a registered provider is enabled unless you
explicitly set `o.Enabled = false` (inline or bound from configuration). If
you see `ai/provider-disabled`, an explicit `Enabled = false` is the cause.

## Which providers work without a paid API key?

The self-hosted ones: `ollama`, `lmstudio`, `vllm`, `jan`, `localai`,
`textgenwebui`, `tgi`, `fastchat`. Point `BaseUrl` at the local server,
e.g. `http://localhost:11434/v1/` for Ollama. Note that OpenAI-compatible
providers still validate that `ApiKey` is non-empty — set any placeholder
value for keyless local servers.

## Why does `GetModelsAsync` throw?

Check `SupportsModelDiscovery` first. Providers whose catalog definition
has `hasModelDiscoveryApi: false` throw `AiException` with code
`ai/model-discovery-not-supported`. See
[Model Discovery](model-discovery/model-discovery.md).

## Does streaming work for every provider?

All four chat-family provider implementations implement `IStreamingChatProvider`, so
`StreamAsync` is always callable — but whether the backend actually serves
SSE depends on the service. Check `provider is IStreamingChatProvider`
before calling `StreamAsync`; fall back to `ChatAsync` where streaming is
not available.

## Can I send images or other multimodal content?

Yes. Set `ChatMessage.ContentParts` to a list of `ContentPart` values (text and
image parts). An image part carries an `ImageContent`, whose source can be a
URL, in-memory bytes, a stream, or a local file:

```csharp
var message = new ChatMessage
{
    Role = EChatRole.User,
    ContentParts =
    [
        new ContentPart { Type = "text", Text = "Describe this image." },
        new ContentPart
        {
            Type = "image_url",
            Image = ImageContent.FromFile("diagram.png")
            // or FromUrl(url), FromBytes(bytes, "image/png"), FromStream(stream, "image/png")
        }
    ]
};
```

The OpenAI-compatible protocol sends the image as a URL (encoding raw bytes as a
`data:` URI); the Messages API protocol sends a base64 or url `source` block; the
KeyQuery (Gemini) protocol sends an `inlineData` (base64) or `fileData` (URI) part.
When `ContentParts` is null or empty, `Content` is sent as a plain string, so
text-only callers are unaffected.

## Can I replace the `IAIProviderFactory` implementation?

`AddAiProviders()` registers `DefaultAIProviderFactory`, which resolves
providers from the container. If you need different behavior (key storage,
per-call key overrides, fallback order), implement `IAIProviderFactory` and
register it after `AddAiProviders()` to supersede the default.

## How do I know which protocol a provider uses?

Query the catalog:

```csharp
EProviderProtocol protocol = catalog.Get("gemini")!.Protocol;   // KeyQuery
```

Or read the `protocol` field in the corresponding `ai-providers/{id}.json`
file — the mapping table is in [Wire Protocols](concepts/wire-protocols.md).
