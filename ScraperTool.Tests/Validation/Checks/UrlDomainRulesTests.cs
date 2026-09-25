using FluentAssertions;

using ScraperTool.Services.Validation.Checks;

namespace ScraperTool.Tests.Validation.Checks;

public sealed class UrlDomainRulesTests
{
    [Theory]
    [InlineData("https://platform.openai.com/", "https://openai.com")]
    [InlineData("https://console.example.com/login", "https://example.com")]
    [InlineData("https://deep.example.com/v2", "https://example.com")]
    public void GetRootDomain_Subdomain_ReturnsRoot(string url, string expected)
    {
        var uri = new Uri(url);
        UrlDomainRules.GetRootDomain(uri).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://openai.com/")]
    [InlineData("https://www.perplexity.ai/")]
    [InlineData("http://localhost:1234/v1")]
    public void GetRootDomain_AlreadyRootOrNull_ReturnsNull(string url)
    {
        var uri = new Uri(url);
        UrlDomainRules.GetRootDomain(uri).Should().BeNull();
    }

    [Theory]
    [InlineData("www.example.com", "example.com")]
    [InlineData("WWW.Example.COM", "Example.COM")]
    [InlineData("example.com", "example.com")]
    public void StripWww_VariousInputs(string input, string expected)
    {
        UrlDomainRules.StripWww(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("http://localhost:8080/ui", true)]
    [InlineData("http://127.0.0.1:1234/v1", true)]
    [InlineData("http://10.0.0.1/api", true)]
    [InlineData("http://172.16.0.1/api", true)]
    [InlineData("http://192.168.1.1/api", true)]
    [InlineData("https://example.com", false)]
    [InlineData("https://api.openai.com", false)]
    public void IsPrivateUrl_VariousUris(string url, bool expected)
    {
        var uri = new Uri(url);
        UrlDomainRules.IsPrivateUrl(uri).Should().Be(expected);
    }
}
