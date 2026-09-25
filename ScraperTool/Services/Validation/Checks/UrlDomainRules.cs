namespace ScraperTool.Services.Validation.Checks;

/// <summary>
/// Pure domain utilities for root-domain extraction, www-prefix stripping,
/// and private-network detection. Shared by the field checker's gates and
/// the website-ownership judge.
/// </summary>
public static class UrlDomainRules
{
    /// <summary>
    /// Extracts the root domain URL from a URI that may be on a subdomain.
    /// Returns null if the URI is already at the root domain.
    /// Example: https://platform.openai.com/ → https://openai.com
    /// </summary>
    public static string? GetRootDomain(Uri uri)
    {
        var host = uri.Host;
        var parts = host.Split('.');

        if (parts.Length < 3)
            return null;

        // "www" is a conventional prefix, not a meaningful subdomain.
        // www.perplexity.ai is semantically equivalent to perplexity.ai.
        if (string.Equals(parts[0], "www", StringComparison.OrdinalIgnoreCase))
            return null;

        var rootHost = parts.Length > 3 && parts[^2].Length <= 3 && parts[^1].Length <= 2
                           ? string.Join(".", parts[^3..])
                           : string.Join(".", parts[^2..]);

        if (string.Equals(rootHost, host, StringComparison.OrdinalIgnoreCase))
            return null;

        return $"{uri.Scheme}://{rootHost}";
    }

    /// <summary>
    /// Strips the conventional "www." prefix from a host name.
    /// </summary>
    public static string StripWww(string host) =>
        host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? host.Substring(4)
            : host;

    /// <summary>
    /// Returns true when the URI points at a loopback or RFC-1918 address.
    /// </summary>
    public static bool IsPrivateUrl(Uri uri)
    {
        if (uri.IsLoopback) return true;

        if (uri.HostNameType == UriHostNameType.IPv4)
        {
            var ip = System.Net.IPAddress.Parse(uri.Host);
            var bytes = ip.GetAddressBytes();
            return bytes[0] == 10
                   || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                   || (bytes[0] == 192 && bytes[1] == 168);
        }

        return false;
    }
}
