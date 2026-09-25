using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

using GraphVisualization.Layout;
using GraphVisualization.Model;
using GraphVisualization.Styling;
using GraphVisualization.ViewModels;

namespace GraphVisualization.Controls;

/// <summary>
/// Renders any <see cref="GraphVisualModel"/> as an interactive node-edge diagram
/// (layered top-to-bottom layout). Delegates geometry to <see cref="IGraphLayoutEngine"/>
/// and pan/zoom state to <see cref="GraphViewerViewModel"/>; this class only draws
/// pre-styled visuals and forwards raw mouse input.
/// </summary>
public sealed partial class GraphViewerControl
{
    private readonly IGraphLayoutEngine _layoutEngine = new HierarchicalGraphLayoutEngine();

    private readonly LayoutOptions _options = new();

    private readonly GraphViewerViewModel _viewModel = new();

    private readonly ScaleTransform _scaleTransform = new(1, 1);
    private readonly TranslateTransform _translateTransform = new(0, 0);

    /// <summary>Horizontal inset of a hexagon's flat top/bottom edges; shared by drawing and edge anchoring.</summary>
    private const double HexagonInset = 18;

    /// <summary>Current node centers (canvas space); mutated by node dragging.</summary>
    private readonly Dictionary<string, Point> _positions = new();

    /// <summary>Drawn elements per node with offsets from the node center, so a drag moves them all.</summary>
    private readonly Dictionary<string, List<(FrameworkElement Element, double OffsetX, double OffsetY)>> _nodeElements = new();

    /// <summary>Drawn edges with their routing inputs, so a drag re-routes connected edges.</summary>
    private readonly List<EdgeDrawState> _edgeStates = new();

    private string? _dragNodeId;
    private Point _dragStart;
    private Point _dragOriginalCenter;

    public static readonly DependencyProperty GraphProperty =
        DependencyProperty.Register(
            nameof(Graph),
            typeof(GraphVisualModel),
            typeof(GraphViewerControl),
            new PropertyMetadata(null, OnGraphChanged));

    public GraphVisualModel? Graph
    {
        get => (GraphVisualModel?)GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    /// <summary>Optional caller-supplied legend rendered in the toolbar between the fit button and the hint.</summary>
    public static readonly DependencyProperty LegendContentProperty =
        DependencyProperty.Register(
            nameof(LegendContent),
            typeof(object),
            typeof(GraphViewerControl),
            new PropertyMetadata(null));

    public object? LegendContent
    {
        get => GetValue(LegendContentProperty);
        set => SetValue(LegendContentProperty, value);
    }

    public GraphViewerControl()
    {
        InitializeComponent();

        // Toolbar commands resolve against the pan/zoom view model; the control's own
        // DataContext is left untouched so external Graph/LegendContent bindings keep working
        Toolbar.DataContext = _viewModel;

        var group = new TransformGroup();
        group.Children.Add(_scaleTransform);
        group.Children.Add(_translateTransform);
        GraphCanvas.RenderTransform = group;

        // Render transforms mirror the view model's viewport state
        var scaleBinding = new Binding(nameof(GraphViewerViewModel.Scale)) { Source = _viewModel };
        BindingOperations.SetBinding(_scaleTransform, ScaleTransform.ScaleXProperty, scaleBinding);
        BindingOperations.SetBinding(_scaleTransform, ScaleTransform.ScaleYProperty, scaleBinding);
        BindingOperations.SetBinding(_translateTransform, TranslateTransform.XProperty,
            new Binding(nameof(GraphViewerViewModel.OffsetX)) { Source = _viewModel });
        BindingOperations.SetBinding(_translateTransform, TranslateTransform.YProperty,
            new Binding(nameof(GraphViewerViewModel.OffsetY)) { Source = _viewModel });
    }

    private static void OnGraphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((GraphViewerControl)d).Rebuild();
    }

