using AIProviderConnect.Models;
using AIProviderConnect.Services;

using FluentAssertions;

namespace AIProviderConnect.Tests.Services;

public class ProviderCatalogReloadTests : IDisposable
{
    private readonly string _tempDir;

    public ProviderCatalogReloadTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"provider-catalog-reload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact]
    public void ReloadFromDisk_IdMismatchWithStem_SurfacedViaLoadErrors()
    {
        // Arrange — write a file whose JSON "id" does not match the file stem.
        var catalog = new ProviderCatalog();
        var errorsBefore = catalog.LoadErrors.Count;

        File.WriteAllText(Path.Combine(_tempDir, "mismatched-provider.json"),
            BuildProviderJson("wrong-id", "Wrong"));

        // Act
        var reloaded = catalog.ReloadFromDisk(_tempDir, ["mismatched-provider"]);

        // Assert
        reloaded.Should().Be(0, "the id mismatch should prevent a successful reload");
        catalog.LoadErrors.Count.Should().BeGreaterThan(errorsBefore,
            "a load error should be recorded for the id mismatch");
        catalog.LoadErrors.Last().Should().Contain("does not match");
    }

    [Fact]
    public void ReloadFromDisk_ValidFile_IncrementsLoadErrorsByZero()
    {
        // Arrange — write a valid file whose JSON "id" matches the file stem.
        var catalog = new ProviderCatalog();
        var errorsBefore = catalog.LoadErrors.Count;

        File.WriteAllText(Path.Combine(_tempDir, "good-provider.json"),
            BuildProviderJson("good-provider", "Good Provider"));

        // Act
        var reloaded = catalog.ReloadFromDisk(_tempDir, ["good-provider"]);

        // Assert
        reloaded.Should().Be(1);
        catalog.LoadErrors.Count.Should().Be(errorsBefore,
            "a successful reload should not add load errors");
        catalog.Get("good-provider").Should().NotBeNull();
        catalog.Get("good-provider")!.DisplayName.Should().Be("Good Provider");
    }

    [Fact]
    public void ReloadFromDisk_NonexistentFile_ReturnsZeroAndNoError()
    {
        // Arrange
        var catalog = new ProviderCatalog();
        var errorsBefore = catalog.LoadErrors.Count;

        // Act
        var reloaded = catalog.ReloadFromDisk(_tempDir, ["nonexistent-provider"]);

        // Assert
        reloaded.Should().Be(0);
        catalog.LoadErrors.Count.Should().Be(errorsBefore,
            "a missing file is silently skipped, not an error");
    }

    private static string BuildProviderJson(string id, string displayName) => $$"""
        {
            "id": "{{id}}",
            "displayName": "{{displayName}}",
            "baseUrl": "https://test.example.com",
            "protocol": "OpenAICompatible",
            "website": "https://test.example.com",
            "loginUrl": "https://test.example.com/login",
            "apiPricingUrl": "https://test.example.com/pricing",
            "documentationUrl": "https://test.example.com/docs",
            "minModelCount": 1
        }
        """;
}
