using AIProviderConnect.Constants;

namespace AIProviderConnect.Models;

/// <summary>
/// A single content part within a chat message (text or image).
/// </summary>
public sealed record ContentPart
{
    /// <summary>
    /// Part type: "text" or "image_url" (the "image" alias is also accepted for image parts).
    /// See <see cref="ContentPartTypes"/> for the canonical values.
    /// </summary>
    public string Type { get; init; } = ContentPartTypes.Text;

    /// <summary>
    /// Text content (when <see cref="Type"/> is "text").
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Image content (when <see cref="Type"/> is "image_url" or "image"). The image source may be a
    /// URL, in-memory bytes, a stream, or a local file — see <see cref="ImageContent"/>.
    /// </summary>
    public ImageContent? Image { get; init; }
}
