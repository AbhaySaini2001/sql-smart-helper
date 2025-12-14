using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SqlConstraintHelper.Core.Models;
using SqlConstraintHelper.Core.Services;
using SqlConstraintHelper.Desktop.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace SqlConstraintHelper.Desktop.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private IDatabaseService? _dbService;
        private readonly ISettingsService _settingsService;
        private readonly IThemeManager _themeManager;

        [ObservableProperty]
        private ConnectionInfo _currentConnection = new();

        [ObservableProperty]
        private ObservableCollection<ConnectionProfile> _savedProfiles = new();

        [ObservableProperty]
        private ConnectionProfile? _selectedProfile;

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

        [ObservableProperty]
        private bool _isDarkTheme;

        [ObservableProperty]
        private ObservableCollection<QueryHistoryItem> _queryHistory = new();

        [ObservableProperty]
        private GraphViewModel _graphViewModel;

        [ObservableProperty]
        private QueryBuilderViewModel _queryBuilderViewModel;

        public MainViewModel(ISettingsService settingsService, IThemeManager themeManager)
        {
            _settingsService = settingsService;
            _themeManager = themeManager;
            _graphViewModel = new GraphViewModel();
            _queryBuilderViewModel = new QueryBuilderViewModel();

            // Initialize
            _ = InitializeAsync();
        }

        public MainViewModel() : this(
            new SettingsService(),
            new ThemeManager())
        {
        }

        private async Task InitializeAsync()
        {
            try
            {
                // Load settings
                var settings = await _settingsService.LoadSettingsAsync();
                IsDarkTheme = settings.CurrentTheme == Theme.Dark;

                // Apply theme
                _themeManager.ApplyTheme(settings.CurrentTheme);

                // Load profiles
                await LoadProfilesAsync();

                // Load last used connection if enabled
                if (settings.RememberLastConnection && settings.LastUsedProfileId.HasValue)
                {
                    var profile = await _settingsService.GetProfileAsync(settings.LastUsedProfileId.Value);
                    if (profile != null)
                    {
                        SelectedProfile = profile;
                        await LoadProfileCommand.ExecuteAsync(profile);
                    }
                }

                // Load query history
                await LoadQueryHistoryAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Initialization error: {ex.Message}";
            }
        }

        private async Task LoadProfilesAsync()
        {
            var profiles = await _settingsService.GetProfilesAsync();
            SavedProfiles.Clear();
            foreach (var profile in profiles.OrderByDescending(p => p.IsFavorite).ThenByDescending(p => p.LastUsedDate))
            {
                SavedProfiles.Add(profile);
            }
        }

        private async Task LoadQueryHistoryAsync()
        {
            var history = await _settingsService.GetQueryHistoryAsync(50);
            QueryHistory.Clear();
            foreach (var item in history)
            {
                QueryHistory.Add(item);
            }
        }

        [RelayCommand]
        private async Task LoadProfileAsync(ConnectionProfile? profile)
        {
            if (profile == null) return;

            CurrentConnection = profile.ToConnectionInfo();
            StatusMessage = $"Loaded profile: {profile.Name}";
        }

        [RelayCommand]
        private async Task SaveCurrentProfileAsync()
        {
            try
            {
                var profile = ConnectionProfile.FromConnectionInfo(CurrentConnection);

                var result = System.Windows.MessageBox.Show(
                    $"Save current connection as '{profile.Name}'?",
                    "Save Profile",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    await _settingsService.SaveProfileAsync(profile);
                    await LoadProfilesAsync();
                    StatusMessage = "Profile saved successfully";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving profile: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task DeleteProfileAsync(ConnectionProfile? profile)
        {
            if (profile == null) return;

            var result = System.Windows.MessageBox.Show(
                $"Delete profile '{profile.Name}'?",
                "Confirm Delete",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                await _settingsService.DeleteProfileAsync(profile.Id);
                await LoadProfilesAsync();
                StatusMessage = $"Profile '{profile.Name}' deleted";
            }
        }

        [RelayCommand]
        private void ToggleTheme()
        {
            IsDarkTheme = !IsDarkTheme;
            var newTheme = IsDarkTheme ? Theme.Dark : Theme.Light;
            _themeManager.ApplyTheme(newTheme);

            // Save preference
            _ = Task.Run(async () =>
            {
                var settings = await _settingsService.LoadSettingsAsync();
                settings.CurrentTheme = newTheme;
                await _settingsService.SaveSettingsAsync(settings);
            });

            StatusMessage = $"Theme switched to {(IsDarkTheme ? "Dark" : "Light")}";
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

                // Build graph visualization
                StatusMessage = "Building schema graph...";
                await GraphViewModel.BuildGraphAsync(tables, fks.ToList());

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
                var startTime = System.Diagnostics.Stopwatch.StartNew();
                GeneratedQuery = await _dbService.GenerateQueryAsync(type, SelectedTable);
                startTime.Stop();

                // Save to history
                var historyItem = new QueryHistoryItem
                {
                    QueryText = GeneratedQuery,
                    QueryType = type,
                    DatabaseName = CurrentConnection.Database,
                    TableName = SelectedTable.TableName,
                    ExecutionTimeMs = startTime.ElapsedMilliseconds,
                    WasSuccessful = true
                };

                await _settingsService.SaveQueryHistoryAsync(historyItem);
                await LoadQueryHistoryAsync();
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
            //_dbService?.Dispose();
            _dbService = null;
            IsConnected = false;
            Tables.Clear();
            ForeignKeys.Clear();
            Issues.Clear();
            GeneratedQuery = string.Empty;
            StatusMessage = "Disconnected";
        }

        [RelayCommand]
        private async Task ExportQueryAsync()
        {
            if (string.IsNullOrWhiteSpace(GeneratedQuery))
            {
                MessageBox.Show("No query to export.", "Nothing to Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "SQL Files (*.sql)|*.sql|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                    DefaultExt = ".sql",
                    FileName = $"{SelectedTable?.TableName ?? "Query"}_{DateTime.Now:yyyyMMdd_HHmmss}.sql"
                };

                if (dialog.ShowDialog() == true)
                {
                    var content = $"-- Generated by SQL Constraint Helper\n" +
                                 $"-- Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                                 $"-- Database: {CurrentConnection.Database}\n" +
                                 $"-- Table: {SelectedTable?.FullName ?? "N/A"}\n\n" +
                                 GeneratedQuery;

                    await File.WriteAllTextAsync(dialog.FileName, content);
                    StatusMessage = $"Query exported to {dialog.FileName}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting query: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void LoadHistoryQuery(QueryHistoryItem? item)
        {
            if (item != null)
            {
                GeneratedQuery = item.QueryText;
                StatusMessage = $"Loaded query from history (executed {item.ExecutedDate:g})";
            }
        }
    }
}