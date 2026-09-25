using System.Text.Json;

using FluentAssertions;

using ScraperTool.Models;
using ScraperTool.Services;

namespace ScraperTool.Tests;

public class ProviderJsonPatchServiceTests : IDisposable
{
    private readonly string _tempDir;

    public ProviderJsonPatchServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"patch-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Apply_NumericFieldExisting_WritesNumberNotString()
    {
        // Arrange — existing file has minModelCount as a JSON number
        const string providerId = "test-provider";
        var filePath = Path.Combine(_tempDir, $"{providerId}.json");
        File.WriteAllText(
            filePath,
            """
            {
              "id": "test-provider",
              "displayName": "Test",
              "minModelCount": 10,
              "baseUrl": "https://api.test.example.com"
            }
            """);

        var service = new ProviderJsonPatchService(_tempDir);
        var suggestions = new List<AiSuggestion>
        {
            new()
            {
                ProviderId = providerId,
                Field = "minModelCount",
                SuggestedValue = "17", // AI always returns string
                DisplayName = "Min Model Count"
            }
        };

        // Act
        var result = service.Apply(suggestions);

        // Assert — minModelCount must remain a JSON number, not become "17"
        result.Errors.Should().BeEmpty();
        result.UpdatedFileCount.Should().Be(1);
        result.AppliedSuggestionCount.Should().Be(1);

        var json = File.ReadAllText(filePath);
        using var doc = JsonDocument.Parse(json);
        var element = doc.RootElement.GetProperty("minModelCount");
        element.ValueKind.Should().Be(JsonValueKind.Number);
        element.GetInt32().Should().Be(17);
    }

    [Fact]
    public void Apply_StringFieldExisting_WritesString()
    {
        // Arrange — existing file has baseUrl as a JSON string
        const string providerId = "test-provider";
        var filePath = Path.Combine(_tempDir, $"{providerId}.json");
        File.WriteAllText(
            filePath,
            """
            {
              "id": "test-provider",
              "displayName": "Test",
              "minModelCount": 10,
              "baseUrl": "https://old.test.example.com"
            }
            """);

        var service = new ProviderJsonPatchService(_tempDir);
        var suggestions = new List<AiSuggestion>
        {
            new()
            {
                ProviderId = providerId,
                Field = "baseUrl",
                SuggestedValue = "https://new.test.example.com",
                DisplayName = "Base URL"
            }
        };

        // Act
        var result = service.Apply(suggestions);

        // Assert — baseUrl must remain a JSON string
        result.Errors.Should().BeEmpty();

        var json = File.ReadAllText(filePath);
        using var doc = JsonDocument.Parse(json);
        var element = doc.RootElement.GetProperty("baseUrl");
        element.ValueKind.Should().Be(JsonValueKind.String);
        element.GetString().Should().Be("https://new.test.example.com");
    }

    [Fact]
    public void Apply_NewFieldNoExistingType_WritesString()
    {
        // Arrange — field does not exist yet, so no type inference possible
        const string providerId = "test-provider";
        var filePath = Path.Combine(_tempDir, $"{providerId}.json");
        File.WriteAllText(
            filePath,
            """
            {
              "id": "test-provider",
              "displayName": "Test"
            }
            """);

        var service = new ProviderJsonPatchService(_tempDir);
        var suggestions = new List<AiSuggestion>
        {
            new()
            {
                ProviderId = providerId,
                Field = "website",
                SuggestedValue = "https://test.example.com",
                DisplayName = "Website"
            }
        };

        // Act
        var result = service.Apply(suggestions);

        // Assert — new field written as string (no existing type to infer from)
        result.Errors.Should().BeEmpty();

        var json = File.ReadAllText(filePath);
        using var doc = JsonDocument.Parse(json);
        var element = doc.RootElement.GetProperty("website");
        element.ValueKind.Should().Be(JsonValueKind.String);
        element.GetString().Should().Be("https://test.example.com");
    }

