using AIProviderConnect.Constants;
using AIProviderConnect.Models;

namespace AIProviderConnect.Services;

/// <summary>
/// Resolves the plain-text form of a <see cref="ChatMessage"/> for wire-protocol fields that carry
/// text only (for example an Anthropic <c>system</c> or a Gemini <c>systemInstruction</c>).
/// </summary>
internal static class MessageTextResolver
{
    /// <summary>
    /// Returns <see cref="ChatMessage.Content"/> when no <see cref="ChatMessage.ContentParts"/> are
    /// present; otherwise concatenates the message's text parts. Image parts are skipped because a
    /// text-only field cannot represent them.
    /// </summary>
    internal static string ResolveText(ChatMessage message)
    {
        if (message.ContentParts is not { Count: > 0 })
            return message.Content ?? string.Empty;

        return string.Join(
            " ",
            message.ContentParts
                .Where(p => p.Type is not (ContentPartTypes.ImageUrl or ContentPartTypes.Image))
                .Select(p => p.Text ?? string.Empty));
    }

    /// <summary>
    /// Joins all system messages into a single instruction string separated by double newlines.
    /// Used by wire protocols that need a consolidated system instruction (e.g. Gemini, Anthropic).
    /// </summary>
    internal static string ResolveSystemInstruction(IEnumerable<ChatMessage> messages) =>
        string.Join(
            Environment.NewLine + Environment.NewLine,
            messages.Where(x => x.Role == EChatRole.System).Select(ResolveText));
}
