namespace SqlConstraintHelper.Core.Models
{
    /// <summary>
    /// Represents a database table with its schema information
    /// </summary>
    public class TableInfo
    {
        public string SchemaName { get; set; } = "dbo";
        public string TableName { get; set; } = string.Empty;
        public string FullName => $"{SchemaName}.{TableName}";
        public List<ColumnInfo> Columns { get; set; } = new();
        public List<ConstraintInfo> Constraints { get; set; } = new();
        public long RowCounts { get; set; }
    }

    /// <summary>
    /// Represents a column in a table
    /// </summary>
    public class ColumnInfo
    {
        public string ColumnName { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public long? MaxLength { get; set; }
        public bool IsNullable { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsForeignKey { get; set; }
        public string? DefaultValue { get; set; }
    }

    /// <summary>
    /// Base constraint information
    /// </summary>
    public class ConstraintInfo
    {
        public string ConstraintName { get; set; } = string.Empty;
        public ConstraintType Type { get; set; }
        public string TableSchema { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public DateTime CreatedDate { get; set; }
    }

    /// <summary>
    /// Foreign Key constraint details
    /// </summary>
    public class ForeignKeyInfo : ConstraintInfo
    {
        public string ColumnName { get; set; } = string.Empty;
        public string ReferencedSchema { get; set; } = string.Empty;
        public string ReferencedTable { get; set; } = string.Empty;
        public string ReferencedColumn { get; set; } = string.Empty;
        public string DeleteAction { get; set; } = "NO ACTION";
        public string UpdateAction { get; set; } = "NO ACTION";

        public string ReferencedFullName => $"{ReferencedSchema}.{ReferencedTable}";
    }

    /// <summary>
    /// Primary Key constraint details
    /// </summary>
    public class PrimaryKeyInfo : ConstraintInfo
    {
        public List<string> Columns { get; set; } = new();
        public bool IsClustered { get; set; }
    }

    /// <summary>
    /// Unique constraint details
    /// </summary>
    public class UniqueConstraintInfo : ConstraintInfo
    {
        public List<string> Columns { get; set; } = new();
    }

    /// <summary>
    /// Check constraint details
    /// </summary>
    public class CheckConstraintInfo : ConstraintInfo
    {
        public string Definition { get; set; } = string.Empty;
    }

    /// <summary>
    /// Types of database constraints
    /// </summary>
    public enum ConstraintType
    {
        PrimaryKey,
        ForeignKey,
        Unique,
        Check,
        Default
    }

    /// <summary>
    /// Represents a constraint violation or issue
    /// </summary>
    public class ConstraintIssue
    {
        public string ConstraintName { get; set; } = string.Empty;
        public ConstraintType Type { get; set; }
        public string TableName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public IssueSeverity Severity { get; set; }
        public string? SuggestedFix { get; set; }
        public int AffectedRows { get; set; }
    }

    /// <summary>
    /// Severity levels for constraint issues
    /// </summary>
    public enum IssueSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    /// <summary>
    /// Database connection information
    /// </summary>
    public class ConnectionInfo
    {
        public string Name { get; set; } = "New Connection";
        public string Server { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public bool UseIntegratedSecurity { get; set; } = true;
        public string? Username { get; set; }
        public string? Password { get; set; }
        public int Timeout { get; set; } = 30;

        public string GetConnectionString()
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
            {
                DataSource = Server,
                InitialCatalog = Database,
                ConnectTimeout = Timeout,
                IntegratedSecurity = UseIntegratedSecurity,
                TrustServerCertificate = true,
                MultipleActiveResultSets = true,
            };

            if (!UseIntegratedSecurity)
            {
                builder.UserID = Username;
                builder.Password = Password;
            }

            return builder.ConnectionString;
        }
    }
}