using System.Text.Json;

using AIProviderConnect.Models;

namespace AIProviderConnect.Protocols;

/// <summary>
/// Shared helper that extracts <see cref="UsageInfo"/> from a JSON response element.
/// Each wire protocol supplies its own property names for the usage node and token fields.
/// </summary>
internal static class UsageInfoParser
{
    /// <summary>
    /// Parses usage information from a JSON response.
    /// </summary>
    /// <param name="json">The root response JSON element.</param>
    /// <param name="usageNodeName">The name of the usage node (e.g. "usage" or "usageMetadata").</param>
    /// <param name="promptKey">The property name for prompt/input tokens.</param>
    /// <param name="completionKey">The property name for completion/output tokens.</param>
    /// <param name="totalKey">
    /// The property name for total tokens, or null when the protocol computes total as prompt + completion.
    /// </param>
    internal static UsageInfo Parse(
        JsonElement json,
        string usageNodeName,
        string promptKey,
        string completionKey,
        string? totalKey)
    {
        if (!json.TryGetProperty(usageNodeName, out var usageNode))
            return new UsageInfo();

        var prompt = usageNode.TryGetProperty(promptKey, out var promptProp)
                         ? promptProp.GetInt32()
                         : 0;
        var completion = usageNode.TryGetProperty(completionKey, out var completionProp)
                             ? completionProp.GetInt32()
                             : 0;

        var total = totalKey is not null && usageNode.TryGetProperty(totalKey, out var totalProp)
                        ? totalProp.GetInt32()
                        : prompt + completion;

        return new UsageInfo
        {
            PromptTokens = prompt,
            CompletionTokens = completion,
            TotalTokens = total
        };
    }
}
