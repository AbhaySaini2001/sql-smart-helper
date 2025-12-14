using SqlConstraintHelper.Core.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SqlConstraintHelper.Desktop.Controls
{
    public class SchemaGraphCanvas : Canvas
    {
        private SchemaGraph? _graph;
        private Point _lastMousePosition;
        private bool _isPanning;
        private double _zoomLevel = 1.0;
        private TranslateTransform _translateTransform = new();
        private ScaleTransform _scaleTransform = new();
        private Dictionary<string, FrameworkElement> _nodeElements = new();
        private List<Line> _edgeElements = new();

        public static readonly DependencyProperty GraphProperty =
            DependencyProperty.Register(nameof(Graph), typeof(SchemaGraph), typeof(SchemaGraphCanvas),
                new PropertyMetadata(null, OnGraphChanged));

        public SchemaGraph? Graph
        {
            get => (SchemaGraph?)GetValue(GraphProperty);
            set => SetValue(GraphProperty, value);
        }

        public event EventHandler<GraphNode>? NodeClicked;
        public event EventHandler<GraphEdge>? EdgeClicked;

        public SchemaGraphCanvas()
        {
            Background = Brushes.White;
            ClipToBounds = true;

            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(_scaleTransform);
            transformGroup.Children.Add(_translateTransform);
            RenderTransform = transformGroup;

            MouseWheel += OnMouseWheel;
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            MouseLeftButtonUp += OnMouseLeftButtonUp;
            MouseMove += OnMouseMove;
        }

        private static void OnGraphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SchemaGraphCanvas canvas)
            {
                canvas._graph = e.NewValue as SchemaGraph;
                canvas.RenderGraph();
            }
        }

        private void RenderGraph()
        {
            if (_graph == null) return;

            Children.Clear();
            _nodeElements.Clear();
            _edgeElements.Clear();

            // Draw edges first (so they appear behind nodes)
            foreach (var edge in _graph.Edges)
            {
                DrawEdge(edge);
            }

            // Draw nodes
            foreach (var node in _graph.Nodes)
            {
                DrawNode(node);
            }
        }

        private void DrawNode(GraphNode node)
        {
            var border = new Border
            {
                Width = node.Width,
                Height = node.Height,
                Background = GetNodeBackground(node),
                BorderBrush = node.IsSelected ? Brushes.Orange : GetNodeBorderBrush(node),
                BorderThickness = new Thickness(node.IsSelected ? 3 : 2),
                CornerRadius = new CornerRadius(8),
                Tag = node,
                Cursor = Cursors.Hand
            };

            var stackPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var titleText = new TextBlock
            {
                Text = node.Label,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(5, 2, 5, 2),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var schemaText = new TextBlock
            {
                Text = node.SchemaName,
                FontSize = 9,
                Foreground = Brushes.White,
                Opacity = 0.8,
                TextAlignment = TextAlignment.Center
            };

            var statsText = new TextBlock
            {
                Text = $"↓{node.IncomingEdges} ↑{node.OutgoingEdges} | {node.RowCount:N0} rows",
                FontSize = 8,
                Foreground = Brushes.White,
                Opacity = 0.7,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };

            stackPanel.Children.Add(titleText);
            stackPanel.Children.Add(schemaText);
            stackPanel.Children.Add(statsText);

            border.Child = stackPanel;

            border.MouseEnter += (s, e) =>
            {
                border.BorderBrush = Brushes.Yellow;
                border.BorderThickness = new Thickness(3);
                HighlightConnectedEdges(node.Id, true);
            };

            border.MouseLeave += (s, e) =>
            {
                if (!node.IsSelected)
                {
                    border.BorderBrush = GetNodeBorderBrush(node);
                    border.BorderThickness = new Thickness(2);
                }
                HighlightConnectedEdges(node.Id, false);
            };

            border.MouseLeftButtonDown += (s, e) =>
            {
                NodeClicked?.Invoke(this, node);
                e.Handled = true;
            };

            SetLeft(border, node.X);
            SetTop(border, node.Y);

            Children.Add(border);
            _nodeElements[node.Id] = border;
        }

        private void DrawEdge(GraphEdge edge)
        {
            var sourceNode = _graph?.Nodes.FirstOrDefault(n => n.Id == edge.SourceNodeId);
            var targetNode = _graph?.Nodes.FirstOrDefault(n => n.Id == edge.TargetNodeId);

            if (sourceNode == null || targetNode == null) return;

            var line = new Line
            {
                X1 = sourceNode.X + sourceNode.Width / 2,
                Y1 = sourceNode.Y + sourceNode.Height / 2,
                X2 = targetNode.X + targetNode.Width / 2,
                Y2 = targetNode.Y + targetNode.Height / 2,
                Stroke = edge.IsEnabled ? Brushes.Gray : Brushes.Red,
                StrokeThickness = edge.Thickness,
                StrokeDashArray = edge.IsEnabled ? null : new DoubleCollection { 5, 3 },
                Tag = edge,
                Opacity = 0.6
            };

            line.MouseEnter += (s, e) =>
            {
                line.Stroke = Brushes.Orange;
                line.StrokeThickness = 3;
                line.Opacity = 1.0;
            };

            line.MouseLeave += (s, e) =>
            {
                line.Stroke = edge.IsEnabled ? Brushes.Gray : Brushes.Red;
                line.StrokeThickness = edge.Thickness;
                line.Opacity = 0.6;
            };

            line.MouseLeftButtonDown += (s, e) =>
            {
                EdgeClicked?.Invoke(this, edge);
                e.Handled = true;
            };

            Children.Add(line);
            _edgeElements.Add(line);

            // Add arrowhead
            DrawArrowHead(line, sourceNode, targetNode);
        }

        private void DrawArrowHead(Line line, GraphNode source, GraphNode target)
        {
            double angle = Math.Atan2(target.Y - source.Y, target.X - source.X);
            double arrowLength = 10;
            double arrowAngle = Math.PI / 6;

            var arrow = new Polygon
            {
                Fill = line.Stroke,
                Points = new PointCollection
                {
                    new Point(line.X2, line.Y2),
                    new Point(
                        line.X2 - arrowLength * Math.Cos(angle - arrowAngle),
                        line.Y2 - arrowLength * Math.Sin(angle - arrowAngle)
                    ),
                    new Point(
                        line.X2 - arrowLength * Math.Cos(angle + arrowAngle),
                        line.Y2 - arrowLength * Math.Sin(angle + arrowAngle)
                    )
                },
                Opacity = 0.6
            };

            Children.Add(arrow);
        }

        private void HighlightConnectedEdges(string nodeId, bool highlight)
        {
            if (_graph == null) return;

            var connectedEdges = _graph.Edges.Where(e =>
                e.SourceNodeId == nodeId || e.TargetNodeId == nodeId).ToList();

            foreach (var edge in connectedEdges)
            {
                var line = _edgeElements.FirstOrDefault(l => (l.Tag as GraphEdge)?.Id == edge.Id);
                if (line != null)
                {
                    line.Stroke = highlight ? Brushes.Yellow : (edge.IsEnabled ? Brushes.Gray : Brushes.Red);
                    line.StrokeThickness = highlight ? 3 : edge.Thickness;
                    line.Opacity = highlight ? 1.0 : 0.6;
                }
            }
        }

        private Brush GetNodeBackground(GraphNode node)
        {
            if (node.IsOrphaned)
                return new SolidColorBrush(Color.FromRgb(158, 158, 158));

            return node.Color switch
            {
                NodeColor.Blue => new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                NodeColor.Green => new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                NodeColor.Orange => new SolidColorBrush(Color.FromRgb(255, 152, 0)),
                NodeColor.Red => new SolidColorBrush(Color.FromRgb(244, 67, 54)),
                NodeColor.Purple => new SolidColorBrush(Color.FromRgb(156, 39, 176)),
                NodeColor.Yellow => new SolidColorBrush(Color.FromRgb(255, 235, 59)),
                _ => new SolidColorBrush(Color.FromRgb(33, 150, 243))
            };
        }

        private Brush GetNodeBorderBrush(GraphNode node)
        {
            return node.IsOrphaned ? Brushes.DarkGray : Brushes.White;
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double zoomFactor = e.Delta > 0 ? 1.1 : 0.9;
            _zoomLevel *= zoomFactor;
            _zoomLevel = Math.Max(0.1, Math.Min(5.0, _zoomLevel));

            _scaleTransform.ScaleX = _zoomLevel;
            _scaleTransform.ScaleY = _zoomLevel;

            e.Handled = true;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == this)
            {
                _isPanning = true;
                _lastMousePosition = e.GetPosition(this);
                CaptureMouse();
                Cursor = Cursors.Hand;
            }
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                ReleaseMouseCapture();
                Cursor = Cursors.Arrow;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                Point currentPosition = e.GetPosition(this);
                Vector delta = currentPosition - _lastMousePosition;

                _translateTransform.X += delta.X;
                _translateTransform.Y += delta.Y;

                _lastMousePosition = currentPosition;
            }
        }

        public void ResetView()
        {
            _zoomLevel = 1.0;
            _scaleTransform.ScaleX = 1.0;
            _scaleTransform.ScaleY = 1.0;
            _translateTransform.X = 0;
            _translateTransform.Y = 0;
        }

        public void ZoomToFit()
        {
            if (_graph == null || !_graph.Nodes.Any()) return;

            double minX = _graph.Nodes.Min(n => n.X);
            double minY = _graph.Nodes.Min(n => n.Y);
            double maxX = _graph.Nodes.Max(n => n.X + n.Width);
            double maxY = _graph.Nodes.Max(n => n.Y + n.Height);

            double contentWidth = maxX - minX;
            double contentHeight = maxY - minY;

            double scaleX = ActualWidth / contentWidth;
            double scaleY = ActualHeight / contentHeight;

            _zoomLevel = Math.Min(scaleX, scaleY) * 0.9;
            _scaleTransform.ScaleX = _zoomLevel;
            _scaleTransform.ScaleY = _zoomLevel;

            _translateTransform.X = (ActualWidth - contentWidth * _zoomLevel) / 2 - minX * _zoomLevel;
            _translateTransform.Y = (ActualHeight - contentHeight * _zoomLevel) / 2 - minY * _zoomLevel;
        }
    }
}