using System.Text.Json;
using System.Text.Json.Nodes;

using AIProviderConnect.Constants;
using AIProviderConnect.Models;

using ScraperTool.Models;

namespace ScraperTool.Services;

/// <summary>
/// Shared serializer that merges a runtime <see cref="ProviderDefinition"/> with its
/// optional <see cref="ProviderResearchMetadata"/> sibling into the single flat JSON object
/// that matches the on-disk provider manifest shape.
/// </summary>
public static class ProviderManifestSerializer
{
    /// <summary>
    /// Builds the flat manifest JSON object by serializing the definition and overlaying
    /// every research-metadata property on top.
    /// </summary>
    public static JsonObject Flatten(ProviderDefinition definition, ProviderResearchMetadata? research)
    {
        var manifest = JsonSerializer.SerializeToNode(definition)?.AsObject() ?? new JsonObject();

        // An operation carried in the endpoints block owns its wire path; the legacy
        // flat member is not written alongside it so migrated manifests stay
        // free of duplicate endpoint data.
        if (definition.Endpoints is not null)
        {
            RemoveOwnedFlatEndpoint(definition, manifest, EndpointOperations.Chat, ProviderJsonFields.ChatEndpoint);
            RemoveOwnedFlatEndpoint(definition, manifest, EndpointOperations.Models, ProviderJsonFields.ModelsEndpoint);
            RemoveOwnedFlatEndpoint(definition, manifest, EndpointOperations.Messages, ProviderJsonFields.MessagesEndpoint);
        }

        if (research is not null)
        {
            var researchNode = JsonSerializer.SerializeToNode(research)?.AsObject();
            if (researchNode is not null)
            {
                foreach (var property in researchNode)
                {
                    manifest[property.Key] = property.Value?.DeepClone();
                }
            }
        }

        return manifest;
    }

    private static void RemoveOwnedFlatEndpoint(
        ProviderDefinition definition,
        JsonObject manifest,
        string operation,
        string flatField)
    {
        if (EndpointOperations.Find(definition.Endpoints, operation) is not null
            && manifest.ContainsKey(flatField))
        {
            manifest.Remove(flatField);
        }
    }
}
