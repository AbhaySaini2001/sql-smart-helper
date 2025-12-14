using SqlConstraintHelper.Core.Models;
using System.Text;

namespace SqlConstraintHelper.Core.Services
{
    public interface IQueryBuilderService
    {
        Task<string> GenerateSqlAsync(QueryDefinition query);
        Task<List<JoinPath>> FindJoinPathsAsync(string fromTable, string toTable, List<ForeignKeyInfo> foreignKeys);
        Task<QueryJoin> SuggestJoinAsync(QueryTable leftTable, QueryTable rightTable, List<ForeignKeyInfo> foreignKeys);
        Task<List<QuerySuggestion>> AnalyzeQueryAsync(QueryDefinition query, List<ForeignKeyInfo> foreignKeys);
        string FormatSql(string sql);
    }

    public class QueryBuilderService : IQueryBuilderService
    {
        public async Task<string> GenerateSqlAsync(QueryDefinition query)
        {
            await Task.CompletedTask;

            if (!query.Tables.Any())
                return "-- No tables selected";

            var sb = new StringBuilder();

            // SELECT clause
            sb.Append("SELECT ");
            if (query.IsDistinct)
                sb.Append("DISTINCT ");
            if (query.TopCount.HasValue)
                sb.Append($"TOP {query.TopCount.Value} ");

            var selectedColumns = new List<string>();
            foreach (var table in query.Tables)
            {
                var tableAlias = string.IsNullOrEmpty(table.Alias) ? table.TableName : table.Alias;

                foreach (var column in table.Columns.Where(c => c.IsSelected))
                {
                    var columnExpr = $"{tableAlias}.{column.ColumnName}";

                    if (!string.IsNullOrEmpty(column.AggregateFunction))
                    {
                        columnExpr = $"{column.AggregateFunction}({columnExpr})";
                    }

                    selectedColumns.Add(columnExpr);
                }
            }

            if (!selectedColumns.Any())
                selectedColumns.Add("*");

            sb.AppendLine(string.Join(",\n    ", selectedColumns));

            // FROM clause
            var baseTable = query.Tables.FirstOrDefault(t => t.IsBaseTable) ?? query.Tables.First();
            sb.AppendLine($"FROM {baseTable.FullName}");
            if (!string.IsNullOrEmpty(baseTable.Alias))
                sb.Append($" AS {baseTable.Alias}");

            // JOIN clauses
            foreach (var join in query.Joins)
            {
                var leftTable = query.Tables.FirstOrDefault(t => t.Id == join.LeftTableId);
                var rightTable = query.Tables.FirstOrDefault(t => t.Id == join.RightTableId);

                if (leftTable == null || rightTable == null) continue;

                var joinTypeStr = join.JoinType switch
                {
                    JoinType.Inner => "INNER JOIN",
                    JoinType.LeftOuter => "LEFT OUTER JOIN",
                    JoinType.RightOuter => "RIGHT OUTER JOIN",
                    JoinType.FullOuter => "FULL OUTER JOIN",
                    JoinType.Cross => "CROSS JOIN",
                    _ => "INNER JOIN"
                };

                var rightAlias = string.IsNullOrEmpty(rightTable.Alias) ? rightTable.TableName : rightTable.Alias;
                sb.AppendLine($"{joinTypeStr} {rightTable.FullName}");
                if (!string.IsNullOrEmpty(rightTable.Alias))
                    sb.Append($" AS {rightTable.Alias}");

                if (join.JoinType != JoinType.Cross && join.Conditions.Any())
                {
                    sb.Append(" ON ");
                    var conditions = join.Conditions.Select(c =>
                    {
                        var leftAlias = string.IsNullOrEmpty(leftTable.Alias) ? leftTable.TableName : leftTable.Alias;
                        return $"{leftAlias}.{c.LeftColumn} {c.Operator} {rightAlias}.{c.RightColumn}";
                    });
                    sb.AppendLine(string.Join("\n    AND ", conditions));
                }
            }

            // WHERE clause
            if (query.WhereConditions.Any())
            {
                sb.AppendLine("WHERE");
                var whereList = new List<string>();

                foreach (var condition in query.WhereConditions)
                {
                    var table = query.Tables.FirstOrDefault(t => t.Id == condition.TableId);
                    if (table == null) continue;

                    var tableAlias = string.IsNullOrEmpty(table.Alias) ? table.TableName : table.Alias;
                    var conditionStr = $"{tableAlias}.{condition.ColumnName} {condition.Operator} {condition.Value}";

                    if (whereList.Any())
                        conditionStr = $"    {condition.LogicalOperator} {conditionStr}";

                    whereList.Add(conditionStr);
                }

                sb.AppendLine(string.Join("\n", whereList));
            }

            // GROUP BY clause
            if (query.GroupByColumns.Any())
            {
                sb.AppendLine("GROUP BY");
                sb.AppendLine("    " + string.Join(", ", query.GroupByColumns));
            }

            // HAVING clause
            if (!string.IsNullOrEmpty(query.HavingClause))
            {
                sb.AppendLine($"HAVING {query.HavingClause}");
            }

            // ORDER BY clause
            var orderByColumns = query.Tables
                .SelectMany(t => t.Columns.Where(c => c.IsSelected && c.SortPriority.HasValue)
                    .Select(c => new
                    {
                        TableAlias = string.IsNullOrEmpty(t.Alias) ? t.TableName : t.Alias,
                        c.ColumnName,
                        c.SortOrder,
                        c.SortPriority
                    }))
                .OrderBy(c => c.SortPriority)
                .ToList();

            if (orderByColumns.Any())
            {
                sb.AppendLine("ORDER BY");
                var orderByClauses = orderByColumns.Select(c =>
                    $"    {c.TableAlias}.{c.ColumnName} {c.SortOrder ?? "ASC"}");
                sb.AppendLine(string.Join(",\n", orderByClauses));
            }

            return sb.ToString().TrimEnd();
        }

