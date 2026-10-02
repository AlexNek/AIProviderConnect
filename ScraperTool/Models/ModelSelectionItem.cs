using AIProviderConnect.Models;

namespace ScraperTool.Models;

public sealed class ModelSelectionItem
{
    public EModelCapability Capabilities { get; set; }

    public string CompletionPrice { get; set; } = string.Empty;

    public string ContextWindow { get; set; } = string.Empty;

    public string? Description { get; set; }

    public required string DisplayName { get; set; }

    public required string Id { get; set; }

    public bool IsFree { get; set; }

    public string Modalities { get; set; } = string.Empty;

    public string? OwnedBy { get; set; }

    public string PromptPrice { get; set; } = string.Empty;

    public static ModelSelectionItem FromAIModel(AIModel m)
    {
        var owner = m.OwnedBy;
        if (string.IsNullOrWhiteSpace(owner) && m.Id.Contains('/'))
            owner = m.Id.Split('/')[0];

        return new ModelSelectionItem
                   {
                       Id = m.Id,
                       DisplayName = m.DisplayName,
                       Description = m.Description,
                       OwnedBy = owner,
                       Modalities = ParseModalities(m.Modality),
                       ContextWindow =
                           m.ContextWindow is > 0 ? $"{m.ContextWindow:N0}" : string.Empty,
                       PromptPrice =
                           m.PromptPrice is not null ? $"${m.PromptPrice:0.######}" : string.Empty,
                       CompletionPrice =
                           m.CompletionPrice is not null
                               ? $"${m.CompletionPrice:0.######}"
                               : string.Empty,
                       IsFree = m.PromptPrice is 0 && m.CompletionPrice is 0,
                       Capabilities = m.Capabilities,
                   };
    }

    // Parses "text+image+file->text+image" style modality strings into icon labels
    private static string ParseModalities(string? modality)
    {
        if (string.IsNullOrWhiteSpace(modality))
            return string.Empty;

        // Collect all tokens from both sides of "->"
        var all = modality.Replace("->", "+").Split('+', StringSplitOptions.RemoveEmptyEntries);

        var icons = new List<string>();
        bool hasText = false,
             hasImage = false,
             hasAudio = false,
             hasVideo = false,
             hasFile = false,
             hasCode = false;

        foreach (var token in all)
        {
            switch (token.Trim().ToLowerInvariant())
            {
                case "text": hasText = true; break;
                case "image": hasImage = true; break;
                case "audio":
                case "sound": hasAudio = true; break;
                case "video": hasVideo = true; break;
                case "file":
                case "document": hasFile = true; break;
                case "code": hasCode = true; break;
            }
        }

        if (hasText) icons.Add("📝");
        if (hasImage) icons.Add("🖼");
        if (hasAudio) icons.Add("🔊");
        if (hasVideo) icons.Add("🎬");
        if (hasFile) icons.Add("📄");
        if (hasCode) icons.Add("💻");

        return icons.Count > 0 ? string.Join(" ", icons) : modality;
    }
}
