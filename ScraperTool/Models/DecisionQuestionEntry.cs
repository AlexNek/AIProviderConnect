using CommunityToolkit.Mvvm.ComponentModel;

using AIProviderConnect.Models;

namespace ScraperTool.Models;

/// <summary>
/// Observable decision-question row for the model-test panel.
/// Criteria format depends on <see cref="Kind"/>:
/// Choice — comma-separated <c>label=description</c> pairs;
/// Score — comma-separated scale levels;
/// Noul — optional <c>true=description,false=description</c>.
/// </summary>
public sealed partial class DecisionQuestionEntry : ObservableObject
{
    [ObservableProperty]
    private string _criteriaText = string.Empty;

    [ObservableProperty]
    private string _instructions = string.Empty;

    [ObservableProperty]
    private string _kind = nameof(EDecisionQuestionKind.Choice);

    [ObservableProperty]
    private string _name = string.Empty;
}
