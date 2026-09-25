# Tool Calling

Providers on the OpenAI-compatible, Messages API (Anthropic), and KeyQuery
(Gemini) protocols support function calling: declare tools on the
request, execute what the model asks for, and send the results back.

## 1. Define Tools

A `ToolDefinition` is a name, a description, and a raw JSON schema for the
parameters:

```csharp
var weatherTool = new ToolDefinition
{
    Name = "get_weather",
    Description = "Returns the current weather for a city.",
    Parameters = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "city": { "type": "string" }
          },
          "required": ["city"]
        }
        """).RootElement
};
```

Attach the tools to the request:

```csharp
var request = new ChatCompletionRequest
{
    Model = "gpt-4o",
    Messages = [new ChatMessage { Role = EChatRole.User, Content = "Weather in Berlin?" }],
    Tools = [weatherTool]
};
```

## 2. Handle Requested Calls

When the model wants to call a tool, `ChatCompletionResponse.ToolCalls` is
populated and `FinishReason` typically reads `tool_calls`:

```csharp
var response = await provider.ChatAsync(request);

if (response.ToolCalls is { Count: > 0 })
{
    var call = response.ToolCalls[0];
    // call.Id        — correlation ID for the reply message
    // call.Name      — "get_weather"
    // call.Arguments — JSON string, e.g. {"city":"Berlin"}
}
```

`Arguments` is unparsed JSON — deserialize it yourself.

## 3. Send Results Back

Append the assistant message that carried the tool calls, then one
`EChatRole.Tool` message per result, linked by `ToolCallId`:

```csharp
request = request with
{
    Messages =
    [
        ..request.Messages,
        new ChatMessage
        {
            Role = EChatRole.Assistant,
            ToolCalls = response.ToolCalls
        },
        new ChatMessage
        {
            Role = EChatRole.Tool,
            ToolCallId = call.Id,
            Content = """{"temperature_c": 18, "condition": "cloudy"}"""
        }
    ]
};

var final = await provider.ChatAsync(request);
```

!!! note
    Tool calling is supported on all three protocol families:
    OpenAI-compatible, Messages API (Anthropic), and KeyQuery (Gemini).
