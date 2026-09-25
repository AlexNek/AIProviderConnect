using System.Text.Json;

using AIProviderConnect.Constants;
using AIProviderConnect.Models;

namespace AIProviderConnect.Protocols;

/// <summary>
/// Stateful parser for Messages API streaming SSE chunks.
/// Tracks active tool-use content blocks by index and emits <see cref="StreamingChatChunk"/>
/// items for both text deltas and incremental tool-call argument fragments.
/// </summary>
public sealed class MessagesApiStreamingParser
{
    private readonly Dictionary<int, ToolUseBlock> _activeToolBlocks = new();

    /// <summary>
    /// Parses a single SSE data payload (already deserialized as <see cref="JsonElement"/>)
    /// and returns the corresponding <see cref="StreamingChatChunk"/>, or <c>null</c> when
    /// the event type is not relevant to the consumer.
    /// </summary>
    public StreamingChatChunk? ParseStreamChunk(JsonElement json)
    {
        if (!json.TryGetProperty(MessagesApiPropertyNames.Type, out var typeNode))
        {
            return null;
        }

        var type = typeNode.GetString();

        if (type == MessagesApiPropertyNames.ContentBlockStart)
        {
            HandleContentBlockStart(json);
            return null;
        }

        if (type == MessagesApiPropertyNames.ContentBlockDelta)
        {
            return HandleContentBlockDelta(json);
        }

        if (type == MessagesApiPropertyNames.ContentBlockStop)
        {
            HandleContentBlockStop(json);
            return null;
        }

        if (type == MessagesApiPropertyNames.MessageStop)
        {
            return new StreamingChatChunk { IsCompleted = true };
        }

        return null;
    }

    private void HandleContentBlockStart(JsonElement json)
    {
        if (!json.TryGetProperty(MessagesApiPropertyNames.Index, out var indexNode))
            return;

        var index = indexNode.GetInt32();

        if (!json.TryGetProperty("content_block", out var block))
            return;

        if (!block.TryGetProperty(MessagesApiPropertyNames.Type, out var blockTypeNode))
            return;

        if (blockTypeNode.GetString() != MessagesApiPropertyNames.ToolUse)
            return;

        var id = block.TryGetProperty(MessagesApiPropertyNames.Id, out var idNode)
            ? idNode.GetString() ?? string.Empty
            : string.Empty;

        var name = block.TryGetProperty(MessagesApiPropertyNames.Name, out var nameNode)
            ? nameNode.GetString() ?? string.Empty
            : string.Empty;

        _activeToolBlocks[index] = new ToolUseBlock(id, name);
    }

    private StreamingChatChunk? HandleContentBlockDelta(JsonElement json)
    {
        if (!json.TryGetProperty(MessagesApiPropertyNames.Delta, out var delta))
            return null;

        if (!delta.TryGetProperty(MessagesApiPropertyNames.Type, out var deltaTypeNode))
            return null;

        var deltaType = deltaTypeNode.GetString();

        // Text delta — existing behavior
        if (deltaType == MessagesApiPropertyNames.Text &&
            delta.TryGetProperty(MessagesApiPropertyNames.Text, out var textNode))
        {
            return new StreamingChatChunk { Content = textNode.GetString() ?? string.Empty };
        }

        // Tool-call argument fragment
        if (deltaType == MessagesApiPropertyNames.InputJsonDelta)
        {
            if (!json.TryGetProperty(MessagesApiPropertyNames.Index, out var indexNode))
                return null;

            var index = indexNode.GetInt32();

            var fragment = delta.TryGetProperty(MessagesApiPropertyNames.PartialJson, out var fragmentNode)
                ? fragmentNode.GetString() ?? string.Empty
                : string.Empty;

            // Resolve id/name from the registered block (first fragment carries them)
            string? id = null;
            string? name = null;

            if (_activeToolBlocks.TryGetValue(index, out var block) && !block.HasEmittedIdentity)
            {
                id = block.Id;
                name = block.Name;
                block.HasEmittedIdentity = true;
            }

            return new StreamingChatChunk
            {
                ToolCalls = new[]
                {
                    new StreamingToolCallDelta
                    {
                        Index = index,
                        Id = id,
                        Name = name,
                        ArgumentsFragment = fragment
                    }
                }
            };
        }

        return null;
    }

    private void HandleContentBlockStop(JsonElement json)
    {
        if (!json.TryGetProperty(MessagesApiPropertyNames.Index, out var indexNode))
            return;

        _activeToolBlocks.Remove(indexNode.GetInt32());
    }

    private sealed class ToolUseBlock
    {
        public ToolUseBlock(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Id { get; }

        public string Name { get; }

        public bool HasEmittedIdentity { get; set; }
    }
}
