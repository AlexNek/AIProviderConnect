using System.Collections;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScraperTool.Controls;

using ValidationIssue = ScraperTool.Services.ValidationIssue;

public sealed partial class IssuesDataGridControl : UserControl
{
    public static readonly DependencyProperty AiFixSingleIssueCommandProperty =
        DependencyProperty.Register(
            nameof(AiFixSingleIssueCommand),
            typeof(ICommand),
            typeof(IssuesDataGridControl),
            new PropertyMetadata(null));

    public ICommand? AiFixSingleIssueCommand
    {
        get => (ICommand?)GetValue(AiFixSingleIssueCommandProperty);
        set => SetValue(AiFixSingleIssueCommandProperty, value);
    }

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IList),
            typeof(IssuesDataGridControl),
            new PropertyMetadata(null));

    public IList? ItemsSource
    {
        get => (IList?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public IssuesDataGridControl()
    {
        InitializeComponent();
    }

    private static string Clean(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        return s.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");
    }

    private void CopyRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Parent is not ContextMenu ctx)
        {
            return;
        }

        var issue = (ctx.PlacementTarget as FrameworkElement)?.DataContext as ValidationIssue;
        if (issue is null)
        {
            return;
        }

        var text = $"File: {issue.FileName}{Environment.NewLine}" +
                   $"Code: {issue.Code}{Environment.NewLine}" +
                   $"Message: {issue.Message}";
        Clipboard.SetText(text);
    }

    private void CopyTable_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsSource is null)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Code\tFile\tMessage");
        foreach (var item in ItemsSource)
        {
            if (item is not ValidationIssue issue)
            {
                continue;
            }

            sb.Append(Clean(issue.Code));
            sb.Append('\t');
            sb.Append(Clean(issue.FileName));
            sb.Append('\t');
            sb.Append(Clean(issue.Message));
            sb.AppendLine();
        }

        Clipboard.SetText(sb.ToString());
    }
}