        public async Task<List<JoinPath>> FindJoinPathsAsync(string fromTable, string toTable, List<ForeignKeyInfo> foreignKeys)
        {
            await Task.CompletedTask;

            var paths = new List<JoinPath>();
            var visited = new HashSet<string>();
            var currentPath = new List<string> { fromTable };
            var currentFKs = new List<ForeignKeyInfo>();

            FindPathsRecursive(fromTable, toTable, foreignKeys, visited, currentPath, currentFKs, paths, 0, 3);

            return paths.OrderBy(p => p.Distance).ToList();
        }

        private void FindPathsRecursive(string current, string target, List<ForeignKeyInfo> foreignKeys,
            HashSet<string> visited, List<string> path, List<ForeignKeyInfo> fks, List<JoinPath> results,
            int depth, int maxDepth)
        {
            if (depth > maxDepth) return;

            if (current.Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new JoinPath
                {
                    Tables = new List<string>(path),
                    ForeignKeys = new List<ForeignKeyInfo>(fks),
                    Distance = path.Count - 1,
                    IsDirect = path.Count == 2
                });
                return;
            }

            visited.Add(current);

            // Find outgoing foreign keys
            var outgoing = foreignKeys.Where(fk =>
                $"{fk.TableSchema}.{fk.TableName}".Equals(current, StringComparison.OrdinalIgnoreCase));

            foreach (var fk in outgoing)
            {
                var next = $"{fk.ReferencedSchema}.{fk.ReferencedTable}";
                if (!visited.Contains(next))
                {
                    path.Add(next);
                    fks.Add(fk);
                    FindPathsRecursive(next, target, foreignKeys, visited, path, fks, results, depth + 1, maxDepth);
                    path.RemoveAt(path.Count - 1);
                    fks.RemoveAt(fks.Count - 1);
                }
            }

            // Find incoming foreign keys
            var incoming = foreignKeys.Where(fk =>
                $"{fk.ReferencedSchema}.{fk.ReferencedTable}".Equals(current, StringComparison.OrdinalIgnoreCase));

            foreach (var fk in incoming)
            {
                var next = $"{fk.TableSchema}.{fk.TableName}";
                if (!visited.Contains(next))
                {
                    path.Add(next);
                    fks.Add(fk);
                    FindPathsRecursive(next, target, foreignKeys, visited, path, fks, results, depth + 1, maxDepth);
                    path.RemoveAt(path.Count - 1);
                    fks.RemoveAt(fks.Count - 1);
                }
            }

            visited.Remove(current);
        }

