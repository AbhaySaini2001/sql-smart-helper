using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SqlConstraintHelper.Core.Models;
using SqlConstraintHelper.Core.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace SqlConstraintHelper.Desktop.ViewModels
{
    public partial class QueryBuilderViewModel : ObservableObject
    {
        private readonly IQueryBuilderService _queryBuilderService;
        private List<TableInfo> _availableTables = new();
        private List<ForeignKeyInfo> _foreignKeys = new();

        [ObservableProperty]
        private QueryDefinition _currentQuery = new();

        [ObservableProperty]
        private string _generatedSql = string.Empty;

        [ObservableProperty]
        private ObservableCollection<TableInfo> _availableTablesList = new();

        [ObservableProperty]
        private TableInfo? _selectedAvailableTable;

        [ObservableProperty]
        private ObservableCollection<QueryTable> _selectedTables = new();

        [ObservableProperty]
        private ObservableCollection<QueryJoin> _joins = new();

        [ObservableProperty]
        private ObservableCollection<QuerySuggestion> _suggestions = new();

        [ObservableProperty]
        private QueryTable? _selectedTable;

        [ObservableProperty]
        private QueryJoin? _selectedJoin;

        [ObservableProperty]
        private string _statusMessage = "Ready to build query";

        [ObservableProperty]
        private bool _isLoading;

        public ObservableCollection<JoinType> AvailableJoinTypes { get; } = new()
        {
            JoinType.Inner,
            JoinType.LeftOuter,
            JoinType.RightOuter,
            JoinType.FullOuter
        };

        public QueryBuilderViewModel(IQueryBuilderService queryBuilderService)
        {
            _queryBuilderService = queryBuilderService;
        }

        public QueryBuilderViewModel() : this(new QueryBuilderService())
        {
        }

        public void Initialize(List<TableInfo> tables, List<ForeignKeyInfo> foreignKeys)
        {
            _availableTables = tables;
            _foreignKeys = foreignKeys;

            // IMPORTANT: Clear and repopulate the ObservableCollection
            AvailableTablesList.Clear();

            foreach (var table in tables.OrderBy(t => t.SchemaName).ThenBy(t => t.TableName))
            {
                AvailableTablesList.Add(table);
            }

            StatusMessage = $"{AvailableTablesList.Count} tables available";

            // Debug output
            System.Diagnostics.Debug.WriteLine($"QueryBuilder initialized with {AvailableTablesList.Count} tables");
        }

        [RelayCommand]
        private async Task AddTableAsync(TableInfo? tableInfo)
        {
            if (tableInfo == null) return;

            // Check if table already added
            if (SelectedTables.Any(t => t.SchemaName == tableInfo.SchemaName && t.TableName == tableInfo.TableName))
            {
                MessageBox.Show($"Table {tableInfo.FullName} is already in the query.", "Table Already Added",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var queryTable = new QueryTable
            {
                SchemaName = tableInfo.SchemaName,
                TableName = tableInfo.TableName,
                IsBaseTable = !SelectedTables.Any(),
                Columns = tableInfo.Columns.Select(c => new QueryColumn
                {
                    ColumnName = c.ColumnName,
                    DataType = c.DataType,
                    IsPrimaryKey = c.IsPrimaryKey,
                    IsForeignKey = c.IsForeignKey,
                    IsSelected = false
                }).ToList()
            };

            SelectedTables.Add(queryTable);
            CurrentQuery.Tables.Add(queryTable);

            // Auto-suggest JOIN if there's more than one table
            if (SelectedTables.Count > 1)
            {
                await SuggestJoinsAsync();
            }

            StatusMessage = $"Added {tableInfo.TableName} to query";
            await RegenerateQueryAsync();
        }

        [RelayCommand]
        private async Task RemoveTableAsync(QueryTable? table)
        {
            if (table == null) return;

            var result = MessageBox.Show(
                $"Remove table {table.TableName} from query?",
                "Confirm Remove",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            // Remove related joins
            var relatedJoins = Joins.Where(j => j.LeftTableId == table.Id || j.RightTableId == table.Id).ToList();
            foreach (var join in relatedJoins)
            {
                Joins.Remove(join);
                CurrentQuery.Joins.Remove(join);
            }

            SelectedTables.Remove(table);
            CurrentQuery.Tables.Remove(table);

            // If this was the base table, make the first remaining table the base
            if (table.IsBaseTable && SelectedTables.Any())
            {
                SelectedTables.First().IsBaseTable = true;
            }

            StatusMessage = $"Removed {table.TableName}";
            await RegenerateQueryAsync();
        }

        [RelayCommand]
        private async Task SuggestJoinsAsync()
        {
            if (SelectedTables.Count < 2) return;

            IsLoading = true;
            StatusMessage = "Finding optimal joins...";

            try
            {
                var baseTable = SelectedTables.FirstOrDefault(t => t.IsBaseTable) ?? SelectedTables.First();
                var newJoins = new List<QueryJoin>();

                foreach (var table in SelectedTables.Where(t => t.Id != baseTable.Id))
                {
                    // Check if join already exists
                    if (Joins.Any(j => (j.LeftTableId == baseTable.Id && j.RightTableId == table.Id) ||
                                       (j.LeftTableId == table.Id && j.RightTableId == baseTable.Id)))
                    {
                        continue;
                    }

                    var suggestedJoin = await _queryBuilderService.SuggestJoinAsync(baseTable, table, _foreignKeys);

                    if (suggestedJoin.IsAutoGenerated)
                    {
                        newJoins.Add(suggestedJoin);
                    }
                }

                foreach (var join in newJoins)
                {
                    Joins.Add(join);
                    CurrentQuery.Joins.Add(join);
                }

                StatusMessage = $"Suggested {newJoins.Count} join(s)";
                await RegenerateQueryAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error suggesting joins: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task AddJoinAsync()
        {
            if (SelectedTables.Count < 2)
            {
                MessageBox.Show("Add at least 2 tables before creating joins.", "Not Enough Tables",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Create a manual join with the first two tables
            var leftTable = SelectedTables[0];
            var rightTable = SelectedTables[1];

            var newJoin = new QueryJoin
            {
                LeftTableId = leftTable.Id,
                RightTableId = rightTable.Id,
                JoinType = JoinType.Inner,
                IsAutoGenerated = false,
                Conditions = new System.Collections.Generic.List<JoinCondition>()
            };

            Joins.Add(newJoin);
            CurrentQuery.Joins.Add(newJoin);

            StatusMessage = "Manual join added - configure columns";
            await RegenerateQueryAsync();
        }

        [RelayCommand]
        private async Task RemoveJoinAsync(QueryJoin? join)
        {
            if (join == null) return;

            Joins.Remove(join);
            CurrentQuery.Joins.Remove(join);

            StatusMessage = "Join removed";
            await RegenerateQueryAsync();
        }

        [RelayCommand]
        private async Task FindJoinPathAsync()
        {
            if (SelectedTables.Count < 2)
            {
                MessageBox.Show("Select at least 2 tables to find join paths.", "Not Enough Tables",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsLoading = true;
            StatusMessage = "Finding join paths...";

            try
            {
                var fromTable = SelectedTables.First().FullName;
                var toTable = SelectedTables.Last().FullName;

                var paths = await _queryBuilderService.FindJoinPathsAsync(fromTable, toTable, _foreignKeys);

                if (paths.Any())
                {
                    var message = $"Found {paths.Count} path(s) from {fromTable} to {toTable}:\n\n" +
                                 string.Join("\n\n", paths.Take(5).Select((p, i) =>
                                     $"{i + 1}. {p.GetPathDescription()} (distance: {p.Distance})"));

                    MessageBox.Show(message, "Join Paths Found", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"No direct path found between {fromTable} and {toTable}.",
                        "No Path Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                StatusMessage = $"Found {paths.Count} path(s)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error finding paths: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task ToggleColumnSelectionAsync(QueryColumn? column)
        {
            if (column == null) return;

            column.IsSelected = !column.IsSelected;
            await RegenerateQueryAsync();
        }

        [RelayCommand]
        private async Task SelectAllColumnsAsync(QueryTable? table)
        {
            if (table == null) return;

            foreach (var column in table.Columns)
            {
                column.IsSelected = true;
            }

            StatusMessage = $"Selected all columns from {table.TableName}";
            await RegenerateQueryAsync();
        }

        [RelayCommand]
        private async Task DeselectAllColumnsAsync(QueryTable? table)
        {
            if (table == null) return;

            foreach (var column in table.Columns)
            {
                column.IsSelected = false;
            }

            StatusMessage = $"Deselected all columns from {table.TableName}";
            await RegenerateQueryAsync();
        }

        [RelayCommand]
        private async Task RegenerateQueryAsync()
        {
            IsLoading = true;

            try
            {
                GeneratedSql = await _queryBuilderService.GenerateSqlAsync(CurrentQuery);

                // Analyze query
                var suggestions = await _queryBuilderService.AnalyzeQueryAsync(CurrentQuery, _foreignKeys);
                Suggestions.Clear();
                foreach (var suggestion in suggestions)
                {
                    Suggestions.Add(suggestion);
                }

                StatusMessage = $"Query generated - {Suggestions.Count} suggestion(s)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error generating query: {ex.Message}";
                GeneratedSql = $"-- Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void CopyQuery()
        {
            if (!string.IsNullOrWhiteSpace(GeneratedSql))
            {
                Clipboard.SetText(GeneratedSql);
                StatusMessage = "Query copied to clipboard";
            }
        }

        [RelayCommand]
        private void ClearQuery()
        {
            var result = MessageBox.Show(
                "Clear entire query and start over?",
                "Confirm Clear",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            SelectedTables.Clear();
            Joins.Clear();
            Suggestions.Clear();
            CurrentQuery = new QueryDefinition();
            GeneratedSql = string.Empty;
            StatusMessage = "Query cleared";
        }

        [RelayCommand]
        private void FormatSql()
        {
            if (!string.IsNullOrWhiteSpace(GeneratedSql))
            {
                GeneratedSql = _queryBuilderService.FormatSql(GeneratedSql);
                StatusMessage = "SQL formatted";
            }
        }

        private bool _isTablesPanelOpen = true;
        public bool IsTablesPanelOpen
        {
            get => _isTablesPanelOpen;
            set
            {
                _isTablesPanelOpen = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TablesPanelWidth));
            }
        }

        public GridLength TablesPanelWidth =>
            IsTablesPanelOpen ? new GridLength(280) : new GridLength(0);

        private bool _showSql = true;
        public bool ShowSql
        {
            get => _showSql;
            set
            {
                _showSql = value;
                OnPropertyChanged();
            }
        }

        public ICommand ToggleFocusModeCommand => new RelayCommand(() =>
        {
            IsTablesPanelOpen = !IsTablesPanelOpen;
            ShowSql = !ShowSql;
        });

    }
}