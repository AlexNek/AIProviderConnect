# Chat Completions

`IAIProvider.ChatAsync` is the core operation: send a conversation, get one
completion back.

```csharp
Task<ChatCompletionResponse> ChatAsync(
    ChatCompletionRequest request,
    CancellationToken cancellationToken = default);
```

## ChatCompletionRequest

| Property | Type | Default | Notes |
| --- | --- | --- | --- |
| `Model` | `string` | *required* | Model ID to complete with |
| `Messages` | `IReadOnlyList<ChatMessage>` | empty | The conversation |
| `Temperature` | `float` | `0.7` | Sampling temperature (0.0–2.0) |
| `MaxTokens` | `int?` | `null` | Required for Messages API providers; the API rejects requests that omit it |
| `Tools` | `IReadOnlyList<ToolDefinition>?` | `null` | Functions the model may call — see [Tool Calling](tool-calling.md) |
| `ResponseFormat` | `ResponseFormat?` | `null` | Format constraint, e.g. structured JSON output |

### Messages and Roles

A `ChatMessage` has `Role` (`EChatRole`), `Content`, and — for tool round
trips — `ToolCalls` and `ToolCallId`:

| `EChatRole` | Purpose |
| --- | --- |
| `System` | Instructions; folded into `system`/`systemInstruction` by the Messages API and KeyQuery protocols |
| `User` | End-user input |
| `Assistant` | Prior model output (may carry `ToolCalls`) |
| `Tool` | Tool result; must set `ToolCallId` |

Messages are text-only by default (`Content` is a `string`).

### Multimodal Content

To send images alongside text, set `ContentParts` — a list of `ContentPart`
values of type `"text"` or `"image_url"`. When `ContentParts` is non-empty it
takes precedence over `Content`.

An image part carries an `ImageContent`, whose source can be any of:

| Factory | Source |
| --- | --- |
| `ImageContent.FromUrl(url, detail?)` | http(s) URL or `data:` URI |
| `ImageContent.FromBytes(bytes, mediaType, detail?)` | In-memory bytes |
| `ImageContent.FromStream(stream, mediaType, detail?)` | A `Stream` |
| `ImageContent.FromFile(path, mediaType?, detail?)` | Local file (media type inferred from extension) |

```csharp
new ChatMessage
{
    Role = EChatRole.User,
    ContentParts =
    [
        new ContentPart { Type = "text", Text = "Describe this image." },
        new ContentPart { Type = "image_url", Image = ImageContent.FromFile("diagram.png") }
    ]
}
```

The OpenAI-compatible protocol serializes the image as an `image_url` (raw bytes
become a `data:` URI); the Messages API protocol serializes it as a `base64` or
`url` `source` block; the KeyQuery (Gemini) protocol serializes it as an
`inlineData` (base64) or `fileData` (URI) part.

### Structured Output

`ResponseFormat` constrains the response shape:

```csharp
request.ResponseFormat = new ResponseFormat
{
    Type = "json_schema",
    JsonSchema = mySchemaDefinition
};
```

`Type` defaults to `"text"`. A `json_schema` format with a
`JsonSchemaDefinition` (`Name`, `Schema`, `Strict`) is transmitted by the
OpenAI-compatible protocols; `JsonSchemaDefinition.Strict` defaults to `true`.

## ChatCompletionResponse

| Property | Type | Description |
| --- | --- | --- |
| `Id` | `string` | Provider response ID (generated locally for KeyQuery responses) |
| `Model` | `string` | Model that produced the completion |
| `Content` | `string` | Generated text |
| `ReasoningContent` | `string?` | Chain-of-thought reasoning content (some providers) |
| `FinishReason` | `string?` | Why generation stopped (e.g. `stop`, `tool_calls`) |
| `ToolCalls` | `IReadOnlyList<ToolCall>?` | Pending tool invocations, if any |
| `Usage` | `UsageInfo` | Token accounting |

`UsageInfo` exposes `PromptTokens`, `CompletionTokens`, and `TotalTokens`.
