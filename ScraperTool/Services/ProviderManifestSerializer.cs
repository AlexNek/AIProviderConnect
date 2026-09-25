using System.Text.Json;
using System.Text.Json.Nodes;

using AIProviderConnect.Models;

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
}
