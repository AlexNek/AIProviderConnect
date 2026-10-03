using AIProviderConnect.Models;

namespace ScraperTool.Models;

public sealed class ModelSelectionItem
{
    public EModelCapability? Capabilities { get; set; }

    // Effective capabilities: provider-reported when available, derived from modality otherwise.
    // Used by the capability filter and column display.
    public EModelCapability EffectiveCapabilities =>
        Capabilities ?? DeriveCapabilitiesFromModality(ModalityWords, Id, OwnedBy);

    // Readable form of Capabilities; "not reported" because an unset flag means the provider
    // never told us anything, not that the model lacks the capability.
    public string CapabilitiesText { get; set; } = "not reported";

    public string CompletionPrice { get; set; } = string.Empty;

    public string ContextWindow { get; set; } = string.Empty;

    public string? Description { get; set; }

    public required string DisplayName { get; set; }

    public required string Id { get; set; }

    public bool IsFree { get; set; }

    // Icons for tokens on the left side of "->" (what the model accepts).
    public string ModalitiesIn { get; set; } = string.Empty;

    // Icons for tokens on the right side of "->" (what the model produces).
    public string ModalitiesOut { get; set; } = string.Empty;

    // Raw provider modality string (e.g. "text+image->text") — kept because the
    // Modalities columns show icons, which cannot be typed into the text filter.
    public string ModalityWords { get; set; } = string.Empty;

    // Id without its owner prefix ("gpt-4o" from "openai/gpt-4o") — the Name column displays and sorts
    // by this, so names line up alphabetically across owners instead of clustering under each prefix.
    public string Name { get; set; } = string.Empty;

    public string? OwnedBy { get; set; }

    public string PromptPrice { get; set; } = string.Empty;

    public static ModelSelectionItem FromAIModel(AIModel m)
    {
        var owner = m.OwnedBy;
        if (string.IsNullOrWhiteSpace(owner) && m.Id.Contains('/'))
            owner = m.Id.Split('/')[0];

        ParseModalities(m.Modality, out var modalitiesIn, out var modalitiesOut);

        return new ModelSelectionItem
                   {
                       Id = m.Id,
                       DisplayName = m.DisplayName,
                       Description = m.Description,
                       OwnedBy = owner,
                       ModalitiesIn = modalitiesIn,
                       ModalitiesOut = modalitiesOut,
                       ModalityWords = m.Modality ?? string.Empty,
                       Name = m.Id.Contains('/') ? m.Id[(m.Id.LastIndexOf('/') + 1)..] : m.Id,
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
                       CapabilitiesText = FormatCapabilitiesText(m.Capabilities, m.Modality, m.Id, owner),
                   };
    }

    // Parses "text+image->text+image" style modality strings into separate In/Out icon strings.
    private static void ParseModalities(string? modality, out string iconsIn, out string iconsOut)
    {
        if (string.IsNullOrWhiteSpace(modality))
        {
            iconsIn = string.Empty;
            iconsOut = string.Empty;
            return;
        }

        // Split on "->" to separate input and output tokens.
        var parts = modality.Split(new[] { "->" }, 2, StringSplitOptions.None);
        var inputPart = parts[0];
        var outputPart = parts.Length > 1 ? parts[1] : parts[0];

        iconsIn = TokensToIcons(inputPart, modality);
        iconsOut = TokensToIcons(outputPart, modality);
    }

    private static string TokensToIcons(string tokenString, string fallback)
    {
        ParseModalityFlags(tokenString, out var hasImage, out var hasAudio, out var hasVideo,
            out var hasEmbeddings, out var hasDecisions, out var hasRerank);

        // Check text separately (ParseModalityFlags skips it — capabilities have no text flag).
        var hasText = tokenString.Split('+', StringSplitOptions.RemoveEmptyEntries)
            .Any(t => t.Trim().Equals("text", StringComparison.OrdinalIgnoreCase));

        var icons = new List<string>();
        if (hasText) icons.Add("\uD83D\uDCDD");
        if (hasImage) icons.Add("\uD83D\uDDBC");
        if (hasAudio) icons.Add("\uD83D\uDD0A");
        if (hasVideo) icons.Add("\uD83C\uDFAC");
        if (hasEmbeddings) icons.Add("\uD83D\uDD22");
        if (hasDecisions) icons.Add("\uD83C\uDFAF");
        if (hasRerank) icons.Add("\uD83D\uDCCA");

        return icons.Count > 0 ? string.Join(" ", icons) : fallback;
    }

