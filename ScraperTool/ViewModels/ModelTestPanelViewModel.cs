using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ScraperTool.Models;
using ScraperTool.Services;
using ScraperTool.Views;

using Serilog;

namespace ScraperTool.ViewModels;

/// <summary>
/// Work-panel view model for testing embedding and decision models end-to-end.
/// Uses the provider and API key already configured in AI Setup.
/// The user picks a model, sends a real request, and sees the response with token usage and cost.
/// Cumulative counters track total tokens and cost across repeated calls.
/// </summary>
public sealed partial class ModelTestPanelViewModel : ObservableObject
{
    private readonly ITransientCredentialProviderFactory _providerFactory;
    private readonly AppSettings _settings;
    private readonly Action _showDashboard;

    // ── Model discovery ─────────────────────────────────────────────────

    [ObservableProperty]
    private string _modelPriceText = string.Empty;

    [ObservableProperty]
    private string _providerLabel = string.Empty;

    private List<AIModel>? _loadedModels;

    public ObservableCollection<ModelSelectionItem> LoadedModels { get; } = [];

    // ── Embedding tab ───────────────────────────────────────────────────

    [ObservableProperty]
    private string _embeddingModelId = string.Empty;

    [ObservableProperty]
    private string _embeddingInput = "The quick brown fox jumps over the lazy dog.";

    [ObservableProperty]
    private string _embeddingResult = string.Empty;

    [ObservableProperty]
    private string _embeddingStatus = string.Empty;

    // ── Decision tab ────────────────────────────────────────────────────

    [ObservableProperty]
    private string _decisionModelId = string.Empty;

    [ObservableProperty]
    private string _decisionState = "The system is running normally. CPU at 45%.";

    [ObservableProperty]
    private string _decisionResult = string.Empty;

    [ObservableProperty]
    private string _decisionStatus = string.Empty;

    public ObservableCollection<DecisionQuestionEntry> DecisionQuestions { get; } = [];

    // ── Shared ──────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private string _statusText = string.Empty;

    private int _embeddingTotalTokens;
    private decimal _embeddingTotalCost;
    private int _decisionTotalTokens;
    private decimal _decisionTotalCost;

    [ObservableProperty]
    private string _embeddingCumulativeLabel = string.Empty;

    [ObservableProperty]
    private string _decisionCumulativeLabel = string.Empty;

    // ── Construction ────────────────────────────────────────────────────

    public ModelTestPanelViewModel(
        ITransientCredentialProviderFactory providerFactory,
        AppSettings settings,
        Action showDashboard)
    {
        _providerFactory = providerFactory;
        _settings = settings;
        _showDashboard = showDashboard;

        ProviderLabel = _settings.SelectedProviderId ?? string.Empty;
        EmbeddingModelId = _settings.EmbeddingModel ?? string.Empty;
        DecisionModelId = _settings.DecisionModel ?? string.Empty;

        // Pre-populate one question of each kind so the user can test all 3 answer types immediately.
        DecisionQuestions.Add(new DecisionQuestionEntry
        {
            Name = "severity",
            Kind = nameof(EDecisionQuestionKind.Choice),
            CriteriaText = "low=Minor issue, no user impact, medium=Degraded performance, high=Service outage",
            Instructions = "Classify the severity of the current system state."
        });
        DecisionQuestions.Add(new DecisionQuestionEntry
        {
            Name = "needs_intervention",
            Kind = nameof(EDecisionQuestionKind.Noul),
            CriteriaText = "true=Manual action required, false=System can self-recover",
            Instructions = "Does this situation require human intervention?"
        });
        DecisionQuestions.Add(new DecisionQuestionEntry
        {
            Name = "health_score",
            Kind = nameof(EDecisionQuestionKind.Score),
            CriteriaText = "Critical, Degraded, Fair, Good, Excellent",
            Instructions = "Rate the overall system health on this scale."
        });
    }

    /// <summary>
    /// Loads models from the configured provider. Called once when the panel opens.
    /// </summary>
    public async Task InitializeAsync()
    {
        var providerId = _settings.SelectedProviderId;
        var apiKey = _settings.ApiKey;
        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(apiKey))
        {
            StatusText = "Configure provider and API key in AI Setup first.";
            return;
        }

