using Microsoft.Data.SqlClient;
using SqlConstraintHelper.Core.Models;
using System.Data;

namespace SqlConstraintHelper.Core.Services
{
    public interface IDatabaseService
    {
        Task<bool> TestConnectionAsync(ConnectionInfo connection);
        Task<List<TableInfo>> GetTablesAsync();
        Task<List<ForeignKeyInfo>> GetForeignKeysAsync();
        Task<List<ConstraintIssue>> AnalyzeConstraintsAsync();
        Task<string> GenerateQueryAsync(QueryType type, TableInfo table);
        Task<bool> ToggleConstraintAsync(string constraintName, bool enable);
    }

    public class DatabaseService(ConnectionInfo connectionInfo) : IDatabaseService, IDisposable
    {
        private SqlConnection? _connection;
        private readonly ConnectionInfo _connectionInfo = connectionInfo;

        private async Task EnsureConnectionAsync()
        {
            _connection ??= new SqlConnection(_connectionInfo.GetConnectionString());

            if (_connection.State != ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }
        }

        private async Task CloseConnectionAsync()
        {
            if (_connection != null && _connection.State != ConnectionState.Closed)
            {
                await _connection.CloseAsync();
            }
        }

        public async Task<bool> TestConnectionAsync(ConnectionInfo connection)
        {
            try
            {
                using var conn = new SqlConnection(connection.GetConnectionString());
                await conn.OpenAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<TableInfo>> GetTablesAsync()
        {
            await EnsureConnectionAsync();
            var tables = new List<TableInfo>();

            const string query = @"
                SELECT 
                    s.name AS SchemaName,
                    t.name AS TableName,
                    p.rows AS RowCounts
                FROM sys.tables t
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                LEFT JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0,1)
                ORDER BY s.name, t.name";

            using var cmd = new SqlCommand(query, _connection);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var table = new TableInfo
                {
                    SchemaName = reader.GetString(0),
                    TableName = reader.GetString(1),
                    RowCounts = reader.IsDBNull(2) ? 0 : reader.GetInt64(2)
                };
                tables.Add(table);
            }

            // Load columns and constraints for each table
            foreach (var table in tables)
            {
                table.Columns = await GetColumnsAsync(table);
                table.Constraints = await GetConstraintsAsync(table);
            }

            return tables;
        }

        private async Task<List<ColumnInfo>> GetColumnsAsync(TableInfo table)
        {
            try
            {
                var columns = new List<ColumnInfo>();

                const string query = @"
                SELECT 
                    c.name AS ColumnName,
                    t.name AS DataType,
                    c.max_length AS MaxLength,
                    c.is_nullable AS IsNullable,
                    CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey,
                    CASE WHEN fk.parent_column_id IS NOT NULL THEN 1 ELSE 0 END AS IsForeignKey,
                    dc.definition AS DefaultValue
                FROM sys.columns c
                INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
                LEFT JOIN (
                    SELECT ic.object_id, ic.column_id
                    FROM sys.index_columns ic
                    INNER JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                    WHERE i.is_primary_key = 1
                ) pk ON c.object_id = pk.object_id AND c.column_id = pk.column_id
                LEFT JOIN sys.foreign_key_columns fk ON c.object_id = fk.parent_object_id AND c.column_id = fk.parent_column_id
                LEFT JOIN sys.default_constraints dc ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
                WHERE c.object_id = OBJECT_ID(@TableFullName)
                ORDER BY c.column_id";

                using var cmd = new SqlCommand(query, _connection);
                cmd.Parameters.AddWithValue("@TableFullName", table.FullName);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    columns.Add(new ColumnInfo
                    {
                        ColumnName = reader.GetString(0),
                        DataType = reader.GetString(1),
                        MaxLength = reader.IsDBNull(2) ? null : reader.GetInt16(2),
                        IsNullable = reader.GetBoolean(3),
                        IsPrimaryKey = (reader.GetInt32(4) == 1),
                        IsForeignKey = (reader.GetInt32(5) == 1),
                        DefaultValue = reader.IsDBNull(6) ? null : reader.GetString(6)
                    });
                }

                return columns;
            }
            catch (Exception ex)
            {

                throw;
            }
        }

