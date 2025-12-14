using System.Windows;

namespace SqlConstraintHelper.Desktop;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            // Log the inner exception message and stack trace for details
            System.Diagnostics.Debug.WriteLine($"Inner Exception: {ex.InnerException?.Message}");
            // Re-throw the exception to keep the application from running in a bad state
            throw;
        }
    }
}