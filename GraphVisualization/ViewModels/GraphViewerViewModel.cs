using System.Windows;
using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GraphVisualization.ViewModels;

/// <summary>
/// View model holding the pan/zoom viewport state of a graph viewer:
/// scale, translation, content/viewport sizes and the zoom commands.
/// Contains no WPF visuals — the view binds its transforms to these properties
/// and forwards raw mouse input here.
/// </summary>
public sealed class GraphViewerViewModel : ObservableObject
{
    /// <summary>Minimum zoom factor.</summary>
    public const double MinScale = 0.2;

    /// <summary>Maximum zoom factor.</summary>
    public const double MaxScale = 3.0;

    private double _scale = 1;
    private double _offsetX;
    private double _offsetY;

    private double _contentWidth = 1;
    private double _contentHeight = 1;
    private double _viewportWidth;
    private double _viewportHeight;

    private bool _initialFitDone;
    private bool _isPanning;
    private Point _panStart;

    public GraphViewerViewModel()
    {
        ZoomInCommand = new RelayCommand(() => ZoomAt(ViewportCenter, 1.2));
        ZoomOutCommand = new RelayCommand(() => ZoomAt(ViewportCenter, 1 / 1.2));
        ActualSizeCommand = new RelayCommand(ResetToActualSize);
        FitToViewCommand = new RelayCommand(FitToView);
    }

    /// <summary>Current zoom factor applied to the graph canvas.</summary>
    public double Scale
    {
        get => _scale;
        private set => SetProperty(ref _scale, value);
    }

    /// <summary>Horizontal translation of the graph canvas in viewport pixels.</summary>
    public double OffsetX
    {
        get => _offsetX;
        private set => SetProperty(ref _offsetX, value);
    }

    /// <summary>Vertical translation of the graph canvas in viewport pixels.</summary>
    public double OffsetY
    {
        get => _offsetY;
        private set => SetProperty(ref _offsetY, value);
    }

    public ICommand ZoomInCommand { get; }

    public ICommand ZoomOutCommand { get; }

    public ICommand ActualSizeCommand { get; }

    public ICommand FitToViewCommand { get; }

    private Point ViewportCenter => new(_viewportWidth / 2, _viewportHeight / 2);

    /// <summary>Updates the laid-out graph size; a pending initial fit is retried.</summary>
    public void SetContentSize(double width, double height)
    {
        _contentWidth = Math.Max(1, width);
        _contentHeight = Math.Max(1, height);
        TryInitialFit();
    }

    /// <summary>Updates the viewport size; a pending initial fit is retried.</summary>
    public void SetViewportSize(double width, double height)
    {
        _viewportWidth = width;
        _viewportHeight = height;
        TryInitialFit();
    }

    /// <summary>Zooms around the given viewport point (mouse position or viewport center).</summary>
    public void ZoomAt(Point pivot, double factor)
    {
        var newScale = Math.Clamp(Scale * factor, MinScale, MaxScale);
        var ratio = newScale / Scale;

        OffsetX = pivot.X - (pivot.X - OffsetX) * ratio;
        OffsetY = pivot.Y - (pivot.Y - OffsetY) * ratio;
        Scale = newScale;
    }

    /// <summary>Begins a drag-to-pan gesture at the given viewport point.</summary>
    public void BeginPan(Point position)
    {
        _panStart = position;
        _isPanning = true;
    }

    /// <summary>Continues the current pan gesture, translating the canvas by the mouse delta.</summary>
    public void UpdatePan(Point position)
    {
        if (!_isPanning)
            return;

        OffsetX += position.X - _panStart.X;
        OffsetY += position.Y - _panStart.Y;
        _panStart = position;
    }

    /// <summary>Ends the current pan gesture.</summary>
    public void EndPan() => _isPanning = false;

    /// <summary>Resets zoom to 100% and centers the content in the viewport.</summary>
    public void ResetToActualSize()
    {
        Scale = 1;
        OffsetX = (_viewportWidth - _contentWidth) / 2;
        OffsetY = (_viewportHeight - _contentHeight) / 2;
    }

    /// <summary>Scales the whole graph to fit the viewport and centers it.</summary>
    public void FitToView()
    {
        if (_viewportWidth <= 1 || _viewportHeight <= 1 || _contentWidth <= 1)
            return;

        var scale = Math.Min(_viewportWidth / _contentWidth, _viewportHeight / _contentHeight);
        Scale = Math.Min(scale, 1.25);
        OffsetX = (_viewportWidth - _contentWidth * Scale) / 2;
        OffsetY = (_viewportHeight - _contentHeight * Scale) / 2;
        _initialFitDone = true;
    }

    private void TryInitialFit()
    {
        // The first fit can only run once both the content and the viewport have real sizes;
        // retry on every size update until it succeeds.
        if (!_initialFitDone && _viewportWidth > 1 && _viewportHeight > 1)
            FitToView();
    }
}