    private void Rebuild()
    {
        GraphCanvas.Children.Clear();
        var model = Graph;
        if (model is null)
        {
            GraphCanvas.Width = 1;
            GraphCanvas.Height = 1;
            return;
        }

        var edgeColors = new Dictionary<(string From, string To), string>();
        var exitSides = new Dictionary<(string From, string To), EEdgeExitSide>();
        foreach (var edge in model.Edges)
        {
            edgeColors.TryAdd((edge.From, edge.To), edge.ColorHex);
            exitSides.TryAdd((edge.From, edge.To), edge.ExitSide);
        }

        var layout = _layoutEngine.Layout(
            model.Nodes.Select(n => new LayoutNode(n.Id)).ToList(),
            model.Edges.Select(e => new LayoutEdge(e.From, e.To, e.Label)).ToList(),
            model.StartNodeId,
            _options);

        foreach (var (id, pos) in layout.Nodes)
            _positions[id] = new Point(pos.X, pos.Y);

        var shapes = model.Nodes.ToDictionary(n => n.Id, n => n.Style.Shape);

        bool IsRightExit(RoutedEdge e)
        {
            var side = exitSides.GetValueOrDefault((e.From, e.To));
            var downward = !e.IsBackEdge && layout.Nodes[e.To].Y > layout.Nodes[e.From].Y;
            return side == EEdgeExitSide.Right || (side == EEdgeExitSide.Auto && !downward);
        }

        var bottomSourceAnchors = AnchorFractions(layout, layout.Edges.Where(e => !IsRightExit(e)), e => e.From, e => e.To, orderByY: false);
        var rightSourceAnchors = AnchorFractions(layout, layout.Edges.Where(IsRightExit), e => e.From, e => e.To, orderByY: true);
        var targetAnchors = AnchorFractions(layout, layout.Edges, e => e.To, e => e.From, orderByY: false);

        foreach (var edge in layout.Edges)
        {
            var colorHex = edgeColors.TryGetValue((edge.From, edge.To), out var hex) ? hex : "#007BFF";
            CreateEdge(edge, exitSides.GetValueOrDefault((edge.From, edge.To)), colorHex,
                IsRightExit(edge) ? rightSourceAnchors[edge] : bottomSourceAnchors[edge],
                targetAnchors[edge],
                shapes.GetValueOrDefault(edge.From, ENodeShape.RoundedBox),
                shapes.GetValueOrDefault(edge.To, ENodeShape.RoundedBox));
        }

        foreach (var visual in model.Nodes)
        {
            DrawNode(visual, visual.Id == model.StartNodeId);
        }

        GraphCanvas.Width = layout.Width;
        GraphCanvas.Height = layout.Height;
        _viewModel.SetContentSize(layout.Width, layout.Height);

        // Push the viewport size once layout has settled so the initial fit can run
        Dispatcher.BeginInvoke(new System.Action(() =>
            _viewModel.SetViewportSize(Viewport.ActualWidth, Viewport.ActualHeight)));
    }

    // ── Drawing ─────────────────────────────────────────────────────

    private void DrawNode(NodeVisual visual, bool isStart)
    {
        if (!_positions.TryGetValue(visual.Id, out var pos))
            return;

        var elements = new List<(FrameworkElement Element, double OffsetX, double OffsetY)>();

        var fill = Brush(visual.Style.FillHex);
        var stroke = Brush(visual.Style.StrokeHex);

        Shape shape = visual.Style.Shape switch
        {
            ENodeShape.Diamond => MakeDiamond(fill, stroke),
            ENodeShape.Hexagon => MakeHexagon(fill, stroke),
            _ => new Rectangle
            {
                Width = _options.NodeWidth,
                Height = _options.NodeHeight,
                RadiusX = 8,
                RadiusY = 8,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = 1.5
            }
        };

        if (isStart)
        {
            shape.StrokeThickness = 3;
            shape.Stroke = Brushes.DarkBlue;
        }

        Canvas.SetLeft(shape, pos.X - _options.NodeWidth / 2);
        Canvas.SetTop(shape, pos.Y - _options.NodeHeight / 2);
        GraphCanvas.Children.Add(shape);
        elements.Add((shape, -_options.NodeWidth / 2, -_options.NodeHeight / 2));

        // Labels: title / subtitle / detail
        AddText(elements, pos, visual.Title, -22, 11, FontWeights.SemiBold, Brushes.Black);
        AddText(elements, pos, visual.Subtitle, -8, 9, FontWeights.Normal, Brushes.Gray);
        AddText(elements, pos, visual.Detail, 8, 10, FontWeights.Normal, Brushes.Black);

        _nodeElements[visual.Id] = elements;
    }

