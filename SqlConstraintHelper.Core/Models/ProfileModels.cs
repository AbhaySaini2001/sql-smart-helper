using SqlConstraintHelper.Core.Services;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SqlConstraintHelper.Core.Models
{
    /// <summary>
    /// User application settings and preferences
    /// </summary>
    public class AppSettings
    {
        public Theme CurrentTheme { get; set; } = Theme.Light;
        public List<ConnectionProfile> ConnectionProfiles { get; set; } = new();
        public Guid? LastUsedProfileId { get; set; }
        public bool RememberLastConnection { get; set; } = true;
        public QueryHistorySettings QueryHistory { get; set; } = new();
        public int MaxRecentConnections { get; set; } = 5;
        public bool ShowWelcomeScreen { get; set; } = true;
    }

    /// <summary>
    /// Saved connection profile with secure credential handling
    /// </summary>
    public class ConnectionProfile
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "New Connection";
        public string Server { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public bool UseIntegratedSecurity { get; set; } = true;
        public string? Username { get; set; }

        [JsonIgnore] // Never serialize password to disk
        public string? Password { get; set; }

        public int Timeout { get; set; } = 30;
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime? LastUsedDate { get; set; }
        public string? Description { get; set; }
        public ProfileColor Color { get; set; } = ProfileColor.Blue;
        public bool IsFavorite { get; set; }

        public ConnectionInfo ToConnectionInfo()
        {
            return new ConnectionInfo
            {
                Name = Name,
                Server = Server,
                Database = Database,
                UseIntegratedSecurity = UseIntegratedSecurity,
                Username = Username,
                Password = Password,
                Timeout = Timeout
            };
        }

        public static ConnectionProfile FromConnectionInfo(ConnectionInfo info, string? description = null)
        {
            return new ConnectionProfile
            {
                Name = info.Name,
                Server = info.Server,
                Database = info.Database,
                UseIntegratedSecurity = info.UseIntegratedSecurity,
                Username = info.Username,
                Password = info.Password,
                Timeout = info.Timeout,
                Description = description
            };
        }
    }

    /// <summary>
    /// Query history settings and configuration
    /// </summary>
    public class QueryHistorySettings
    {
        public bool EnableHistory { get; set; } = true;
        public int MaxHistoryItems { get; set; } = 100;
        public bool AutoSaveQueries { get; set; } = true;
        public string HistoryPath { get; set; } = "QueryHistory";
    }

    /// <summary>
    /// Query execution history item
    /// </summary>
    public class QueryHistoryItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string QueryText { get; set; } = string.Empty;
        public QueryType QueryType { get; set; }
        public string DatabaseName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public DateTime ExecutedDate { get; set; } = DateTime.Now;
        public bool WasSuccessful { get; set; } = true;
        public string? ErrorMessage { get; set; }
        public long ExecutionTimeMs { get; set; }
        public bool IsFavorite { get; set; }
        public string? Notes { get; set; }
    }

    /// <summary>
    /// Application themes
    /// </summary>
    public enum Theme
    {
        Light,
        Dark,
        System // Follow OS theme
    }

    /// <summary>
    /// Profile color coding for visual identification
    /// </summary>
    public enum ProfileColor
    {
        Blue,
        Green,
        Red,
        Orange,
        Purple,
        Teal,
        Pink,
        Gray
    }

    /// <summary>
    /// Script export options
    /// </summary>
    public class ExportOptions
    {
        public ExportFormat Format { get; set; } = ExportFormat.SQL;
        public bool IncludeComments { get; set; } = true;
        public bool IncludeTimestamp { get; set; } = true;
        public bool IncludeConnectionInfo { get; set; } = false;
        public string? OutputPath { get; set; }
    }

    public enum ExportFormat
    {
        SQL,
        CSV,
        JSON,
        XML,
        PNG
    }

    /// <summary>
    /// Application activity log entry
    /// </summary>
    public class ActivityLogEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public LogLevel Level { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Details { get; set; }
        public string? ConnectionName { get; set; }
        public string? TableName { get; set; }
        public string? QueryText { get; set; }
    }

    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Critical
    }
}