        private async Task<List<ConstraintInfo>> GetConstraintsAsync(TableInfo table)
        {
            var constraints = new List<ConstraintInfo>();

            // Get Foreign Keys
            const string fkQuery = @"
                SELECT 
                    fk.name AS ConstraintName,
                    COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS ColumnName,
                    OBJECT_SCHEMA_NAME(fk.referenced_object_id) AS ReferencedSchema,
                    OBJECT_NAME(fk.referenced_object_id) AS ReferencedTable,
                    COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS ReferencedColumn,
                    fk.delete_referential_action_desc AS DeleteAction,
                    fk.update_referential_action_desc AS UpdateAction,
                    fk.is_disabled AS IsDisabled,
                    fk.create_date AS CreatedDate
                FROM sys.foreign_keys fk
                INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
                WHERE fk.parent_object_id = OBJECT_ID(@TableFullName)";

            using var cmd = new SqlCommand(fkQuery, _connection);
            cmd.Parameters.AddWithValue("@TableFullName", table.FullName);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                constraints.Add(new ForeignKeyInfo
                {
                    ConstraintName = reader.GetString(0),
                    Type = ConstraintType.ForeignKey,
                    TableSchema = table.SchemaName,
                    TableName = table.TableName,
                    ColumnName = reader.GetString(1),
                    ReferencedSchema = reader.GetString(2),
                    ReferencedTable = reader.GetString(3),
                    ReferencedColumn = reader.GetString(4),
                    DeleteAction = reader.GetString(5),
                    UpdateAction = reader.GetString(6),
                    IsEnabled = !reader.GetBoolean(7),
                    CreatedDate = reader.GetDateTime(8)
                });
            }

