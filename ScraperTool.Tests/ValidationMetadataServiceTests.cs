using System.Text.Json;

using FluentAssertions;

namespace ScraperTool.Tests;

public class ValidationMetadataServiceTests
{
    [Fact]
    public void Delete_RemovesSidecarFile()
    {
        var providerPath = ProviderPath("provider3.json");
        var service = new Services.ValidationMetadataService();
        var sidecarPath = providerPath.Replace(".json", ".validation.json");

        try
        {
            File.WriteAllText(sidecarPath, "{}");
            File.Exists(sidecarPath).Should().BeTrue();

            service.Delete(providerPath);
            File.Exists(sidecarPath).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(sidecarPath)) File.Delete(sidecarPath);
            if (File.Exists(providerPath)) File.Delete(providerPath);
        }
    }

    [Fact]
    public void Read_ReturnsNull_WhenSidecarMissing()
    {
        var service = new Services.ValidationMetadataService();
        var result = service.Read("nonexistent.json");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveFromValidationAsync_WritesSidecarFile()
    {
        var providerPath = ProviderPath("provider1.json");
        WriteProvider(providerPath, "provider1");
        var service = new Services.ValidationMetadataService();

        try
        {
            var issues = new[]
                             {
                                 new Services.ValidationIssue(
                                     "provider1.json",
                                     "UrlNotFound",
                                     "https://example.com not found")
                             };

            await service.SaveFromValidationAsync(providerPath, issues);

            var sidecarPath = providerPath.Replace(".json", ".validation.json");
            File.Exists(sidecarPath).Should().BeTrue();

            var json = File.ReadAllText(sidecarPath);
            var metadata = JsonSerializer.Deserialize<Models.ValidationMetadata>(json);
            metadata.Should().NotBeNull();
            metadata!.ProviderId.Should().Be("provider1");
            metadata.Level.Should().Be(Models.ValidationLevel.ValidationError);
            metadata.ValidationErrors.Should().HaveCount(1);
            metadata.ValidatedBy.Should().Be("auto");
        }
        finally
        {
            var sidecar = providerPath.Replace(".json", ".validation.json");
            if (File.Exists(sidecar)) File.Delete(sidecar);
            if (File.Exists(providerPath)) File.Delete(providerPath);
        }
    }

    [Fact]
    public async Task UpdateAsync_OverwritesManualValidation()
    {
        var providerPath = ProviderPath("provider2.json");
        WriteProvider(providerPath, "provider2");
        var service = new Services.ValidationMetadataService();

        try
        {
            await service.SaveFromValidationAsync(providerPath, []);
            await service.UpdateAsync(
                providerPath,
                Models.ValidationLevel.ManuallyValidated,
                "user@example.com",
                "Looks good");

            var sidecarPath = providerPath.Replace(".json", ".validation.json");
            var json = File.ReadAllText(sidecarPath);
            var metadata = JsonSerializer.Deserialize<Models.ValidationMetadata>(json);
            metadata.Should().NotBeNull();
            metadata!.Level.Should().Be(Models.ValidationLevel.ManuallyValidated);
            metadata.ValidatedBy.Should().Be("user@example.com");
            metadata.Notes.Should().Be("Looks good");
        }
        finally
        {
            var sidecar = providerPath.Replace(".json", ".validation.json");
            if (File.Exists(sidecar)) File.Delete(sidecar);
            if (File.Exists(providerPath)) File.Delete(providerPath);
        }
    }

    private static string ProviderPath(string fileName) =>
        Path.Combine(Path.GetTempPath(), $"validation-metadata-tests-{fileName}");

    private static void WriteProvider(string path, string id)
    {
        var json = JsonSerializer.Serialize(new { id });
        File.WriteAllText(path, json);
    }
}
