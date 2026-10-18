namespace AIProviderConnect.Providers;

/// <summary>
/// Recognizes loopback addresses, which are the only hosts allowed to be reached over plain
/// <c>http</c>. Wraps <see cref="Uri.IsLoopback"/> rather than hand-rolling a host list, so
/// <c>localhost</c>, <c>127.0.0.1</c>, and <c>[::1]</c> are all covered by the framework primitive.
/// </summary>
internal static class LoopbackUrl
{
    /// <summary>
    /// Returns true when <paramref name="uri"/> is an absolute <c>http</c> or <c>https</c> URI whose
    /// host is a loopback address. A non-HTTP scheme is never a loopback transport for this library.
    /// </summary>
    internal static bool IsLoopback(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri)
            return false;

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return uri.IsLoopback;
    }

    /// <summary>
    /// Returns true when <paramref name="value"/> parses as an absolute <c>http</c> or <c>https</c>
    /// URI whose host is a loopback address. A missing or unparsable value is not a loopback address;
    /// the base-URL validation reports that case instead.
    /// </summary>
    internal static bool IsLoopback(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && IsLoopback(uri);
}
