using AIProviderConnect.Providers;

using FluentAssertions;

namespace AIProviderConnectLib.Tests.Providers;

/// <summary>
/// The single primitive the loopback allowance is built on: the BCL loopback predicate restricted to
/// http and https, so no hand-rolled host list can drift from the framework.
/// </summary>
public class LoopbackUrlTests
{
    [Theory]
    [InlineData("http://localhost:1234/v1/")]
    [InlineData("http://127.0.0.1:8090/v1/systemone")]
    [InlineData("http://[::1]:8080/api/decide")]
    [InlineData("https://localhost/v1/")]
    public void IsLoopback_LoopbackHosts_AreAccepted(string url)
    {
        LoopbackUrl.IsLoopback(url).Should().BeTrue();
    }

    [Theory]
    [InlineData("http://test.example.com/v1/")]
    [InlineData("https://test.example.com/v1/")]
    [InlineData("http://10.0.0.5:8080/v1/")]
    public void IsLoopback_RoutableHosts_AreRejected(string url)
    {
        LoopbackUrl.IsLoopback(url).Should().BeFalse();
    }

    [Theory]
    [InlineData("ftp://localhost/")]
    [InlineData("file://localhost/etc/hosts")]
    public void IsLoopback_NonHttpScheme_IsRejected(string url)
    {
        LoopbackUrl.IsLoopback(url).Should().BeFalse();
    }

    [Theory]
    [InlineData("/v1/chat/completions")]
    [InlineData("localhost:1234")]
    [InlineData("")]
    [InlineData(null)]
    public void IsLoopback_NonAbsoluteValue_IsRejected(string? url)
    {
        LoopbackUrl.IsLoopback(url).Should().BeFalse();
    }

    [Fact]
    public void IsLoopback_UriOverload_AcceptsLoopback()
    {
        LoopbackUrl.IsLoopback(new Uri("http://[::1]:8080/api/decide")).Should().BeTrue();
    }

    [Fact]
    public void IsLoopback_UriOverload_RejectsRoutableHttp()
    {
        LoopbackUrl.IsLoopback(new Uri("http://test.example.com/")).Should().BeFalse();
    }
}
