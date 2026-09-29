using AIProviderConnect.Models;

using FluentAssertions;

using ScraperTool.Models;
using ScraperTool.Services;
using ScraperTool.Services.Validation;

namespace ScraperTool.Tests;

/// <summary>
/// Proves the schema validator needs no <c>KnownProtocols</c> change for the
/// multi-protocol feature: a migrated definition keeps its root protocol (the
/// nested <c>endpoints.protocol</c> token is not checked), so the migrated
/// shape validates with zero errors while an unknown root protocol still fails.
/// </summary>
public class ProviderSchemaValidatorTests
{
    private const string MigratedShapeJson = """
        {
          "id": "test-provider",
          "displayName": "Test Provider",
          "protocol": "OpenAICompatible",
          "baseUrl": "https://api.test.example.com/v1/",
          "website": "https://test.example.com",
          "loginUrl": "https://test.example.com/login",
          "apiPricingUrl": "https://test.example.com/pricing",
          "documentationUrl": "https://test.example.com/docs",
          "minModelCount": 1,
          "endpoints": {
            "decisions": {
              "path": "alpha/decisions",
              "baseUrl": "https://api.test.example.com/api/",
              "protocol": "decision"
            }
          }
        }
        """;

    [Fact]
    public void ValidateSchema_MigratedEndpointsShape_ReportsZeroErrors()
    {
        // Arrange
        var validator = new ProviderSchemaValidator();

        // Act
        var issues = validator.ValidateSchema(MigratedShapeJson, "test-provider.json");

        // Assert
        issues.Should().BeEmpty();
    }

    [Fact]
    public void ValidateSchema_UnknownRootProtocol_IsReported()
    {
        // Arrange
        var validator = new ProviderSchemaValidator();
        var json = MigratedShapeJson.Replace(
            "\"protocol\": \"OpenAICompatible\"",
            "\"protocol\": \"notARealProtocol\"",
            StringComparison.Ordinal);

        // Act
        var issues = validator.ValidateSchema(json, "test-provider.json");

        // Assert
        issues.Should().Contain(i => i.Code == ValidationIssueCodes.InvalidProtocol);
    }
}
