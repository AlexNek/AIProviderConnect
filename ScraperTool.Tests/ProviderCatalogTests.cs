using System.Text.Json;

using FluentAssertions;

namespace ScraperTool.Tests;

public class ProviderCatalogTests
{
    [Fact]
    public void ReloadFromDisk_AddsNewProvider_IfIdNotPresent()
    {
        var catalog = new AIProviderConnect.Services.ProviderCatalog();
        var tempDir = Path.Combine(Path.GetTempPath(), "provider-catalog-tests-new");
        Directory.CreateDirectory(tempDir);

        try
        {
            var newProvider = @"{
  ""id"": ""test-provider"",
  ""displayName"": ""Test Provider"",
  ""protocol"": ""OpenAICompatible"",
  ""website"": ""https://example.com"",
  ""loginUrl"": ""https://example.com/login"",
  ""apiPricingUrl"": ""https://example.com/api-pricing"",
  ""subscriptionPricingUrl"": ""https://example.com/plans"",
  ""documentationUrl"": ""https://example.com/docs"",
  ""baseUrl"": ""https://api.example.com/v1"",
  ""chatEndpoint"": ""chat/completions"",
  ""modelsEndpoint"": ""models"",
  ""category"": ""Test"",
  ""modelDescription"": ""Test models"",
  ""payAsYouGo"": true,
  ""hasModelDiscoveryApi"": false,
  ""minModelCount"": 1
}";
            File.WriteAllText(Path.Combine(tempDir, "test-provider.json"), newProvider);

            var reloaded = catalog.ReloadFromDisk(tempDir, new[] { "test-provider" });
            reloaded.Should().Be(1);

            var added = catalog.Get("test-provider");
            added.Should().NotBeNull();
            added!.DisplayName.Should().Be("Test Provider");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ReloadFromDisk_LogsWarning_WhenDeserializesToNull()
    {
        var catalog = new AIProviderConnect.Services.ProviderCatalog();
        var tempDir = Path.Combine(Path.GetTempPath(), "provider-catalog-tests-null");
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "bad.json"), "null");

            var reloaded = catalog.ReloadFromDisk(tempDir, new[] { "bad" });
            reloaded.Should().Be(0);
            catalog.LoadErrors.Count.Should().BeGreaterThan(0);
            catalog.LoadErrors.Should().Contain(e => e.Contains("deserialized to null"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ReloadFromDisk_ReplacesExistingProvider()
    {
        var catalog = new AIProviderConnect.Services.ProviderCatalog();
        var tempDir = Path.Combine(Path.GetTempPath(), "provider-catalog-tests");
        Directory.CreateDirectory(tempDir);

        try
        {
            var original = catalog.Get("openai");
            original.Should().NotBeNull();

            var modifiedJson = JsonSerializer.Serialize(
                original,
                new JsonSerializerOptions { WriteIndented = true });
            modifiedJson = modifiedJson.Replace(
                "\"displayName\": \"OpenAI\"",
                "\"displayName\": \"OpenAI Modified\"");
            File.WriteAllText(Path.Combine(tempDir, "openai.json"), modifiedJson);

            var reloaded = catalog.ReloadFromDisk(tempDir, new[] { "openai" });
            reloaded.Should().Be(1);

            var updated = catalog.Get("openai");
            updated.Should().NotBeNull();
            updated!.DisplayName.Should().Be("OpenAI Modified");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
