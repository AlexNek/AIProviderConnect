using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScraperTool.Controls;

public sealed partial class UrlFixActionBar : UserControl
{
    public static readonly DependencyProperty AiFixUrlsCommandProperty =
        DependencyProperty.Register(
            nameof(AiFixUrlsCommand),
            typeof(ICommand),
            typeof(UrlFixActionBar),
            new PropertyMetadata(null));

    public ICommand? AiFixUrlsCommand
    {
        get => (ICommand?)GetValue(AiFixUrlsCommandProperty);
        set => SetValue(AiFixUrlsCommandProperty, value);
    }

    public static readonly DependencyProperty AiRetryFailedCommandProperty =
        DependencyProperty.Register(
            nameof(AiRetryFailedCommand),
            typeof(ICommand),
            typeof(UrlFixActionBar),
            new PropertyMetadata(null));

    public ICommand? AiRetryFailedCommand
    {
        get => (ICommand?)GetValue(AiRetryFailedCommandProperty);
        set => SetValue(AiRetryFailedCommandProperty, value);
    }

    public static readonly DependencyProperty CostSummaryProperty =
        DependencyProperty.Register(
            nameof(CostSummary),
            typeof(string),
            typeof(UrlFixActionBar),
            new PropertyMetadata(string.Empty));

    public string CostSummary
    {
        get => (string)GetValue(CostSummaryProperty);
        set => SetValue(CostSummaryProperty, value);
    }

    public static readonly DependencyProperty HasAiSetupNeededProperty =
        DependencyProperty.Register(
            nameof(HasAiSetupNeeded),
            typeof(bool),
            typeof(UrlFixActionBar),
            new PropertyMetadata(false));

    public bool HasAiSetupNeeded
    {
        get => (bool)GetValue(HasAiSetupNeededProperty);
        set => SetValue(HasAiSetupNeededProperty, value);
    }

    public static readonly DependencyProperty HasFailedItemsProperty =
        DependencyProperty.Register(
            nameof(HasFailedItems),
            typeof(bool),
            typeof(UrlFixActionBar),
            new PropertyMetadata(false));

    public bool HasFailedItems
    {
        get => (bool)GetValue(HasFailedItemsProperty);
        set => SetValue(HasFailedItemsProperty, value);
    }

    public static readonly DependencyProperty HasTokenUsageProperty =
        DependencyProperty.Register(
            nameof(HasTokenUsage),
            typeof(bool),
            typeof(UrlFixActionBar),
            new PropertyMetadata(false));

    public bool HasTokenUsage
    {
        get => (bool)GetValue(HasTokenUsageProperty);
        set => SetValue(HasTokenUsageProperty, value);
    }

    public static readonly DependencyProperty HasUrlIssuesProperty =
        DependencyProperty.Register(
            nameof(HasUrlIssues),
            typeof(bool),
            typeof(UrlFixActionBar),
            new PropertyMetadata(false));

    public bool HasUrlIssues
    {
        get => (bool)GetValue(HasUrlIssuesProperty);
        set => SetValue(HasUrlIssuesProperty, value);
    }

    public static readonly DependencyProperty OpenAiSetupCommandProperty =
        DependencyProperty.Register(
            nameof(OpenAiSetupCommand),
            typeof(ICommand),
            typeof(UrlFixActionBar),
            new PropertyMetadata(null));

    public ICommand? OpenAiSetupCommand
    {
        get => (ICommand?)GetValue(OpenAiSetupCommandProperty);
        set => SetValue(OpenAiSetupCommandProperty, value);
    }

    public static readonly DependencyProperty TokenSummaryProperty =
        DependencyProperty.Register(
            nameof(TokenSummary),
            typeof(string),
            typeof(UrlFixActionBar),
            new PropertyMetadata(string.Empty));

    public string TokenSummary
    {
        get => (string)GetValue(TokenSummaryProperty);
        set => SetValue(TokenSummaryProperty, value);
    }

    public UrlFixActionBar()
    {
        InitializeComponent();
    }
}
