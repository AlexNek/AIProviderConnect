using AIProviderConnect.Abstractions;

using ScraperTool.Services;

namespace ScraperTool.ViewModels;

public interface IWorkPanelFactory
{
    AiUsageHistoryViewModel CreateAiUsageHistory(Action showDashboard);

    CheckDataPanelViewModel CreateCheckDataPanel(
        AiDefinitionAnalyzer analyzer,
        Action showDashboard,
        Action openAiSetup);

    ProviderManualEditorViewModel CreateManualEditor(
        Action showDashboard);

    ScrapePanelViewModel CreateScrapePanel(
        IProviderCatalog catalog,
        Action showDashboard);

    DecisionTreeViewerViewModel CreateDecisionTreeViewer();

    ModelTestPanelViewModel CreateModelTest(Action showDashboard);

    SettingsViewModel CreateSettings();
}
