using System.Text.Json;

using FluentAssertions;

using Moq;

using ScraperTool.Models;
using ScraperTool.Services;
using ScraperTool.Services.Validation;
using ScraperTool.Services.Validation.Checks;

namespace ScraperTool.Tests.Validation.Checks;

public sealed class SelfHostedApplicabilityEvaluatorTests
{
    private readonly SelfHostedApplicabilityEvaluator _sut = new();

    [Fact]
    public void Evaluate_NonSelfHosted_ReturnsAllFalse()
    {
        var json = JsonDocument.Parse("""{"id":"test","category":"Cloud"}""");
        var sink = new Mock<IValidationIssueSink>();

        var result = _sut.Evaluate(json.RootElement, "test.json", sink.Object);

        result.LoginUrlIsNotApplicable.Should().BeFalse();
        result.SubscriptionPricingUrlIsNotApplicable.Should().BeFalse();
        result.ApiPricingUrlIsNotApplicable.Should().BeFalse();
        sink.Verify(s => s.FailWith(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Evaluate_SelfHostedWithLoginUrl_FilesFindingAndReturnsTrue()
    {
        var json = JsonDocument.Parse("""
            {"id":"test","category":"SelfHosted","loginUrl":"https://account.example.com/login"}
            """);
        var issues = new List<ValidationIssue>();
        var sink = new ValidationIssueSink(issues, progress: null);

        var result = _sut.Evaluate(json.RootElement, "test.json", sink);

        result.LoginUrlIsNotApplicable.Should().BeTrue();
        issues.Should().ContainSingle(i => i.Code == ValidationIssueCodes.LoginUrlNotApplicableForSelfHosted);
    }

    [Fact]
    public void Evaluate_SelfHostedWithLoginUrlAlreadyDash_ReturnsFalse()
    {
        var json = JsonDocument.Parse("""
            {"id":"test","category":"SelfHosted","loginUrl":"-"}
            """);
        var sink = new Mock<IValidationIssueSink>();

        var result = _sut.Evaluate(json.RootElement, "test.json", sink.Object);

        result.LoginUrlIsNotApplicable.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_SelfHostedWithSubscriptionUrl_FilesFindingAndReturnsTrue()
    {
        var json = JsonDocument.Parse("""
            {"id":"test","category":"SelfHosted","subscriptionPricingUrl":"https://plans.example.com"}
            """);
        var issues = new List<ValidationIssue>();
        var sink = new ValidationIssueSink(issues, progress: null);

        var result = _sut.Evaluate(json.RootElement, "test.json", sink);

        result.SubscriptionPricingUrlIsNotApplicable.Should().BeTrue();
        issues.Should().ContainSingle(i =>
            i.Code == ValidationIssueCodes.SubscriptionPricingUrlNotApplicableForSelfHosted);
    }

    [Fact]
    public void Evaluate_SelfHostedWithApiPricingUrl_FilesFindingAndReturnsTrue()
    {
        var json = JsonDocument.Parse("""
            {"id":"test","category":"SelfHosted","apiPricingUrl":"https://pricing.example.com"}
            """);
        var issues = new List<ValidationIssue>();
        var sink = new ValidationIssueSink(issues, progress: null);

        var result = _sut.Evaluate(json.RootElement, "test.json", sink);

        result.ApiPricingUrlIsNotApplicable.Should().BeTrue();
        issues.Should().ContainSingle(i =>
            i.Code == ValidationIssueCodes.ApiPricingUrlNotApplicableForSelfHosted);
    }
}
