using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ScraperTool.Controls;

public sealed partial class DashboardTile
{
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(DashboardTile));

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(DashboardTile));

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public static readonly DependencyProperty DescriptionForegroundProperty =
        DependencyProperty.Register(
            nameof(DescriptionForeground),
            typeof(Brush),
            typeof(DashboardTile),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xCC, 0xE4, 0xFF))));

    public Brush DescriptionForeground
    {
        get => (Brush)GetValue(DescriptionForegroundProperty);
        set => SetValue(DescriptionForegroundProperty, value);
    }

    public static readonly DependencyProperty TileBackgroundProperty =
        DependencyProperty.Register(
            nameof(TileBackground),
            typeof(Brush),
            typeof(DashboardTile),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4))));

    public Brush TileBackground
    {
        get => (Brush)GetValue(TileBackgroundProperty);
        set => SetValue(TileBackgroundProperty, value);
    }

    public static readonly DependencyProperty TileBorderBrushProperty =
        DependencyProperty.Register(nameof(TileBorderBrush), typeof(Brush), typeof(DashboardTile));

    public Brush? TileBorderBrush
    {
        get => (Brush?)GetValue(TileBorderBrushProperty);
        set => SetValue(TileBorderBrushProperty, value);
    }

    public static readonly DependencyProperty TileBorderThicknessProperty =
        DependencyProperty.Register(
            nameof(TileBorderThickness),
            typeof(Thickness),
            typeof(DashboardTile),
            new PropertyMetadata(new Thickness(0)));

    public Thickness TileBorderThickness
    {
        get => (Thickness)GetValue(TileBorderThicknessProperty);
        set => SetValue(TileBorderThicknessProperty, value);
    }

    public static readonly DependencyProperty TileHeightProperty =
        DependencyProperty.Register(
            nameof(TileHeight),
            typeof(double),
            typeof(DashboardTile),
            new PropertyMetadata(130.0));

    public double TileHeight
    {
        get => (double)GetValue(TileHeightProperty);
        set => SetValue(TileHeightProperty, value);
    }

    public static readonly DependencyProperty TileWidthProperty =
        DependencyProperty.Register(
            nameof(TileWidth),
            typeof(double),
            typeof(DashboardTile),
            new PropertyMetadata(170.0));

    public double TileWidth
    {
        get => (double)GetValue(TileWidthProperty);
        set => SetValue(TileWidthProperty, value);
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(DashboardTile));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty TitleForegroundProperty =
        DependencyProperty.Register(
            nameof(TitleForeground),
            typeof(Brush),
            typeof(DashboardTile),
            new PropertyMetadata(new SolidColorBrush(Colors.White)));

    public Brush TitleForeground
    {
        get => (Brush)GetValue(TitleForegroundProperty);
        set => SetValue(TitleForegroundProperty, value);
    }

    public DashboardTile()
    {
        InitializeComponent();
    }
}