    // Derives capabilities from the modality string when the provider did not report them.
    // Direction-aware: image in → ImageRecognition, image out → ImageGeneration, etc.
    // Text input or output implies TextGeneration (baseline LLM capability).
    // Models whose owner/name indicate they are not structured-decision models (e.g. behavior
    // scoring) are excluded from Decision derivation even if their modality token says "decisions".
    private static EModelCapability DeriveCapabilitiesFromModality(string? modality, string? modelId, string? owner)
    {
        if (string.IsNullOrWhiteSpace(modality))
            return EModelCapability.None;

        var parts = modality.Split(new[] { "->" }, 2, StringSplitOptions.None);
        var inputPart = parts[0];
        var outputPart = parts.Length > 1 ? parts[1] : parts[0];

        ParseModalityFlags(inputPart, out var hasImageIn, out var hasAudioIn, out var hasVideoIn,
            out _, out _, out _);
        ParseModalityFlags(outputPart, out var hasImageOut, out var hasAudioOut, out var hasVideoOut,
            out var hasEmbeddingsOut, out var hasDecisionsOut, out var hasRerankOut);

        // Check for text on the output side — TextGeneration means the model produces text,
        // not just accepts it. A reranker (text->rerank) accepts text but does not generate it.
        var hasText = HasToken(outputPart, "text");

        // Suppress Decision derivation for models known to be non-decision (e.g. behavior scoring)
        // despite carrying the "decisions" modality token from the provider.
        if (hasDecisionsOut && IsExcludedFromDecisionDerivation(modelId, owner))
            hasDecisionsOut = false;

        var caps = EModelCapability.None;
        if (hasText) caps |= EModelCapability.TextGeneration;
        if (hasImageIn) caps |= EModelCapability.ImageRecognition;
        if (hasImageOut) caps |= EModelCapability.ImageGeneration;
        if (hasAudioIn) caps |= EModelCapability.AudioRecognition;
        if (hasAudioOut) caps |= EModelCapability.TextToSpeech;
        if (hasVideoIn) caps |= EModelCapability.VideoRecognition;
        if (hasVideoOut) caps |= EModelCapability.VideoGeneration;
        if (hasEmbeddingsOut) caps |= EModelCapability.Embedding;
        if (hasDecisionsOut) caps |= EModelCapability.Decision;
        if (hasRerankOut) caps |= EModelCapability.Reranker;
        return caps;
    }

    private static bool HasToken(string tokenString, string token)
    {
        return tokenString.Split('+', StringSplitOptions.RemoveEmptyEntries)
            .Any(t => t.Trim().Equals(token, StringComparison.OrdinalIgnoreCase));
    }

    // Extracts modality presence flags from a token string (one side of the modality arrow).
    private static void ParseModalityFlags(string tokenString,
        out bool hasImage, out bool hasAudio, out bool hasVideo,
        out bool hasEmbeddings, out bool hasDecisions, out bool hasRerank)
    {
        var tokens = tokenString.Split('+', StringSplitOptions.RemoveEmptyEntries);
        hasImage = hasAudio = hasVideo = false;
        hasEmbeddings = hasDecisions = hasRerank = false;

        foreach (var token in tokens)
        {
            switch (token.Trim().ToLowerInvariant())
            {
                case "image": hasImage = true; break;
                case "audio":
                case "sound":
                case "speech":
                case "transcription": hasAudio = true; break;
                case "video": hasVideo = true; break;
                case "embedding":
                case "embeddings": hasEmbeddings = true; break;
                case "decision":
                case "decisions": hasDecisions = true; break;
                case "rerank": hasRerank = true; break;
            }
        }
    }

    // Formats the Capabilities column text. Provider-reported capabilities show as-is;
    // derived capabilities carry a "(derived)" suffix so users know the data comes from
    // modality tokens (which providers can mislabel) rather than an explicit report.
    // When derivation yields nothing but modality is known (e.g. excluded models), show
    // the raw modality so the user sees what the provider reported instead of "not reported".
    private static string FormatCapabilitiesText(EModelCapability? reported, string? modality, string? modelId, string? owner)
    {
        if (reported is not null)
            return reported.Value.ToString();

        var derived = DeriveCapabilitiesFromModality(modality, modelId, owner);
        if (derived != EModelCapability.None)
            return $"{derived} (derived)";

        // Derivation produced nothing — show raw modality if available, otherwise truly unreported.
        return string.IsNullOrWhiteSpace(modality) ? "not reported" : modality;
    }

    // Models whose owner or name indicate they are not structured-decision models, despite
    // the provider labeling them with the "decisions" output modality token.
    private static bool IsExcludedFromDecisionDerivation(string? modelId, string? owner)
    {
        var id = (modelId ?? string.Empty).ToLowerInvariant();
        var own = (owner ?? string.Empty).ToLowerInvariant();

        // respan / span-* — behavior scoring, not structured decisions.
        if (own == "respan" || id.Contains("respan/"))
            return true;

        // Check the name part (after the last slash) for known non-decision prefixes.
        var name = id.Contains('/') ? id[(id.LastIndexOf('/') + 1)..] : id;
        if (name.StartsWith("span-"))
            return true;

        return false;
    }
}
