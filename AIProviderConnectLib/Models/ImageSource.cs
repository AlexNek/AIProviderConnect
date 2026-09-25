namespace AIProviderConnect.Models;

/// <summary>
/// The resolved source of an <see cref="ImageContent"/>: either a URL string or raw base64 bytes
/// with a media type. Produced by <see cref="ImageContent.ResolveSource"/>.
/// </summary>
public abstract record ImageSource
{
    /// <summary>
    /// A URL-based image source (http(s) URL or data URI).
    /// </summary>
    public sealed record UrlSource(string Url) : ImageSource;

    /// <summary>
    /// A base64-encoded image source with raw bytes and a MIME type.
    /// </summary>
    public sealed record Base64Source(byte[] Data, string? MediaType) : ImageSource;
}