            return constraints;
        }

        public async Task<List<ForeignKeyInfo>> GetForeignKeysAsync()
        {
            await EnsureConnectionAsync();
            var foreignKeys = new List<ForeignKeyInfo>();

            const string query = @"
                SELECT 
                    fk.name AS ConstraintName,
                    OBJECT_SCHEMA_NAME(fk.parent_object_id) AS TableSchema,
                    OBJECT_NAME(fk.parent_object_id) AS TableName,
                    COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS ColumnName,
                    OBJECT_SCHEMA_NAME(fk.referenced_object_id) AS ReferencedSchema,
                    OBJECT_NAME(fk.referenced_object_id) AS ReferencedTable,
                    COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS ReferencedColumn,
                    fk.delete_referential_action_desc AS DeleteAction,
                    fk.update_referential_action_desc AS UpdateAction,
                    fk.is_disabled AS IsDisabled,
                    fk.create_date AS CreatedDate
                FROM sys.foreign_keys fk
                INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
                ORDER BY TableSchema, TableName, fk.name";

            using var cmd = new SqlCommand(query, _connection);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                foreignKeys.Add(new ForeignKeyInfo
                {
                    ConstraintName = reader.GetString(0),
                    Type = ConstraintType.ForeignKey,
                    TableSchema = reader.GetString(1),
                    TableName = reader.GetString(2),
                    ColumnName = reader.GetString(3),
                    ReferencedSchema = reader.GetString(4),
                    ReferencedTable = reader.GetString(5),
                    ReferencedColumn = reader.GetString(6),
                    DeleteAction = reader.GetString(7),
                    UpdateAction = reader.GetString(8),
                    IsEnabled = !reader.GetBoolean(9),
                    CreatedDate = reader.GetDateTime(10)
                });
            }

            return foreignKeys;
        }

        public async Task<List<ConstraintIssue>> AnalyzeConstraintsAsync()
        {
            await EnsureConnectionAsync();
            var issues = new List<ConstraintIssue>();

            // Check for orphaned FK records
            var foreignKeys = await GetForeignKeysAsync();

            foreach (var fk in foreignKeys.Where(f => f.IsEnabled))
            {
                var orphanQuery = $@"
                    SELECT COUNT(*)
                    FROM [{fk.TableSchema}].[{fk.TableName}] t
                    WHERE t.[{fk.ColumnName}] IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1 FROM [{fk.ReferencedSchema}].[{fk.ReferencedTable}] r
                        WHERE r.[{fk.ReferencedColumn}] = t.[{fk.ColumnName}]
                    )";

                using var cmd = new SqlCommand(orphanQuery, _connection);
                var orphanCount = (int)await cmd.ExecuteScalarAsync();

                if (orphanCount > 0)
                {
                    issues.Add(new ConstraintIssue
                    {
                        ConstraintName = fk.ConstraintName,
                        Type = ConstraintType.ForeignKey,
                        TableName = $"{fk.TableSchema}.{fk.TableName}",
                        Description = $"Found {orphanCount} orphaned record(s) in {fk.TableName}.{fk.ColumnName} " +
                                     $"that don't have matching parent records in {fk.ReferencedTable}.{fk.ReferencedColumn}",
                        Severity = IssueSeverity.Error,
                        AffectedRows = orphanCount,
                        SuggestedFix = $"DELETE FROM [{fk.TableSchema}].[{fk.TableName}] WHERE [{fk.ColumnName}] " +
                                      $"NOT IN (SELECT [{fk.ReferencedColumn}] FROM [{fk.ReferencedSchema}].[{fk.ReferencedTable}])"
                    });
                }
            }

            // Check for disabled constraints
            foreach (var fk in foreignKeys.Where(f => !f.IsEnabled))
            {
                issues.Add(new ConstraintIssue
                {
                    ConstraintName = fk.ConstraintName,
                    Type = ConstraintType.ForeignKey,
                    TableName = $"{fk.TableSchema}.{fk.TableName}",
                    Description = $"Foreign key constraint '{fk.ConstraintName}' is currently disabled",
                    Severity = IssueSeverity.Warning,
                    SuggestedFix = $"ALTER TABLE [{fk.TableSchema}].[{fk.TableName}] CHECK CONSTRAINT [{fk.ConstraintName}]"
                });
            }

            return issues;
        }

        public async Task<string> GenerateQueryAsync(QueryType type, TableInfo table)
        {
            await Task.CompletedTask; // Make async

            return type switch
            {
                QueryType.Select => GenerateSelectQuery(table),
                QueryType.Insert => GenerateInsertQuery(table),
                QueryType.Update => GenerateUpdateQuery(table),
                QueryType.Delete => GenerateDeleteQuery(table),
                _ => throw new ArgumentException("Invalid query type")
            };
        }

        private string GenerateSelectQuery(TableInfo table)
        {
            var columns = string.Join(",\n    ", table.Columns.Select(c => $"[{c.ColumnName}]"));
            return $"SELECT\n    {columns}\nFROM [{table.SchemaName}].[{table.TableName}]\nWHERE 1=1;";
        }

        private string GenerateInsertQuery(TableInfo table)
        {
            var columns = table.Columns.Where(c => !c.IsPrimaryKey || c.DefaultValue == null);
            var columnList = string.Join(",\n    ", columns.Select(c => $"[{c.ColumnName}]"));
            var valueList = string.Join(",\n    ", columns.Select(c => $"@{c.ColumnName}"));

            return $"INSERT INTO [{table.SchemaName}].[{table.TableName}] (\n    {columnList}\n)\nVALUES (\n    {valueList}\n);";
        }

        private string GenerateUpdateQuery(TableInfo table)
        {
            var pkColumns = table.Columns.Where(c => c.IsPrimaryKey).ToList();
            var updateColumns = table.Columns.Where(c => !c.IsPrimaryKey);

            var setClause = string.Join(",\n    ", updateColumns.Select(c => $"[{c.ColumnName}] = @{c.ColumnName}"));
            var whereClause = string.Join("\n    AND ", pkColumns.Select(c => $"[{c.ColumnName}] = @{c.ColumnName}"));

            return $"UPDATE [{table.SchemaName}].[{table.TableName}]\nSET\n    {setClause}\nWHERE\n    {whereClause};";
        }

        private string GenerateDeleteQuery(TableInfo table)
        {
            var pkColumns = table.Columns.Where(c => c.IsPrimaryKey).ToList();
            var whereClause = string.Join("\n    AND ", pkColumns.Select(c => $"[{c.ColumnName}] = @{c.ColumnName}"));

            return $"DELETE FROM [{table.SchemaName}].[{table.TableName}]\nWHERE\n    {whereClause};";
        }

        public async Task<bool> ToggleConstraintAsync(string constraintName, bool enable)
        {
            await EnsureConnectionAsync();

            var action = enable ? "CHECK" : "NOCHECK";

            // First, find which table this constraint belongs to
            const string findTableQuery = @"
                SELECT OBJECT_SCHEMA_NAME(parent_object_id), OBJECT_NAME(parent_object_id)
                FROM sys.foreign_keys
                WHERE name = @ConstraintName";

            using var findCmd = new SqlCommand(findTableQuery, _connection);
            findCmd.Parameters.AddWithValue("@ConstraintName", constraintName);

            using var reader = await findCmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return false;

            var schema = reader.GetString(0);
            var table = reader.GetString(1);
            reader.Close();

            // Now toggle the constraint
            var toggleQuery = $"ALTER TABLE [{schema}].[{table}] {action} CONSTRAINT [{constraintName}]";
            using var toggleCmd = new SqlCommand(toggleQuery, _connection);
            await toggleCmd.ExecuteNonQueryAsync();

            return true;
        }

        public void Dispose()
        {
            _connection?.Dispose();
        }
    }

    public enum QueryType
    {
        Select,
        Insert,
        Update,
        Delete
    }
}