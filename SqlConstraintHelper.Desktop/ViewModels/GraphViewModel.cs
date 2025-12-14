using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SqlConstraintHelper.Core.Models;
using SqlConstraintHelper.Core.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace SqlConstraintHelper.Desktop.ViewModels
{
    public partial class GraphViewModel(
        IGraphService graphService
        ) : ObservableObject
    {
        private readonly IGraphService _graphService = graphService;

        [ObservableProperty]
        private SchemaGraph? _currentGraph;

        [ObservableProperty]
        private GraphNode? _selectedNode;

        [ObservableProperty]
        private GraphEdge? _selectedEdge;

        [ObservableProperty]
        private string _statusMessage = "No graph loaded";

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private LayoutAlgorithm _selectedLayout = LayoutAlgorithm.Hierarchical;

        [ObservableProperty]
        private bool _showOrphanedOnly;

        [ObservableProperty]
        private string _schemaFilter = string.Empty;

        [ObservableProperty]
        private ObservableCollection<string> _availableSchemas = new();

        [ObservableProperty]
        private GraphStatistics _statistics = new();

        public ObservableCollection<LayoutAlgorithm> AvailableLayouts { get; } = new()
        {
            LayoutAlgorithm.Hierarchical,
            LayoutAlgorithm.Circular,
            LayoutAlgorithm.Force,
            LayoutAlgorithm.Grid
        };

        public GraphViewModel() : this(new GraphService())
        {
        }

        public async Task BuildGraphAsync(System.Collections.Generic.List<TableInfo> tables,
            System.Collections.Generic.List<ForeignKeyInfo> foreignKeys)
        {
            IsLoading = true;
            StatusMessage = "Building graph...";

            try
            {
                var graph = await _graphService.BuildGraphAsync(tables, foreignKeys);

                var layoutOptions = new GraphLayoutOptions
                {
                    Algorithm = SelectedLayout,
                    NodeSpacing = 50,
                    LayerSpacing = 100
                };

                CurrentGraph = await _graphService.ApplyLayoutAsync(graph, layoutOptions);
                Statistics = CurrentGraph.Statistics;

                // Extract unique schemas
                AvailableSchemas.Clear();
                foreach (var schema in CurrentGraph.Nodes.Select(n => n.SchemaName).Distinct().OrderBy(s => s))
                {
                    AvailableSchemas.Add(schema);
                }

                StatusMessage = $"Graph built: {CurrentGraph.Nodes.Count} tables, {CurrentGraph.Edges.Count} relationships";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error building graph: {ex.Message}";
                MessageBox.Show($"Failed to build graph: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task ChangeLayoutAsync()
        {
            if (CurrentGraph == null) return;

            IsLoading = true;
            StatusMessage = $"Applying {SelectedLayout} layout...";

            try
            {
                var layoutOptions = new GraphLayoutOptions
                {
                    Algorithm = SelectedLayout,
                    NodeSpacing = 50,
                    LayerSpacing = 100
                };

                CurrentGraph = await _graphService.ApplyLayoutAsync(CurrentGraph, layoutOptions);
                StatusMessage = $"{SelectedLayout} layout applied";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error applying layout: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task ApplyFilterAsync()
        {
            if (CurrentGraph == null) return;

            IsLoading = true;
            StatusMessage = "Applying filters...";

            try
            {
                var filter = new GraphFilter
                {
                    ShowOrphanedOnly = ShowOrphanedOnly
                };

                if (!string.IsNullOrWhiteSpace(SchemaFilter))
                {
                    filter.IncludeSchemas = [.. SchemaFilter.Split(',')
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))];
                }

                var filteredGraph = await _graphService.ApplyFilterAsync(CurrentGraph, filter);

                // Re-apply layout to filtered graph
                var layoutOptions = new GraphLayoutOptions
                {
                    Algorithm = SelectedLayout,
                    NodeSpacing = 50,
                    LayerSpacing = 100
                };

                CurrentGraph = await _graphService.ApplyLayoutAsync(filteredGraph, layoutOptions);
                StatusMessage = $"Filter applied: {CurrentGraph.Nodes.Count} tables shown";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error applying filter: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void ClearFilter()
        {
            ShowOrphanedOnly = false;
            SchemaFilter = string.Empty;
            StatusMessage = "Filters cleared - rebuild graph to see all tables";
        }

        [RelayCommand]
        private async Task FindRelatedTablesAsync()
        {
            if (SelectedNode == null || CurrentGraph == null)
            {
                MessageBox.Show("Please select a table first.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsLoading = true;
            StatusMessage = $"Finding tables related to {SelectedNode.TableName}...";

            try
            {
                var relatedNodes = _graphService.FindRelatedNodes(CurrentGraph, SelectedNode.Id, 2);

                var message = $"Found {relatedNodes.Count} related tables:\n\n" +
                             string.Join("\n", relatedNodes.Select(n => $"• {n.SchemaName}.{n.TableName}"));

                MessageBox.Show(message, $"Related to {SelectedNode.TableName}",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                StatusMessage = $"Found {relatedNodes.Count} related tables";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error finding related tables: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void DetectCircularReferences()
        {
            if (CurrentGraph == null)
            {
                MessageBox.Show("No graph loaded.", "No Graph",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var cycles = _graphService.DetectCircularReferences(CurrentGraph);

            if (cycles.Any())
            {
                var message = $"Found {cycles.Count} circular reference(s):\n\n" +
                             string.Join("\n\n", cycles.Select((c, i) => $"{i + 1}. {c}"));

                MessageBox.Show(message, "Circular References Detected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show("No circular references detected!", "Clean Schema",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        public void OnNodeClicked(GraphNode node)
        {
            SelectedNode = node;
            StatusMessage = $"Selected: {node.SchemaName}.{node.TableName} " +
                          $"(↓{node.IncomingEdges} ↑{node.OutgoingEdges}, {node.RowCount:N0} rows)";
        }

        public void OnEdgeClicked(GraphEdge edge)
        {
            SelectedEdge = edge;
            StatusMessage = $"Selected: {edge.ConstraintName} " +
                          $"({edge.SourceColumn} → {edge.TargetColumn}) " +
                          $"[{(edge.IsEnabled ? "Enabled" : "Disabled")}]";
        }
    }
}