        public async Task<QueryJoin> SuggestJoinAsync(QueryTable leftTable, QueryTable rightTable, List<ForeignKeyInfo> foreignKeys)
        {
            await Task.CompletedTask;

            var leftFullName = leftTable.FullName;
            var rightFullName = rightTable.FullName;

            // Try to find direct FK relationship
            var directFK = foreignKeys.FirstOrDefault(fk =>
                ($"{fk.TableSchema}.{fk.TableName}".Equals(leftFullName, StringComparison.OrdinalIgnoreCase) &&
                 $"{fk.ReferencedSchema}.{fk.ReferencedTable}".Equals(rightFullName, StringComparison.OrdinalIgnoreCase)) ||
                ($"{fk.TableSchema}.{fk.TableName}".Equals(rightFullName, StringComparison.OrdinalIgnoreCase) &&
                 $"{fk.ReferencedSchema}.{fk.ReferencedTable}".Equals(leftFullName, StringComparison.OrdinalIgnoreCase)));

            if (directFK != null)
            {
                var isLeftToRight = $"{directFK.TableSchema}.{directFK.TableName}".Equals(leftFullName, StringComparison.OrdinalIgnoreCase);

                return new QueryJoin
                {
                    LeftTableId = leftTable.Id,
                    RightTableId = rightTable.Id,
                    JoinType = JoinType.Inner,
                    IsAutoGenerated = true,
                    ConstraintName = directFK.ConstraintName,
                    Conditions = new List<JoinCondition>
                    {
                        new JoinCondition
                        {
                            LeftColumn = isLeftToRight ? directFK.ColumnName : directFK.ReferencedColumn,
                            RightColumn = isLeftToRight ? directFK.ReferencedColumn : directFK.ColumnName,
                            Operator = "="
                        }
                    }
                };
            }

            // No direct relationship found
            return new QueryJoin
            {
                LeftTableId = leftTable.Id,
                RightTableId = rightTable.Id,
                JoinType = JoinType.Inner,
                IsAutoGenerated = false,
                Conditions = new List<JoinCondition>()
            };
        }

        public async Task<List<QuerySuggestion>> AnalyzeQueryAsync(QueryDefinition query, List<ForeignKeyInfo> foreignKeys)
        {
            await Task.CompletedTask;

            var suggestions = new List<QuerySuggestion>();

            // Check for SELECT *
            if (query.Tables.Any(t => !t.Columns.Any(c => c.IsSelected)))
            {
                suggestions.Add(new QuerySuggestion
                {
                    Type = SuggestionType.SelectStar,
                    Title = "Avoid SELECT *",
                    Description = "Select only the columns you need instead of using SELECT * for better performance.",
                    Severity = SuggestionSeverity.Warning,
                    SqlExample = "SELECT Column1, Column2 FROM Table"
                });
            }

            // Check for missing WHERE clause on large tables
            if (!query.WhereConditions.Any() && query.Tables.Any(t => t.Columns.Any()))
            {
                suggestions.Add(new QuerySuggestion
                {
                    Type = SuggestionType.MissingWhereClause,
                    Title = "Consider adding WHERE clause",
                    Description = "Query without WHERE clause will return all rows. Consider adding filters.",
                    Severity = SuggestionSeverity.Info,
                    SqlExample = "WHERE Column = 'Value'"
                });
            }

            // Check for Cartesian product (tables without joins)
            if (query.Tables.Count > 1 && query.Joins.Count < query.Tables.Count - 1)
            {
                suggestions.Add(new QuerySuggestion
                {
                    Type = SuggestionType.CartesianProduct,
                    Title = "Possible Cartesian Product",
                    Description = $"You have {query.Tables.Count} tables but only {query.Joins.Count} joins. This may cause a Cartesian product.",
                    Severity = SuggestionSeverity.Error,
                    SqlExample = "Ensure all tables are properly joined"
                });
            }

            // Check for missing indexes on JOIN columns
            foreach (var join in query.Joins)
            {
                if (join.Conditions.Any())
                {
                    suggestions.Add(new QuerySuggestion
                    {
                        Type = SuggestionType.MissingIndex,
                        Title = "Verify indexes on JOIN columns",
                        Description = "Ensure indexes exist on columns used in JOIN conditions for optimal performance.",
                        Severity = SuggestionSeverity.Performance,
                        CanAutoFix = false
                    });
                }
            }

            // Suggest using EXISTS instead of IN for subqueries
            if (query.WhereConditions.Any(w => w.Operator.Equals("IN", StringComparison.OrdinalIgnoreCase)))
            {
                suggestions.Add(new QuerySuggestion
                {
                    Type = SuggestionType.SuboptimalJoinOrder,
                    Title = "Consider using EXISTS",
                    Description = "For subqueries, EXISTS can be more efficient than IN in many cases.",
                    Severity = SuggestionSeverity.Performance,
                    SqlExample = "WHERE EXISTS (SELECT 1 FROM Table WHERE ...)"
                });
            }

            return suggestions;
        }

        public string FormatSql(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
                return string.Empty;

            // Simple SQL formatting
            var keywords = new[] { "SELECT", "FROM", "WHERE", "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "OUTER",
                                   "ON", "AND", "OR", "GROUP BY", "HAVING", "ORDER BY" };

            var formatted = sql;
            foreach (var keyword in keywords)
            {
                formatted = formatted.Replace(keyword, $"\n{keyword}", StringComparison.OrdinalIgnoreCase);
            }

            return formatted.Trim();
        }
    }
}