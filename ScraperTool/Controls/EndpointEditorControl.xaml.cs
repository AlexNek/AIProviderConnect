using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScraperTool.Controls;

/// <summary>
/// Edits the per-operation endpoint overrides of a provider definition: one row
/// per operation with the operation name, relative path, optional base-URL
/// override, and optional protocol override. Mirrors
/// <see cref="KeyValueEditorControl"/>'s dependency-property surface.
/// </summary>
public partial class EndpointEditorControl : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(EndpointEditorControl),
        new PropertyMetadata(null));

    public static readonly DependencyProperty AddCommandProperty = DependencyProperty.Register(
        nameof(AddCommand),
        typeof(ICommand),
        typeof(EndpointEditorControl),
        new PropertyMetadata(null));

    public static readonly DependencyProperty RemoveCommandProperty = DependencyProperty.Register(
        nameof(RemoveCommand),
        typeof(ICommand),
        typeof(EndpointEditorControl),
        new PropertyMetadata(null));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(EndpointEditorControl),
        new PropertyMetadata(string.Empty, OnTitleChanged));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(string),
        typeof(EndpointEditorControl),
        new PropertyMetadata(string.Empty, OnDescriptionChanged));

    public IEnumerable ItemsSource
    {
        get => (IEnumerable)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ICommand AddCommand
    {
        get => (ICommand)GetValue(AddCommandProperty);
        set => SetValue(AddCommandProperty, value);
    }

    public ICommand RemoveCommand
    {
        get => (ICommand)GetValue(RemoveCommandProperty);
        set => SetValue(RemoveCommandProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (EndpointEditorControl)d;
        control.TitleElement.Text = e.NewValue as string ?? string.Empty;
    }

    private static void OnDescriptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (EndpointEditorControl)d;
        control.DescriptionElement.Text = e.NewValue as string ?? string.Empty;
    }

    public EndpointEditorControl()
    {
        InitializeComponent();
    }
}