        StatusText = "Loading models...";
        try
        {
            var provider = _providerFactory.GetProvider(providerId, apiKey);
            if (provider is not IModelDiscoveryProvider discovery
                || !discovery.SupportsModelDiscovery)
            {
                StatusText = "Configured provider does not support model discovery.";
                return;
            }

            var models = await discovery.GetModelsAsync();
            _loadedModels = models.ToList();
            LoadedModels.Clear();
            foreach (var m in models.OrderBy(m => m.Id))
                LoadedModels.Add(ModelSelectionItem.FromAIModel(m));
            StatusText = $"Loaded {LoadedModels.Count} models from {providerId}.";

            // Restore price text for previously saved model selections.
            if (!string.IsNullOrWhiteSpace(EmbeddingModelId))
                UpdateModelPriceText(EmbeddingModelId);
            if (!string.IsNullOrWhiteSpace(DecisionModelId))
                UpdateModelPriceText(DecisionModelId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load models for {Provider}", providerId);
            StatusText = $"Failed to load models: {ex.Message}";
        }
    }

    // ── Model selection ─────────────────────────────────────────────────

    [RelayCommand]
    private void SelectEmbeddingModel()
    {
        OpenModelPicker(EModelCapability.Embedding, id => EmbeddingModelId = id);
    }

    [RelayCommand]
    private void SelectDecisionModel()
    {
        OpenModelPicker(EModelCapability.Decision, id => DecisionModelId = id);
    }

    private void OpenModelPicker(EModelCapability capability, Action<string> setId)
    {
        var window = new ModelGridSelectorWindow { Owner = GetActiveWindow() };
        window.LoadModels(
            LoadedModels,
            initialSelectionId: capability == EModelCapability.Embedding
                ? EmbeddingModelId
                : DecisionModelId,
            sourceLabel: ProviderLabel,
            requiredCapability: capability);
        if (window.ShowDialog() == true && window.SelectedItem is not null)
        {
            setId(window.SelectedItem.Id);
            UpdateModelPriceText(window.SelectedItem.Id);
            SaveModelSelection(capability, window.SelectedItem.Id);
        }
    }

    private void UpdateModelPriceText(string modelId)
    {
        var model = _loadedModels?.FirstOrDefault(
            m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));
        if (model is null)
        {
            ModelPriceText = string.Empty;
            return;
        }

