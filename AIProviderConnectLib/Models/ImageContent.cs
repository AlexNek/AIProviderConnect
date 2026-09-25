using AIProviderConnect.Constants;

namespace AIProviderConnect.Models;

/// <summary>
/// An image attached to a multimodal <see cref="ContentPart"/>. The image source is flexible:
/// a remote http(s) URL, an already-encoded <c>data:</c> URI, in-memory bytes, a
/// <see cref="Stream"/>, or a local file. Each wire protocol serializes the source in its own
/// format — the OpenAI-compatible protocol emits a URL (encoding <see cref="Data"/> as a
/// <c>data:</c> URI), while the Messages API emits a <c>base64</c> or <c>url</c> source object.
/// </summary>
public sealed record ImageContent
{
    private static readonly Dictionary<string, string> MediaTypeByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = MediaTypes.ImagePng,
            [".jpg"] = MediaTypes.ImageJpeg,
            [".jpeg"] = MediaTypes.ImageJpeg,
            [".gif"] = MediaTypes.ImageGif,
            [".webp"] = MediaTypes.ImageWebP
        };

    /// <summary>
    /// Gets the image URL — an http(s) URL or an already-built <c>data:</c> URI.
    /// Ignored when <see cref="Data"/> is non-empty.
    /// </summary>
    public string? Url { get; init; }

    /// <summary>
    /// Gets the raw image bytes for an in-memory, stream, or file source.
    /// Takes precedence over <see cref="Url"/> when non-empty.
    /// </summary>
    public byte[]? Data { get; init; }

    /// <summary>
    /// Gets the MIME type of <see cref="Data"/> (e.g. "image/png").
    /// Required when <see cref="Data"/> is used.
    /// </summary>
    public string? MediaType { get; init; }

    /// <summary>
    /// Gets the optional detail hint ("low", "high", "auto").
    /// Only transmitted by providers that support it.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>
    /// Creates an image source from an http(s) URL or a pre-built <c>data:</c> URI.
    /// </summary>
    /// <param name="url">The image URL or data URI.</param>
    /// <param name="detail">Optional detail hint.</param>
    public static ImageContent FromUrl(string url, string? detail = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(url);
        return new ImageContent { Url = url, Detail = detail };
    }

    /// <summary>
    /// Creates an image source from in-memory bytes and an explicit media type.
    /// </summary>
    /// <param name="data">The raw image bytes.</param>
    /// <param name="mediaType">The MIME type of the bytes (e.g. "image/png").</param>
    /// <param name="detail">Optional detail hint.</param>
    public static ImageContent FromBytes(byte[] data, string mediaType, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrEmpty(mediaType);

        if (data.Length == 0)
            throw new ArgumentException("Image data must not be empty.", nameof(data));

        return new ImageContent { Data = data, MediaType = mediaType, Detail = detail };
    }

    /// <summary>
    /// Creates an image source by reading a <see cref="Stream"/> to completion.
    /// </summary>
    /// <param name="stream">The stream containing the image bytes.</param>
    /// <param name="mediaType">The MIME type of the bytes (e.g. "image/png").</param>
    /// <param name="detail">Optional detail hint.</param>
    public static ImageContent FromStream(Stream stream, string mediaType, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrEmpty(mediaType);

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return FromBytes(buffer.ToArray(), mediaType, detail);
    }

    /// <summary>
    /// Creates an image source by reading a local file. The media type is inferred from the
    /// file extension (.png, .jpg, .jpeg, .gif, .webp) unless supplied explicitly.
    /// </summary>
    /// <param name="path">The path to the image file.</param>
    /// <param name="mediaType">Optional MIME type override; inferred from the extension when null.</param>
    /// <param name="detail">Optional detail hint.</param>
    public static ImageContent FromFile(string path, string? mediaType = null, string? detail = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!File.Exists(path))
            throw new FileNotFoundException("The image file was not found.", path);

        var resolved = mediaType;
        if (string.IsNullOrEmpty(resolved))
        {
            var extension = Path.GetExtension(path);
            if (!MediaTypeByExtension.TryGetValue(extension, out resolved))
                throw new ArgumentException(
                    $"Cannot infer the media type for extension '{extension}'. " +
                    "Pass mediaType explicitly.",
                    nameof(path));
        }

        return FromBytes(File.ReadAllBytes(path), resolved, detail);
    }

    /// <summary>
    /// Resolves the image source to a discriminated result: base64 bytes with media type when
    /// <see cref="Data"/> is present, otherwise the URL string. Encapsulates the <see cref="Data"/>
    /// vs <see cref="Url"/> priority check so protocols do not re-implement it.
    /// </summary>
    public ImageSource ResolveSource()
    {
        if (Data is { Length: > 0 })
            return new ImageSource.Base64Source(Data, MediaType);

        return new ImageSource.UrlSource(Url ?? string.Empty);
    }

    /// <summary>
    /// Resolves the value to send for URL-style protocols: <see cref="Data"/> encoded as a
    /// <c>data:</c> URI when present, otherwise <see cref="Url"/>.
    /// </summary>
    /// <returns>The URL or data URI string, or an empty string when no source is set.</returns>
    public string ResolveUrl()
    {
        if (Data is { Length: > 0 })
        {
            var mediaType = string.IsNullOrEmpty(MediaType) ? "application/octet-stream" : MediaType;
            return $"data:{mediaType};base64,{Convert.ToBase64String(Data)}";
        }

        return Url ?? string.Empty;
    }
}
