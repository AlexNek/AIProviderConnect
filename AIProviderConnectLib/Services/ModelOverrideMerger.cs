using AIProviderConnect.Models;

namespace AIProviderConnect.Services;

/// <summary>
/// Applies consumer-supplied <see cref="ModelOverride"/> entries over a provider's live model
/// list. Field-level merge: the consumer wins on the fields they set, unset (null) fields keep
/// the live value. Overrides matching no live model are materialized and appended; hidden models
/// are removed. Base order is preserved and new models are appended last.
/// </summary>
public static class ModelOverrideMerger
{
    /// <summary>
    /// Merges <paramref name="overrides"/> over the <paramref name="live"/> model list.
    /// </summary>
    /// <param name="live">The inner provider's live models (may be empty).</param>
    /// <param name="overrides">The consumer overrides, applied in order.</param>
    /// <param name="providerId">The provider id stamped onto materialized models.</param>
    /// <returns>The merged model list.</returns>
    public static IReadOnlyList<AIModel> Merge(
        IReadOnlyList<AIModel> live,
        IReadOnlyList<ModelOverride> overrides,
        string providerId)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentException.ThrowIfNullOrEmpty(providerId);

        if (overrides.Count == 0)
            return live;

        // Working copy preserves base order; removed entries are nulled and compacted at the end.
        var working = new List<AIModel?>(live);
        var positionById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < working.Count; i++)
        {
            var id = working[i]!.Id;
            if (!positionById.ContainsKey(id))
                positionById[id] = i;
        }

        foreach (var modelOverride in overrides)
        {
            if (modelOverride.Hidden)
            {
                if (positionById.TryGetValue(modelOverride.Id, out var hiddenIndex))
                {
                    working[hiddenIndex] = null;
                    positionById.Remove(modelOverride.Id);
                }

                continue;
            }

            if (positionById.TryGetValue(modelOverride.Id, out var index))
            {
                working[index] = Patch(working[index]!, modelOverride);
                continue;
            }

            working.Add(Materialize(modelOverride, providerId));
            positionById[modelOverride.Id] = working.Count - 1;
        }

        var merged = new List<AIModel>(working.Count);
        foreach (var model in working)
        {
            if (model is not null)
                merged.Add(model);
        }

        return merged;
    }

    private static AIModel Patch(AIModel target, ModelOverride modelOverride) => target with
    {
        DisplayName = modelOverride.DisplayName ?? target.DisplayName,
        Description = modelOverride.Description ?? target.Description,
        Modality = modelOverride.Modality ?? target.Modality,
        OwnedBy = modelOverride.OwnedBy ?? target.OwnedBy,
        PromptPrice = modelOverride.PromptPrice ?? target.PromptPrice,
        CompletionPrice = modelOverride.CompletionPrice ?? target.CompletionPrice,
        PriceUnit = modelOverride.PriceUnit ?? target.PriceUnit,
        ContextWindow = modelOverride.ContextWindow ?? target.ContextWindow,
        Capabilities = modelOverride.Capabilities ?? target.Capabilities
    };

    private static AIModel Materialize(ModelOverride modelOverride, string providerId)
    {
        var model = new AIModel
        {
            Id = modelOverride.Id,
            DisplayName = modelOverride.DisplayName ?? modelOverride.Id,
            ProviderId = providerId
        };

        return Patch(model, modelOverride);
    }

}