        var parts = new List<string>();
        if (model.PromptPrice.HasValue)
            parts.Add($"${model.PromptPrice:0.######} in / 1M tokens");
        if (model.CompletionPrice.HasValue)
            parts.Add($"${model.CompletionPrice:0.######} out / 1M tokens");
        ModelPriceText = parts.Count > 0
            ? string.Join(" | ", parts)
            : "Price: not reported";
    }

    private void SaveModelSelection(EModelCapability capability, string modelId)
    {
        switch (capability)
        {
            case EModelCapability.Embedding:
                _settings.EmbeddingModel = modelId;
                break;
            case EModelCapability.Decision:
                _settings.DecisionModel = modelId;
                break;
        }

        try { _settings.Save(); }
        catch (Exception ex) { Log.Warning(ex, "Failed to persist model selection"); }
    }

    // ── Embedding test ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task TestEmbeddingAsync()
    {
        if (string.IsNullOrWhiteSpace(EmbeddingModelId))
        { EmbeddingStatus = "Select an embedding model first."; return; }
        if (string.IsNullOrWhiteSpace(EmbeddingInput))
        { EmbeddingStatus = "Enter input text."; return; }

        var providerId = _settings.SelectedProviderId;
        var apiKey = _settings.ApiKey;
        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(apiKey))
        { EmbeddingStatus = "Configure provider and API key in AI Setup first."; return; }

        IsTesting = true;
        EmbeddingStatus = "Sending embedding request...";
        try
        {
            var provider = _providerFactory.GetProvider(providerId, apiKey);
            if (provider is not IEmbeddingProvider embeddingProvider)
            {
                EmbeddingStatus = "Selected provider does not support embeddings.";
                return;
            }

            var request = new EmbeddingRequest
            {
                Model = EmbeddingModelId,
                Input = new[] { EmbeddingInput }
            };
            var response = await embeddingProvider.EmbedAsync(request);

            var sb = new StringBuilder();
            sb.AppendLine($"Model: {response.Model}");
            sb.AppendLine($"Vectors: {response.Data.Count}");
            foreach (var d in response.Data)
            {
                sb.AppendLine($"  [{d.Index}] dimensions={d.Embedding.Length}");
                var preview = string.Join(", ", d.Embedding.Take(5).Select(
                    f => f.ToString("F4", CultureInfo.InvariantCulture)));
                sb.AppendLine($"    values: {preview}{(d.Embedding.Length > 5 ? ", ..." : "")}");
            }

            sb.AppendLine();
            sb.AppendLine($"Tokens: {response.Usage.PromptTokens}");
            EmbeddingResult = sb.ToString();

            // Cumulative tracking
            _embeddingTotalTokens += response.Usage.PromptTokens;
            var model = _loadedModels?.FirstOrDefault(
                m => string.Equals(m.Id, EmbeddingModelId, StringComparison.OrdinalIgnoreCase));
            if (model?.PromptPrice.HasValue == true)
                _embeddingTotalCost += response.Usage.PromptTokens * model.PromptPrice.Value / 1_000_000m;
            EmbeddingCumulativeLabel =
                $"Cumulative: {_embeddingTotalTokens} tokens, {_embeddingTotalCost:0.000000} USD";

            EmbeddingStatus = "Success.";
            StatusText = "Embedding test succeeded.";
            Log.Information(
                "Embedding test for {Provider}/{Model}: {Tokens} tokens",
                providerId, EmbeddingModelId, response.Usage.PromptTokens);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Embedding test failed for {Provider}", providerId);
            EmbeddingStatus = $"Error: {ex.Message}";
            EmbeddingResult = string.Empty;
        }
        finally
        {
            IsTesting = false;
        }
    }

    // ── Decision test ───────────────────────────────────────────────────

    [RelayCommand]
    private void AddQuestion()
    {
        DecisionQuestions.Add(new DecisionQuestionEntry
        {
            Name = $"q{DecisionQuestions.Count + 1}",
            Kind = nameof(EDecisionQuestionKind.Choice)
        });
    }

    [RelayCommand]
    private void RemoveQuestion(DecisionQuestionEntry? entry)
    {
        if (entry is not null)
            DecisionQuestions.Remove(entry);
    }

    [RelayCommand]
    private async Task TestDecisionAsync()
    {
        if (string.IsNullOrWhiteSpace(DecisionModelId))
        { DecisionStatus = "Select a decision model first."; return; }
        if (DecisionQuestions.Count == 0)
        { DecisionStatus = "Add at least one question."; return; }

        var providerId = _settings.SelectedProviderId;
        var apiKey = _settings.ApiKey;
        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(apiKey))
        { DecisionStatus = "Configure provider and API key in AI Setup first."; return; }

        IsTesting = true;
        DecisionStatus = "Sending decision request...";
        try
        {
            var provider = _providerFactory.GetProvider(providerId, apiKey);
            if (provider is not IDecisionProvider decisionProvider
                || !decisionProvider.SupportsDecisions)
            {
                DecisionStatus = "Selected provider does not support decisions.";
                return;
            }

            var questions = new Dictionary<string, DecisionQuestion>();
            foreach (var q in DecisionQuestions)
            {
                if (string.IsNullOrWhiteSpace(q.Name)) continue;
                questions[q.Name] = BuildQuestion(q);
            }

            if (questions.Count == 0)
            {
                DecisionStatus = "All questions have empty names.";
                return;
            }

            var request = new DecisionRequest
            {
                Model = DecisionModelId,
                State = DecisionState,
                Questions = questions
            };
            var response = await decisionProvider.DecideAsync(request);

            var sb = new StringBuilder();
            sb.AppendLine($"Model: {response.Model}");
            sb.AppendLine($"Provider: {response.Provider}");
            sb.AppendLine();
            foreach (var (key, answer) in response.Answers)
            {
                sb.AppendLine($"  {key}:");
                switch (answer.Kind)
                {
                    case EDecisionAnswerKind.Choice when answer.Choice is not null:
                        sb.AppendLine($"    Selection: {answer.Choice.Selected}");
                        sb.AppendLine($"    Confidence: {answer.Choice.Confidence:P1}");
                        foreach (var (opt, prob) in answer.Choice.Probabilities)
                            sb.AppendLine($"      {opt}: {prob:P1}");
                        break;
                    case EDecisionAnswerKind.Noul when answer.Noul is not null:
                        sb.AppendLine($"    P(yes): {answer.Noul.ProbabilityOfYes:P1}");
                        break;
                    case EDecisionAnswerKind.Score when answer.Score is not null:
                        sb.AppendLine($"    Score: {answer.Score.Score:F2}");
                        sb.AppendLine($"    Confidence: {answer.Score.Confidence:P1}");
                        foreach (var level in answer.Score.Legend)
                            sb.AppendLine($"      {level}");
                        break;
                    default:
                        sb.AppendLine($"    (unknown answer kind: {answer.Kind})");
                        break;
                }
            }

            sb.AppendLine();
            sb.AppendLine($"Tokens: {response.Usage.PromptTokens} prompt + {response.Usage.CompletionTokens} completion = {response.Usage.TotalTokens} total");
            if (response.Usage.Cost.HasValue)
                sb.AppendLine($"Cost: {response.Usage.Cost.Value:0.000000} USD (reported)");
            DecisionResult = sb.ToString();

            // Cumulative tracking
            _decisionTotalTokens += response.Usage.TotalTokens;
            if (response.Usage.Cost.HasValue)
                _decisionTotalCost += response.Usage.Cost.Value;
            DecisionCumulativeLabel =
                $"Cumulative: {_decisionTotalTokens} tokens, {_decisionTotalCost:0.000000} USD";

            DecisionStatus = "Success.";
            StatusText = "Decision test succeeded.";
            Log.Information(
                "Decision test for {Provider}/{Model}: {Tokens} tokens",
                providerId, DecisionModelId, response.Usage.TotalTokens);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Decision test failed for {Provider}", providerId);
            DecisionStatus = $"Error: {ex.Message}";
            DecisionResult = string.Empty;
        }
        finally
        {
            IsTesting = false;
        }
    }

    private static DecisionQuestion BuildQuestion(DecisionQuestionEntry entry)
    {
        if (!Enum.TryParse<EDecisionQuestionKind>(entry.Kind, ignoreCase: true, out var kind))
            kind = EDecisionQuestionKind.Choice;

        IReadOnlyDictionary<string, string>? criteria = null;
        IReadOnlyList<string>? scale = null;

        switch (kind)
        {
            case EDecisionQuestionKind.Choice:
                criteria = ParseKeyValuePairs(entry.CriteriaText);
                break;
            case EDecisionQuestionKind.Noul:
                if (!string.IsNullOrWhiteSpace(entry.CriteriaText))
                    criteria = ParseKeyValuePairs(entry.CriteriaText);
                break;
            case EDecisionQuestionKind.Score:
                if (!string.IsNullOrWhiteSpace(entry.CriteriaText))
                    scale = entry.CriteriaText
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();
                break;
        }

        return new DecisionQuestion
        {
            Kind = kind,
            Criteria = criteria,
            Scale = scale,
            Instructions = string.IsNullOrWhiteSpace(entry.Instructions) ? null : entry.Instructions
        };
    }

    private static Dictionary<string, string> ParseKeyValuePairs(string text)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        foreach (var pair in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eqIdx = pair.IndexOf('=');
            if (eqIdx > 0)
                result[pair[..eqIdx].Trim()] = pair[(eqIdx + 1)..].Trim();
            else
                result[pair.Trim()] = pair.Trim();
        }
        return result;
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static Window? GetActiveWindow() =>
        Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive);
}
