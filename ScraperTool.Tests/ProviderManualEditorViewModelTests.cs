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

        // Assert — decisions from the endpoints block, chat/models/messages seeded
        // from legacy flat fields, and embeddings auto-seeded with its library default.
        vm.EndpointConfiguration.Should().HaveCount(5);
        var decisions = vm.EndpointConfiguration.First(r => r.Operation == "decisions");
        decisions.Path.Should().Be("alpha/decisions");
        decisions.BaseUrl.Should().Be("https://api.test.example.com/api/");
        decisions.Protocol.Should().Be("decision");
        var messages = vm.EndpointConfiguration.First(r => r.Operation == "messages");
        messages.Path.Should().Be("v1/messages");
        vm.EndpointConfiguration.First(r => r.Operation == "chat").Path.Should().Be("chat/completions");
        vm.EndpointConfiguration.First(r => r.Operation == "models").Path.Should().Be("models");
        var embeddings = vm.EndpointConfiguration.First(r => r.Operation == "embeddings");
        embeddings.Path.Should().Be(EndpointDefaults.Embeddings);
        embeddings.IsAutoSeededDefault.Should().BeTrue();
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
        var row = vm.EndpointConfiguration.First(r => r.Operation == "decisions");
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
    public void Save_RowWithAdditionalQueryParameter_RoundTrips()
    {
        // Arrange
        using var harness = CreateViewModel(ProviderJson);
        var vm = harness.ViewModel;

        // Act
        var row = vm.EndpointConfiguration.First(r => r.Operation == "models");
        row.AdditionalQueryParameter = "output_modalities=all";
        vm.SaveCommand.Execute(null);

        // Assert
        var def = JsonSerializer.Deserialize<ProviderDefinition>(
            File.ReadAllText(harness.ProviderFilePath), ReadOptions)!;
        var entry = def.Endpoints!["models"]!;
        entry.AdditionalQueryParameter.Should().Be("output_modalities=all");
    }

    [Fact]
    public void Load_DefinitionWithAdditionalQueryParameter_PopulatesRow()
    {
        // Arrange
        var json = ProviderJson.Replace(
            "\"path\": \"alpha/decisions\",",
            "\"path\": \"alpha/decisions\",\n\"additionalQueryParameter\": \"output_modalities=all\",");
        using var harness = CreateViewModel(json);
        var vm = harness.ViewModel;

        // Act & Assert
        var row = vm.EndpointConfiguration.First(r => r.Operation == "decisions");
        row.AdditionalQueryParameter.Should().Be("output_modalities=all");
    }

    [Fact]
    public void Save_RowWithEmptyOperationOrAllFieldsEmpty_IsDropped()
    {
        // Arrange
        using var harness = CreateViewModel(ProviderJson);
        var vm = harness.ViewModel;

        // Act — an empty row and a row with an operation but nothing else
        vm.AddEndpointConfigEntryCommand.Execute(null);
        vm.EndpointConfiguration[5].Path = "orphan/path";
        vm.AddEndpointConfigEntryCommand.Execute(null);
        vm.SaveCommand.Execute(null);

        // Assert — chat + decisions + messages + models (seeded from flat fields and
        // the endpoints block) + embeddings (auto-seeded default); rows without an
        // operation or with all fields empty are dropped, and auto-seeded defaults
        // with no override are not written.
        var def = JsonSerializer.Deserialize<ProviderDefinition>(
            File.ReadAllText(harness.ProviderFilePath), ReadOptions)!;
        def.Endpoints.Should().NotBeNull();
        def.Endpoints.Should().HaveCount(4, "auto-seeded defaults with no override and rows without an operation are dropped");
        def.Endpoints.Should().ContainKey("decisions");
        def.Endpoints.Should().ContainKey("messages");
        def.Endpoints.Should().ContainKey("chat");
        def.Endpoints.Should().ContainKey("models");
    }

    [Fact]
    public void Save_DuplicateOperations_KeepLastRow()
    {
        // Arrange
        using var harness = CreateViewModel(ProviderJson);
        var vm = harness.ViewModel;

        // Act — a second "decisions" row must win
        vm.AddEndpointConfigEntryCommand.Execute(null);
        var duplicate = vm.EndpointConfiguration[5];
        duplicate.Operation = "decisions";
        duplicate.Path = "gamma/decisions";
        vm.SaveCommand.Execute(null);

        // Assert
        var def = JsonSerializer.Deserialize<ProviderDefinition>(
            File.ReadAllText(harness.ProviderFilePath), ReadOptions)!;
        def.Endpoints!.Should().HaveCount(4);
        def.Endpoints["decisions"]!.Path.Should().Be("gamma/decisions");
    }

    [Fact]
    public void Save_MessagesEndpoint_Survives()
    {
        // Arrange
        using var harness = CreateViewModel(ProviderJson);
        var vm = harness.ViewModel;

        // Act — editing the flat property updates the seeded EndpointConfiguration row
        vm.MessagesEndpoint = "anthropic/messages";
        vm.SaveCommand.Execute(null);

        // Assert — the messages path moved into the endpoints block; the serializer
        // drops the flat member because the block now owns the operation.
        var json = File.ReadAllText(harness.ProviderFilePath);
        var root = JsonNode.Parse(json)!;
        root["messagesEndpoint"].Should().BeNull("flat member is suppressed when endpoints block owns the operation");
        root["endpoints"]!["messages"]!["path"]!.GetValue<string>().Should().Be("anthropic/messages");
    }

    [Fact]
    public void OptionLists_MatchLibraryVocabulary()
    {
        // Arrange
        var expectedOperations = new[] { string.Empty,
            EndpointOperations.Chat, EndpointOperations.Models,
            EndpointOperations.Messages, EndpointOperations.Embeddings,
            EndpointOperations.Decisions
        };
        var expectedProtocols = new[] { EndpointConfigEntry.InheritProtocolDisplay }
            .Concat(Enum.GetValues<EProviderProtocol>().Select(ProviderProtocolMapper.ToJson))
            .ToArray();

        // Act
        var operations = EndpointConfigEntry.KnownOperationsStatic;
        var protocols = EndpointConfigEntry.KnownProtocolsStatic;

        // Assert
        operations.Should().Equal(expectedOperations);
        protocols.Should().Equal(expectedProtocols);
        protocols.First().Should().Be(EndpointConfigEntry.InheritProtocolDisplay, "the first protocol choice is the visible inherit choice");
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
