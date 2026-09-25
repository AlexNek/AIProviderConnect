using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace ScraperTool.Controls;

public sealed partial class LogPanelControl : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IList),
            typeof(LogPanelControl),
            new PropertyMetadata(null, OnItemsSourceChanged));

    private static void OnItemsSourceChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        var control = (LogPanelControl)d;
        if (e.OldValue is INotifyCollectionChanged old)
            old.CollectionChanged -= control.OnLogChanged;
        if (e.NewValue is INotifyCollectionChanged newColl)
            newColl.CollectionChanged += control.OnLogChanged;
    }

    public IList? ItemsSource
    {
        get => (IList?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public LogPanelControl()
    {
        InitializeComponent();
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        var items = LogList.Items.Cast<string>().ToList();
        if (items.Count > 0)
        {
            Clipboard.SetText(string.Join(Environment.NewLine, items));
        }
    }

    private void CopyRow_Click(object sender, RoutedEventArgs e)
    {
        if (LogList.SelectedItem is string row)
        {
            Clipboard.SetText(row);
        }
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            // Use InvokeAsync with Background priority to ensure UI updates happen
            // This forces the UI to process the collection change immediately
            Dispatcher.InvokeAsync(
                () =>
                    {
                        if (LogList.Items.Count > 0)
                        {
                            LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
                        }
                    },
                System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
