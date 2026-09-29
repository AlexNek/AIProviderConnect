using System.Text.Json;
using System.Text.Json.Nodes;

using AIProviderConnect.Constants;
using AIProviderConnect.Models;

using FluentAssertions;

using Moq;

using ScraperTool.Models;
using ScraperTool.Services;
using ScraperTool.ViewModels;

using ProviderCatalog = AIProviderConnect.Services.ProviderCatalog;

namespace ScraperTool.Tests;

public class ProviderManualEditorViewModelTests
{
    private const string ProviderJson = """
        {
          "id": "test-provider",
          "displayName": "Test Provider",
          "protocol": "openaicompatible",
          "baseUrl": "https://api.test.example.com/v1/",
          "chatEndpoint": "chat/completions",
          "modelsEndpoint": "models",
          "messagesEndpoint": "v1/messages",
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

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void LoadProvider_WithEndpointsBlock_ProjectsSortedRows()
    {
        // Arrange
        using var harness = CreateViewModel(ProviderJson);

        // Act
        var vm = harness.ViewModel;

        // Assert
        vm.EndpointConfiguration.Should().HaveCount(1);
        var row = vm.EndpointConfiguration[0];
        row.Operation.Should().Be("decisions");
        row.Path.Should().Be("alpha/decisions");
        row.BaseUrl.Should().Be("https://api.test.example.com/api/");
        row.Protocol.Should().Be("decision");
        vm.MessagesEndpoint.Should().Be("v1/messages");
    }

    [Fact]
    public void Save_AfterUnrelatedEdit_PreservesEndpointsBlock()
    {
        // Arrange
        using var harness = CreateViewModel(ProviderJson);
        var vm = harness.ViewModel;

        // Act — an edit unrelated to the endpoints block
        vm.DisplayName = "Renamed Provider";
        vm.SaveCommand.Execute(null);

        // Assert
        var json = File.ReadAllText(harness.ProviderFilePath);
        var root = JsonNode.Parse(json)!;
        var decisions = root["endpoints"]!["decisions"]!;
        decisions["path"]!.GetValue<string>().Should().Be("alpha/decisions");
        decisions["baseUrl"]!.GetValue<string>().Should().Be("https://api.test.example.com/api/");
        decisions["protocol"]!.GetValue<string>().Should().Be("decision");
    }

    [Fact]
    public void Save_EditedRow_RoundTrips()
    {
        // Arrange
        using var harness = CreateViewModel(ProviderJson);
        var vm = harness.ViewModel;

        // Act
        var row = vm.EndpointConfiguration[0];
        row.Path = "beta/decisions";
        row.BaseUrl = "https://override.test.example.com/api/";
        vm.SaveCommand.Execute(null);

        // Assert
        var def = JsonSerializer.Deserialize<ProviderDefinition>(
            File.ReadAllText(harness.ProviderFilePath), ReadOptions)!;
        var entry = def.Endpoints!["decisions"]!;
        entry.Path.Should().Be("beta/decisions");
        entry.BaseUrl.Should().Be("https://override.test.example.com/api/");
    }

    [Fact]
    public void Save_RowWithEmptyOperationOrAllFieldsEmpty_IsDropped()
    {
        // Arrange
        using var harness = CreateViewModel(ProviderJson);
        var vm = harness.ViewModel;

        // Act — an empty row and a row with an operation but nothing else
        vm.AddEndpointConfigEntryCommand.Execute(null);
        vm.EndpointConfiguration[1].Path = "orphan/path";
        vm.AddEndpointConfigEntryCommand.Execute(null);
        vm.SaveCommand.Execute(null);

        // Assert
        var def = JsonSerializer.Deserialize<ProviderDefinition>(
            File.ReadAllText(harness.ProviderFilePath), ReadOptions)!;
        def.Endpoints.Should().NotBeNull();
        def.Endpoints.Should().HaveCount(1, "rows without an operation or with all fields empty are dropped");
        def.Endpoints.Should().ContainKey("decisions");
    }

    [Fact]
    public void Save_DuplicateOperations_KeepLastRow()
    {
        // Arrange
        using var harness = CreateViewModel(ProviderJson);
        var vm = harness.ViewModel;

        // Act — a second "decisions" row must win
        vm.AddEndpointConfigEntryCommand.Execute(null);
        var duplicate = vm.EndpointConfiguration[1];
        duplicate.Operation = "decisions";
        duplicate.Path = "gamma/decisions";
        vm.SaveCommand.Execute(null);

        // Assert
        var def = JsonSerializer.Deserialize<ProviderDefinition>(
            File.ReadAllText(harness.ProviderFilePath), ReadOptions)!;
        def.Endpoints!.Should().HaveCount(1);
        def.Endpoints["decisions"]!.Path.Should().Be("gamma/decisions");
    }

    [Fact]
    public void Save_MessagesEndpoint_Survives()
    {
        // Arrange
        using var harness = CreateViewModel(ProviderJson);
        var vm = harness.ViewModel;

        // Act
        vm.MessagesEndpoint = "anthropic/messages";
        vm.SaveCommand.Execute(null);

        // Assert
        var json = File.ReadAllText(harness.ProviderFilePath);
        JsonNode.Parse(json)!["messagesEndpoint"]!.GetValue<string>().Should().Be("anthropic/messages");
    }

    [Fact]
    public void OptionLists_MatchLibraryVocabulary()
    {
        // Arrange
        var expectedOperations = new[]
        {
            EndpointOperations.Chat, EndpointOperations.Models,
            EndpointOperations.Messages, EndpointOperations.Embeddings,
            EndpointOperations.Decisions
        };
        var expectedProtocols = new[] { string.Empty }
            .Concat(Enum.GetValues<EProviderProtocol>().Select(ProviderProtocolMapper.ToJson))
            .ToArray();

        // Act
        var operations = EndpointConfigEntry.KnownOperations;
        var protocols = EndpointConfigEntry.KnownProtocols;

        // Assert
        operations.Should().Equal(expectedOperations);
        protocols.Should().Equal(expectedProtocols);
        protocols.First().Should().BeEmpty("the first protocol choice is the inherit choice");
    }

    private static EditorHarness CreateViewModel(string json)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"editor-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var providerFilePath = Path.Combine(dir, "test-provider.json");
        File.WriteAllText(providerFilePath, json);

        var def = JsonSerializer.Deserialize<ProviderDefinition>(json, ReadOptions)!;
        var catalog = new ProviderCatalog([def]);
        var vm = new ProviderManualEditorViewModel(
            catalog, dir, navigateBack: () => { }, new Mock<IClipboardService>().Object);
        vm.SelectedProvider =
            vm.FilteredProviders.First(p => p.ProviderId == "test-provider");

        return new EditorHarness(vm, dir, providerFilePath);
    }

    private sealed class EditorHarness(
        ProviderManualEditorViewModel viewModel,
        string directory,
        string providerFilePath) : IDisposable
    {
        public ProviderManualEditorViewModel ViewModel { get; } = viewModel;

        public string Directory { get; } = directory;

        public string ProviderFilePath { get; } = providerFilePath;

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup of the throwaway manifest directory.
            }
        }
    }
}