    [Fact]
    public void Apply_MissingNumericField_WritesNumberNotString()
    {
        // The state that raised the issue: a required number is absent, so there is nothing on
        // disk to copy a type from. Writing the suggestion as text produces a field the model
        // cannot read, which costs the whole provider file rather than that one field — and the
        // validator then reports the same missing number forever.
        const string providerId = "test-provider";
        var filePath = Path.Combine(_tempDir, $"{providerId}.json");
        File.WriteAllText(
            filePath,
            """
            {
              "id": "test-provider",
              "displayName": "Test"
            }
            """);

        var service = new ProviderJsonPatchService(_tempDir);
        var suggestions = new List<AiSuggestion>
        {
            new()
            {
                ProviderId = providerId,
                Field = "minModelCount",
                SuggestedValue = "35",
                DisplayName = "Min Model Count"
            }
        };

        // Act
        var result = service.Apply(suggestions);

        // Assert
        result.Errors.Should().BeEmpty();
        result.AppliedSuggestionCount.Should().Be(1);

        using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
        var element = doc.RootElement.GetProperty("minModelCount");
        element.ValueKind.Should().Be(JsonValueKind.Number);
        element.GetInt32().Should().Be(35);
    }

    [Fact]
    public void Apply_NumericFieldWithNonNumericSuggestion_SkipsFieldAndReportsIt()
    {
        // A number field cannot be answered with prose. Writing it anyway would take the provider
        // out of the catalogue, so the field is skipped and the reason surfaced instead.
        const string providerId = "test-provider";
        var filePath = Path.Combine(_tempDir, $"{providerId}.json");
        File.WriteAllText(
            filePath,
            """
            {
              "id": "test-provider",
              "displayName": "Test"
            }
            """);

        var service = new ProviderJsonPatchService(_tempDir);
        var suggestions = new List<AiSuggestion>
        {
            new()
            {
                ProviderId = providerId,
                Field = "minModelCount",
                SuggestedValue = "about thirty five",
                DisplayName = "Min Model Count"
            },
            new()
            {
                ProviderId = providerId,
                Field = "website",
                SuggestedValue = "https://test.example.com",
                DisplayName = "Website"
            }
        };

        // Act
        var result = service.Apply(suggestions);

        // Assert — the other field still lands, and the file stays readable
        result.Errors.Should().ContainSingle().Which.Should().Contain("takes a number");
        result.AppliedSuggestionCount.Should().Be(1);

        using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
        doc.RootElement.TryGetProperty("minModelCount", out _).Should().BeFalse();
        doc.RootElement.GetProperty("website").GetString().Should().Be("https://test.example.com");
    }

    [Fact]
    public void Apply_MissingBoolField_WritesJsonBoolNotString()
    {
        const string providerId = "test-provider";
        var filePath = Path.Combine(_tempDir, $"{providerId}.json");
        File.WriteAllText(filePath, """{ "id": "test-provider", "displayName": "Test" }""");

        var service = new ProviderJsonPatchService(_tempDir);
        var suggestions = new List<AiSuggestion>
        {
            new()
            {
                ProviderId = providerId,
                Field = "hasFreeTier",
                SuggestedValue = "true",
                DisplayName = "Has Free Tier"
            }
        };

        // Act
        var result = service.Apply(suggestions);

        // Assert
        result.Errors.Should().BeEmpty();

        using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
        doc.RootElement.GetProperty("hasFreeTier").ValueKind.Should().Be(JsonValueKind.True);
    }

    [Fact]
    public void Apply_BoolFieldWithNonBoolSuggestion_SkipsFieldAndReportsIt()
    {
        const string providerId = "test-provider";
        var filePath = Path.Combine(_tempDir, $"{providerId}.json");
        File.WriteAllText(filePath, """{ "id": "test-provider", "displayName": "Test" }""");

        var service = new ProviderJsonPatchService(_tempDir);
        var suggestions = new List<AiSuggestion>
        {
            new()
            {
                ProviderId = providerId,
                Field = "hasFreeTier",
                SuggestedValue = "probably",
                DisplayName = "Has Free Tier"
            }
        };

        // Act
        var result = service.Apply(suggestions);

        // Assert
        result.Errors.Should().ContainSingle().Which.Should().Contain("takes true or false");
        result.AppliedSuggestionCount.Should().Be(0);

        using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
        doc.RootElement.TryGetProperty("hasFreeTier", out _).Should().BeFalse();
    }
}
