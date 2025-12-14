using SqlConstraintHelper.Core.Models;
using SqlConstraintHelper.Desktop.Controls;
using SqlConstraintHelper.Desktop.ViewModels;
using System.Windows;
using System.Windows.Data;

namespace SqlConstraintHelper.Desktop;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Get the ViewModel from DataContext
        if (DataContext is MainViewModel vm)
        {
            // Create the graph canvas
            var canvas = new SchemaGraphCanvas
            {
                Width = 2000,
                Height = 2000
            };

            // Bind the Graph property to ViewModel
            canvas.SetBinding(SchemaGraphCanvas.GraphProperty,
                new Binding("GraphViewModel.CurrentGraph")
                {
                    Source = vm
                });

            // Wire up node click events
            canvas.NodeClicked += (s, node) =>
            {
                vm.GraphViewModel.OnNodeClicked(node);
            };

            // Wire up edge click events
            canvas.EdgeClicked += (s, edge) =>
            {
                vm.GraphViewModel.OnEdgeClicked(edge);
            };

            // Add canvas to the container
            if (GraphCanvasContainer != null)
            {
                GraphCanvasContainer.Child = canvas;
            }
        }
    }

    // Event handler for Zoom Fit button
    private void ZoomFit_Click(object sender, RoutedEventArgs e)
    {
        var canvas = FindGraphCanvas();
        canvas?.ZoomToFit();
    }

    // Event handler for Reset View button
    private void ResetView_Click(object sender, RoutedEventArgs e)
    {
        var canvas = FindGraphCanvas();
        canvas?.ResetView();
    }

    // Helper method to find the canvas in the visual tree
    private SchemaGraphCanvas? FindGraphCanvas()
    {
        if (GraphCanvasContainer?.Child is SchemaGraphCanvas canvas)
        {
            return canvas;
        }
        return null;
    }

    // Event handler for clicking available tables in Query Builder
    private void AvailableTable_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element &&
            element.DataContext is TableInfo tableInfo &&
            DataContext is MainViewModel vm)
        {
            vm.QueryBuilderViewModel.AddTableCommand.Execute(tableInfo);
        }
    }
}