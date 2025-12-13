using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SqlConstraintHelper.Core.Models;
using SqlConstraintHelper.Core.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace SqlConstraintHelper.Desktop.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private IDatabaseService? _dbService;

        [ObservableProperty]
        private ConnectionInfo _currentConnection = new();

        [ObservableProperty]
        private bool _isConnected;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _statusMessage = "Not connected";

        [ObservableProperty]
        private ObservableCollection<TableInfo> _tables = new();

        [ObservableProperty]
        private ObservableCollection<ForeignKeyInfo> _foreignKeys = new();

        [ObservableProperty]
        private ObservableCollection<ConstraintIssue> _issues = new();

        [ObservableProperty]
        private TableInfo? _selectedTable;

        [ObservableProperty]
        private ForeignKeyInfo? _selectedForeignKey;

        [ObservableProperty]
        private string _generatedQuery = string.Empty;

        [ObservableProperty]
        private int _errorCount;

        [ObservableProperty]
        private int _warningCount;

        public MainViewModel()
        {
            // Initialize with sample connection for demo
            CurrentConnection.Server = "localhost";
            CurrentConnection.Database = "YourDatabase";
        }

        [RelayCommand]
        private async Task ConnectAsync()
        {
            if (string.IsNullOrWhiteSpace(CurrentConnection.Server) ||
                string.IsNullOrWhiteSpace(CurrentConnection.Database))
            {
                MessageBox.Show("Please enter server and database name.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsLoading = true;
            StatusMessage = "Connecting...";

            try
            {
                _dbService = new DatabaseService(CurrentConnection);
                var connected = await _dbService.TestConnectionAsync(CurrentConnection);

                if (connected)
                {
                    IsConnected = true;
                    StatusMessage = $"Connected to {CurrentConnection.Database}";
                    await LoadDataAsync();
                }
                else
                {
                    StatusMessage = "Connection failed";
                    MessageBox.Show("Failed to connect to database. Please check your connection settings.",
                        "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show($"Connection error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task RefreshAsync()
        {
            if (!IsConnected || _dbService == null) return;

            await LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            if (_dbService == null) return;

            IsLoading = true;
            StatusMessage = "Loading database schema...";

            try
            {
                // Load tables
                var tables = await _dbService.GetTablesAsync();
                Tables.Clear();
                foreach (var table in tables)
                {
                    Tables.Add(table);
                }

                StatusMessage = $"Loaded {tables.Count} tables";

                // Load foreign keys
                var fks = await _dbService.GetForeignKeysAsync();
                ForeignKeys.Clear();
                foreach (var fk in fks)
                {
                    ForeignKeys.Add(fk);
                }

                // Analyze constraints
                StatusMessage = "Analyzing constraints...";
                var issues = await _dbService.AnalyzeConstraintsAsync();
                Issues.Clear();
                foreach (var issue in issues)
                {
                    Issues.Add(issue);
                }

                ErrorCount = issues.Count(i => i.Severity == IssueSeverity.Error || i.Severity == IssueSeverity.Critical);
                WarningCount = issues.Count(i => i.Severity == IssueSeverity.Warning);

                StatusMessage = $"Found {ErrorCount} errors, {WarningCount} warnings";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show($"Error loading data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task GenerateSelectQueryAsync()
        {
            await GenerateQueryAsync(QueryType.Select);
        }

        [RelayCommand]
        private async Task GenerateInsertQueryAsync()
        {
            await GenerateQueryAsync(QueryType.Insert);
        }

        [RelayCommand]
        private async Task GenerateUpdateQueryAsync()
        {
            await GenerateQueryAsync(QueryType.Update);
        }

        [RelayCommand]
        private async Task GenerateDeleteQueryAsync()
        {
            await GenerateQueryAsync(QueryType.Delete);
        }

        private async Task GenerateQueryAsync(QueryType type)
        {
            if (SelectedTable == null || _dbService == null)
            {
                MessageBox.Show("Please select a table first.", "No Table Selected",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                GeneratedQuery = await _dbService.GenerateQueryAsync(type, SelectedTable);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating query: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void CopyQuery()
        {
            if (!string.IsNullOrWhiteSpace(GeneratedQuery))
            {
                Clipboard.SetText(GeneratedQuery);
                StatusMessage = "Query copied to clipboard";
            }
        }

        [RelayCommand]
        private async Task ToggleConstraintAsync(ForeignKeyInfo? fk)
        {
            if (fk == null || _dbService == null) return;

            var action = fk.IsEnabled ? "disable" : "enable";
            var result = MessageBox.Show(
                $"Are you sure you want to {action} constraint '{fk.ConstraintName}'?",
                "Confirm Action",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                IsLoading = true;
                var success = await _dbService.ToggleConstraintAsync(fk.ConstraintName, !fk.IsEnabled);

                if (success)
                {
                    fk.IsEnabled = !fk.IsEnabled;
                    StatusMessage = $"Constraint {action}d successfully";
                }
                else
                {
                    MessageBox.Show($"Failed to {action} constraint.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error toggling constraint: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void ViewIssueDetails(ConstraintIssue? issue)
        {
            if (issue == null) return;

            var details = $"Constraint: {issue.ConstraintName}\n" +
                         $"Table: {issue.TableName}\n" +
                         $"Type: {issue.Type}\n" +
                         $"Severity: {issue.Severity}\n" +
                         $"Affected Rows: {issue.AffectedRows}\n\n" +
                         $"Description:\n{issue.Description}\n\n" +
                         $"Suggested Fix:\n{issue.SuggestedFix}";

            MessageBox.Show(details, "Issue Details", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        [RelayCommand]
        private void Disconnect()
        {
           // _dbService?.Dispose();
            _dbService = null;
            IsConnected = false;
            Tables.Clear();
            ForeignKeys.Clear();
            Issues.Clear();
            GeneratedQuery = string.Empty;
            StatusMessage = "Disconnected";
        }
    }
}