    private void AddText(List<(FrameworkElement Element, double OffsetX, double OffsetY)> elements,
        Point center, string text, double offsetY, double size, FontWeight weight, Brush foreground)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var tb = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            Foreground = foreground,
            FontFamily = new FontFamily("Segoe UI")
        };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(tb, center.X - tb.DesiredSize.Width / 2);
        Canvas.SetTop(tb, center.Y + offsetY - tb.DesiredSize.Height / 2);
        GraphCanvas.Children.Add(tb);
        elements.Add((tb, -tb.DesiredSize.Width / 2, offsetY - tb.DesiredSize.Height / 2));
    }

    private Polygon MakeDiamond(Brush fill, Brush stroke)
    {
        return new Polygon
        {
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = 1.5,
            Points = new PointCollection
            {
                new(_options.NodeWidth / 2, 0),
                new(_options.NodeWidth, _options.NodeHeight / 2),
                new(_options.NodeWidth / 2, _options.NodeHeight),
                new(0, _options.NodeHeight / 2)
            }
        };
    }

    private Polygon MakeHexagon(Brush fill, Brush stroke)
    {
        return new Polygon
        {
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = 1.5,
            Points = new PointCollection
            {
                new(HexagonInset, 0),
                new(_options.NodeWidth - HexagonInset, 0),
                new(_options.NodeWidth, _options.NodeHeight / 2),
                new(_options.NodeWidth - HexagonInset, _options.NodeHeight),
                new(HexagonInset, _options.NodeHeight),
                new(0, _options.NodeHeight / 2)
            }
        };
    }

    private void CreateEdge(RoutedEdge edge, EEdgeExitSide exitSide, string colorHex,
        double sourceAnchor, double targetAnchor, ENodeShape sourceShape, ENodeShape targetShape)
    {
        var brush = Brush(colorHex);

        var path = new Path { Stroke = brush, StrokeThickness = 1.5 };
        var arrow = new Polygon { Fill = brush };
        var label = new TextBlock
        {
            Text = edge.Label,
            FontSize = 9,
            Foreground = brush,
            Background = Brushes.White,
            FontFamily = new FontFamily("Segoe UI")
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        GraphCanvas.Children.Add(path);
        GraphCanvas.Children.Add(arrow);
        GraphCanvas.Children.Add(label);

        var state = new EdgeDrawState
        {
            Edge = edge,
            ExitSide = exitSide,
            SourceAnchor = sourceAnchor,
            TargetAnchor = targetAnchor,
            SourceShape = sourceShape,
            TargetShape = targetShape,
            Path = path,
            Arrow = arrow,
            Label = label
        };
        _edgeStates.Add(state);
        UpdateEdgeGeometry(state);
    }

    /// <summary>Recomputes one edge's path, arrowhead, and label from the current node positions.</summary>
    private void UpdateEdgeGeometry(EdgeDrawState state)
    {
        if (!_positions.TryGetValue(state.Edge.From, out var from)
            || !_positions.TryGetValue(state.Edge.To, out var to))
            return;

        var downward = !state.Edge.IsBackEdge && to.Y > from.Y;
        var exitRight = state.ExitSide == EEdgeExitSide.Right
                        || (state.ExitSide == EEdgeExitSide.Auto && !downward);

        var start = exitRight
            ? RightAnchor(from, state.SourceShape, state.SourceAnchor)
            : BottomAnchor(from, state.SourceShape, state.SourceAnchor);

        Point end, control1, control2, arrowTip, arrowBase;

        if (downward)
        {
            // Enters the target's top side; leaves the source's bottom or right side
            var top = TopAnchor(to, state.TargetShape, state.TargetAnchor);
            end = new Point(top.X, top.Y - 6);
            var dy = Math.Max(20, (end.Y - start.Y) / 2);
            control1 = exitRight
                ? new Point(start.X + Math.Max(40, (end.X - start.X) / 2), start.Y)
                : new Point(start.X, start.Y + dy);
            control2 = new Point(end.X, end.Y - dy);
            arrowTip = new Point(end.X, end.Y + 6);
            arrowBase = end;
        }
        else
        {
            // Same-layer or back edge: loops around the right side, dashed,
            // leaving the source's right side (or bottom when the caller pinned it there)
            end = new Point(to.X + _options.NodeWidth / 2 + 6, to.Y);
            var bulge = Math.Max(60, Math.Abs(from.Y - to.Y) * 0.4) + 30;
            control1 = exitRight
                ? new Point(start.X + bulge, start.Y)
                : new Point(start.X, start.Y + bulge);
            control2 = new Point(end.X + bulge, end.Y);
            arrowTip = new Point(end.X - 6, end.Y);
            arrowBase = end;
        }

        state.Path.Data = new PathGeometry
        {
            Figures =
            {
                new PathFigure
                {
                    StartPoint = start,
                    Segments = { new BezierSegment(control1, control2, end, true) }
                }
            }
        };
        state.Path.StrokeDashArray = downward ? new DoubleCollection() : new DoubleCollection { 4, 3 };

        state.Arrow.Points = downward
            ? new PointCollection
            {
                arrowTip,
                new Point(arrowBase.X - 5, arrowBase.Y),
                new Point(arrowBase.X + 5, arrowBase.Y)
            }
            : new PointCollection
            {
                arrowTip,
                new Point(arrowBase.X, arrowBase.Y - 5),
                new Point(arrowBase.X, arrowBase.Y + 5)
            };

        double labelX, labelY;
        if (downward)
        {
            if (exitRight)
            {
                labelX = (start.X + end.X) / 2 + 24;
                labelY = (start.Y + end.Y) / 2;
            }
            else
            {
                // Stagger labels along the edge so parallel edges between the same
                // node pair (e.g. success/transientFailure/permanentFailure) never overprint
                var t = 0.2 + 0.6 * state.SourceAnchor;
                var sourceBottomY = from.Y + _options.NodeHeight / 2;
                var targetTopY = to.Y - _options.NodeHeight / 2;
                labelX = start.X + (end.X - start.X) * t;
                labelY = sourceBottomY + (targetTopY - sourceBottomY) * t;
            }
        }
        else
        {
            labelX = Math.Max(from.X, to.X) + _options.NodeWidth / 2 + 40;
            labelY = (from.Y + to.Y) / 2;
        }

        Canvas.SetLeft(state.Label, labelX - state.Label.DesiredSize.Width / 2);
        Canvas.SetTop(state.Label, labelY - state.Label.DesiredSize.Height / 2);
    }

    private static Brush Brush(string hex)
        => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    /// <summary>
    /// Assigns each edge an anchor fraction (0..1) along its keyed node's side, ordered by the
    /// opposite endpoint's X. A node with two branches (e.g. a condition's true/false) thus
    /// exits at 1/3 and 2/3 of its width instead of both edges stacking at bottom-center.
    /// </summary>
    private static Dictionary<RoutedEdge, double> AnchorFractions(
        GraphLayoutResult layout,
        IEnumerable<RoutedEdge> edges,
        Func<RoutedEdge, string> keyOf,
        Func<RoutedEdge, string> otherOf,
        bool orderByY)
    {
        var result = new Dictionary<RoutedEdge, double>();

        foreach (var group in edges.GroupBy(keyOf))
        {
            var ordered = group
                .OrderBy(e => orderByY ? layout.Nodes[otherOf(e)].Y : layout.Nodes[otherOf(e)].X)
                .ToList();
            for (var i = 0; i < ordered.Count; i++)
                result[ordered[i]] = (i + 1.0) / (ordered.Count + 1);
        }

        return result;
    }

    /// <summary>Point on the node's bottom silhouette at the given width fraction, honoring the shape.</summary>
    private Point BottomAnchor(Point center, ENodeShape shape, double fraction)
    {
        var x = center.X - _options.NodeWidth / 2 + fraction * _options.NodeWidth;
        var y = center.Y + _options.NodeHeight / 2;

        if (shape == ENodeShape.Diamond)
        {
            // Follow the diamond's lower edges so the edge leaves the silhouette
            var t = 1 - Math.Min(1, Math.Abs(x - center.X) / (_options.NodeWidth / 2));
            y = center.Y + t * _options.NodeHeight / 2;
        }
        else if (shape == ENodeShape.Hexagon)
        {
            x = Math.Clamp(x, center.X - _options.NodeWidth / 2 + HexagonInset, center.X + _options.NodeWidth / 2 - HexagonInset);
        }

        return new Point(x, y);
    }

    /// <summary>Point on the node's top silhouette at the given width fraction, honoring the shape.</summary>
    private Point TopAnchor(Point center, ENodeShape shape, double fraction)
    {
        var x = center.X - _options.NodeWidth / 2 + fraction * _options.NodeWidth;
        var y = center.Y - _options.NodeHeight / 2;

        if (shape == ENodeShape.Diamond)
        {
            var t = 1 - Math.Min(1, Math.Abs(x - center.X) / (_options.NodeWidth / 2));
            y = center.Y - t * _options.NodeHeight / 2;
        }
        else if (shape == ENodeShape.Hexagon)
        {
            x = Math.Clamp(x, center.X - _options.NodeWidth / 2 + HexagonInset, center.X + _options.NodeWidth / 2 - HexagonInset);
        }

        return new Point(x, y);
    }

    /// <summary>Point on the node's right silhouette at the given height fraction, honoring the shape.</summary>
    private Point RightAnchor(Point center, ENodeShape shape, double fraction)
    {
        var y = center.Y - _options.NodeHeight / 2 + fraction * _options.NodeHeight;
        var x = center.X + _options.NodeWidth / 2;

        if (shape == ENodeShape.Diamond || shape == ENodeShape.Hexagon)
        {
            // Follow the slanted silhouette toward the right vertex
            var t = 1 - Math.Min(1, Math.Abs(y - center.Y) / (_options.NodeHeight / 2));
            x = center.X + _options.NodeWidth / 2 - (shape == ENodeShape.Hexagon ? HexagonInset * (1 - t) : _options.NodeWidth / 2 * (1 - t));
        }

        return new Point(x, y);
    }

    // ── Input forwarding ────────────────────────────────────────────

    private void ViewportSizeChanged(object sender, SizeChangedEventArgs e)
        => _viewModel.SetViewportSize(Viewport.ActualWidth, Viewport.ActualHeight);

    private void ViewportMouseWheel(object sender, MouseWheelEventArgs e)
        => _viewModel.ZoomAt(e.GetPosition(Viewport), e.Delta > 0 ? 1.1 : 1 / 1.1);

    private void ViewportMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var canvasPos = e.GetPosition(GraphCanvas);
        var hitNodeId = FindNodeAt(canvasPos);
        if (hitNodeId is not null)
        {
            // Drag the node itself so a wrongly placed node can be moved by hand
            _dragNodeId = hitNodeId;
            _dragStart = canvasPos;
            _dragOriginalCenter = _positions[hitNodeId];
        }
        else
        {
            _viewModel.BeginPan(e.GetPosition(Viewport));
        }

        Viewport.CaptureMouse();
    }

    private void ViewportMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragNodeId = null;
        _viewModel.EndPan();
        Viewport.ReleaseMouseCapture();
    }

    private void ViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragNodeId is not null)
        {
            var canvasPos = e.GetPosition(GraphCanvas);
            ApplyNodePosition(_dragNodeId, new Point(
                _dragOriginalCenter.X + canvasPos.X - _dragStart.X,
                _dragOriginalCenter.Y + canvasPos.Y - _dragStart.Y));

            foreach (var state in _edgeStates)
            {
                if (state.Edge.From == _dragNodeId || state.Edge.To == _dragNodeId)
                    UpdateEdgeGeometry(state);
            }
        }
        else
        {
            _viewModel.UpdatePan(e.GetPosition(Viewport));
        }
    }

    /// <summary>First node whose bounding box contains the given canvas-space point.</summary>
    private string? FindNodeAt(Point canvasPos)
    {
        foreach (var (id, center) in _positions)
        {
            if (Math.Abs(canvasPos.X - center.X) <= _options.NodeWidth / 2
                && Math.Abs(canvasPos.Y - center.Y) <= _options.NodeHeight / 2)
                return id;
        }

        return null;
    }

    private void ApplyNodePosition(string nodeId, Point center)
    {
        _positions[nodeId] = center;
        if (!_nodeElements.TryGetValue(nodeId, out var elements))
            return;

        foreach (var (element, offsetX, offsetY) in elements)
        {
            Canvas.SetLeft(element, center.X + offsetX);
            Canvas.SetTop(element, center.Y + offsetY);
        }
    }
}
