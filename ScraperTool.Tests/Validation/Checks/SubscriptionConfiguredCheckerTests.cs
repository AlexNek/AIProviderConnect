using System.Text.Json;

using FluentAssertions;

using Moq;

using ScraperTool.Models;
using ScraperTool.Services;
using ScraperTool.Services.Validation;
using ScraperTool.Services.Validation.Checks;

namespace ScraperTool.Tests.Validation.Checks;

public sealed class SubscriptionConfiguredCheckerTests
{
    private readonly SubscriptionConfiguredChecker _sut = new();

    [Fact]
    public async Task ValidateAsync_HasSubscriptionUrl_DoesNothing()
    {
        var json = JsonDocument.Parse("""
            {"id":"test","subscriptionPricingUrl":"https://pricing.example.com"}
            """);
        var sink = new Mock<IValidationIssueSink>();

        await _sut.ValidateAsync(json.RootElement, "test.json", sink.Object);

        sink.Verify(s => s.Append(It.IsAny<ValidationIssue>()), Times.Never);
    }

    [Fact]
    public async Task ValidateAsync_SelfHosted_DoesNothing()
    {
        var json = JsonDocument.Parse("""{"id":"test","category":"SelfHosted"}""");
        var sink = new Mock<IValidationIssueSink>();

        await _sut.ValidateAsync(json.RootElement, "test.json", sink.Object);

        sink.Verify(s => s.Append(It.IsAny<ValidationIssue>()), Times.Never);
    }

    [Fact]
    public async Task ValidateAsync_MissingSubscriptionUrl_FilesIssue()
    {
        var json = JsonDocument.Parse("""{"id":"test"}""");
        var issues = new List<ValidationIssue>();
        var sink = new ValidationIssueSink(issues, progress: null);

        await _sut.ValidateAsync(json.RootElement, "test.json", sink);

        issues.Should().ContainSingle(i =>
            i.Code == ValidationIssueCodes.MissingSubscriptionPricingUrl
            && i.Field == ProviderJsonFields.SubscriptionPricingUrl);
    }
}
