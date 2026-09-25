using FluentAssertions;

using ScraperTool.Services.UrlResearch.DecisionTree.TemplateResolution;

namespace ScraperTool.Tests;

public class DecisionTreeTemplateResolverTests
{
    private readonly DecisionTreeTemplateResolver _resolver = new();

    [Fact]
    public void Resolve_ReplacesSinglePlaceholder()
    {
        var json = """{"task": "Check {provider} pricing"}""";
        var parameters = new Dictionary<string, string> { ["provider"] = "OpenAI" };

        var result = _resolver.Resolve(json, parameters);

        result.Should().Be("""{"task": "Check OpenAI pricing"}""");
    }

    [Fact]
    public void Resolve_ReplacesMultiplePlaceholders()
    {
        var json = """{"task": "Check {provider} at {baseUrl} for {fieldKind}"}""";
        var parameters = new Dictionary<string, string>
        {
            ["provider"] = "Anthropic",
            ["baseUrl"] = "https://api.test.example.com",
            ["fieldKind"] = "modelsEndpoint"
        };

        var result = _resolver.Resolve(json, parameters);

        result.Should().Be("""{"task": "Check Anthropic at https://api.test.example.com for modelsEndpoint"}""");
    }

    [Fact]
    public void Resolve_WithEmptyParameters_ReturnsOriginalJson()
    {
        var json = """{"task": "Check {provider}"}""";

        var result = _resolver.Resolve(json, new Dictionary<string, string>());

        result.Should().Be(json);
    }

    [Fact]
    public void Resolve_WithNullParameters_ReturnsOriginalJson()
    {
        var json = """{"task": "Check {provider}"}""";

        var result = _resolver.Resolve(json, null!);

        result.Should().Be(json);
    }

    [Fact]
    public void Resolve_LeavesUnknownPlaceholdersIntact()
    {
        var json = """{"task": "Check {provider} and {unknown}"}""";
        var parameters = new Dictionary<string, string> { ["provider"] = "OpenAI" };

        var result = _resolver.Resolve(json, parameters);

        result.Should().Be("""{"task": "Check OpenAI and {unknown}"}""");
    }

    [Fact]
    public void Resolve_ThrowsOnNullOrWhiteSpaceJson()
    {
        var parameters = new Dictionary<string, string>();

        var act = () => _resolver.Resolve("", parameters);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildParameters_IncludesAllStandardKeys()
    {
        var result = DecisionTreeTemplateResolver.BuildParameters(
            "OpenAI",
            "https://test.example.com",
            "subscriptionPricingUrl",
            baseUrl: "https://api.test.example.com",
            region: "us",
            currentValue: "https://test.example.com/pricing");

        result.Should().ContainKey("providerName").WhoseValue.Should().Be("OpenAI");
        result.Should().ContainKey("providerUrl").WhoseValue.Should().Be("https://test.example.com");
        result.Should().ContainKey("fieldKind").WhoseValue.Should().Be("subscriptionPricingUrl");
        result.Should().ContainKey("baseUrl").WhoseValue.Should().Be("https://api.test.example.com");
        result.Should().ContainKey("region").WhoseValue.Should().Be("us");
        result.Should().ContainKey("currentValue").WhoseValue.Should().Be("https://test.example.com/pricing");
    }

    [Fact]
    public void BuildParameters_OmitsSearchQueryTemplate_WhenNull()
    {
        var result = DecisionTreeTemplateResolver.BuildParameters(
            "OpenAI",
            "https://test.example.com",
            "subscriptionPricingUrl");

        result.Should().NotContainKey("searchQueryTemplate");
    }

    [Fact]
    public void BuildParameters_IncludesSearchQueryTemplate_WhenProvided()
    {
        var result = DecisionTreeTemplateResolver.BuildParameters(
            "OpenAI",
            "https://test.example.com",
            "subscriptionPricingUrl",
            searchQueryTemplate: "site:test.example.com pricing");

        result.Should().ContainKey("searchQueryTemplate")
            .WhoseValue.Should().Be("site:test.example.com pricing");
    }

    [Fact]
    public void BuildParameters_NullableFieldsDefaultToEmpty()
    {
        var result = DecisionTreeTemplateResolver.BuildParameters(
            "OpenAI",
            "https://test.example.com",
            "field");

        result["baseUrl"].Should().BeEmpty();
        result["region"].Should().BeEmpty();
        result["currentValue"].Should().Be("none");
    }

    [Fact]
    public void BuildParameters_CurrentValueShowsNone_WhenNullOrEmpty()
    {
        var nullResult = DecisionTreeTemplateResolver.BuildParameters(
            "Provider", "https://test.example.com", "field", currentValue: null);
        nullResult["currentValue"].Should().Be("none");

        var emptyResult = DecisionTreeTemplateResolver.BuildParameters(
            "Provider", "https://test.example.com", "field", currentValue: "");
        emptyResult["currentValue"].Should().Be("none");

        var whitespaceResult = DecisionTreeTemplateResolver.BuildParameters(
            "Provider", "https://test.example.com", "field", currentValue: "  ");
        whitespaceResult["currentValue"].Should().Be("none");
    }

    [Fact]
    public void BuildParameters_CurrentValuePreserved_WhenSet()
    {
        var result = DecisionTreeTemplateResolver.BuildParameters(
            "Provider", "https://test.example.com", "field", currentValue: "5");
        result["currentValue"].Should().Be("5");
    }

    [Fact]
    public void BuildParameters_IncludesSiblingUrls_WhenProvided()
    {
        var siblings = new Dictionary<string, string>
        {
            ["apiPricingUrl"] = "https://test.example.com/pricing",
            ["documentationUrl"] = "https://test.example.com/docs"
        };

        var result = DecisionTreeTemplateResolver.BuildParameters(
            "Provider", "https://test.example.com", "baseUrl",
            siblingUrls: siblings);

        result.Should().ContainKey("siblingUrls");
        result["siblingUrls"].Should().Contain("apiPricingUrl=https://test.example.com/pricing");
        result["siblingUrls"].Should().Contain("documentationUrl=https://test.example.com/docs");
    }

    [Fact]
    public void BuildParameters_OmitsSiblingUrls_WhenNullOrEmpty()
    {
        var nullResult = DecisionTreeTemplateResolver.BuildParameters(
            "Provider", "https://test.example.com", "field");
        nullResult.Should().NotContainKey("siblingUrls");

        var emptyResult = DecisionTreeTemplateResolver.BuildParameters(
            "Provider", "https://test.example.com", "field",
            siblingUrls: new Dictionary<string, string>());
        emptyResult.Should().NotContainKey("siblingUrls");
    }
}
