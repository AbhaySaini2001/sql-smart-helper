using SqlConstraintHelper.Core.Models;
using System.Text.Json;

namespace SqlConstraintHelper.Core.Services
{
    public interface ISettingsService
    {
        Task<AppSettings> LoadSettingsAsync();
        Task SaveSettingsAsync(AppSettings settings);
        Task<List<ConnectionProfile>> GetProfilesAsync();
        Task<ConnectionProfile?> GetProfileAsync(Guid id);
        Task SaveProfileAsync(ConnectionProfile profile);
        Task DeleteProfileAsync(Guid id);
        Task<List<QueryHistoryItem>> GetQueryHistoryAsync(int maxItems = 100);
        Task SaveQueryHistoryAsync(QueryHistoryItem item);
        Task ClearQueryHistoryAsync();
        Task<List<ActivityLogEntry>> GetActivityLogsAsync(DateTime? since = null);
        Task LogActivityAsync(ActivityLogEntry entry);
    }

    public class SettingsService : ISettingsService
    {
        private readonly string _settingsPath;
        private readonly string _profilesPath;
        private readonly string _historyPath;
        private readonly string _logsPath;
        private AppSettings? _cachedSettings;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public SettingsService(string? basePath = null)
        {
            basePath ??= Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SqlConstraintHelper"
            );

            _settingsPath = Path.Combine(basePath, "settings.json");
            _profilesPath = Path.Combine(basePath, "profiles.json");
            _historyPath = Path.Combine(basePath, "history.json");
            _logsPath = Path.Combine(basePath, "logs");

            // Ensure directories exist
            Directory.CreateDirectory(basePath);
            Directory.CreateDirectory(_logsPath);
        }

        public async Task<AppSettings> LoadSettingsAsync()
        {
            if (_cachedSettings != null)
                return _cachedSettings;

            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = await File.ReadAllTextAsync(_settingsPath);
                    _cachedSettings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                }
                else
                {
                    _cachedSettings = new AppSettings();
                    await SaveSettingsAsync(_cachedSettings);
                }
            }
            catch (Exception ex)
            {
                await LogActivityAsync(new ActivityLogEntry
                {
                    Level = LogLevel.Error,
                    Category = "Settings",
                    Message = "Failed to load settings",
                    Details = ex.ToString()
                });
                _cachedSettings = new AppSettings();
            }

            return _cachedSettings;
        }

        public async Task SaveSettingsAsync(AppSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, JsonOptions);
                await File.WriteAllTextAsync(_settingsPath, json);
                _cachedSettings = settings;
            }
            catch (Exception ex)
            {
                await LogActivityAsync(new ActivityLogEntry
                {
                    Level = LogLevel.Error,
                    Category = "Settings",
                    Message = "Failed to save settings",
                    Details = ex.ToString()
                });
                throw;
            }
        }

        public async Task<List<ConnectionProfile>> GetProfilesAsync()
        {
            try
            {
                if (File.Exists(_profilesPath))
                {
                    var json = await File.ReadAllTextAsync(_profilesPath);
                    return JsonSerializer.Deserialize<List<ConnectionProfile>>(json, JsonOptions) ?? new List<ConnectionProfile>();
                }
            }
            catch (Exception ex)
            {
                await LogActivityAsync(new ActivityLogEntry
                {
                    Level = LogLevel.Error,
                    Category = "Profiles",
                    Message = "Failed to load profiles",
                    Details = ex.ToString()
                });
            }

            return new List<ConnectionProfile>();
        }

        public async Task<ConnectionProfile?> GetProfileAsync(Guid id)
        {
            var profiles = await GetProfilesAsync();
            return profiles.FirstOrDefault(p => p.Id == id);
        }

        public async Task SaveProfileAsync(ConnectionProfile profile)
        {
            try
            {
                var profiles = await GetProfilesAsync();

                var existing = profiles.FirstOrDefault(p => p.Id == profile.Id);
                if (existing != null)
                {
                    profiles.Remove(existing);
                }

                profile.LastUsedDate = DateTime.Now;
                profiles.Add(profile);

                var json = JsonSerializer.Serialize(profiles, JsonOptions);
                await File.WriteAllTextAsync(_profilesPath, json);

                await LogActivityAsync(new ActivityLogEntry
                {
                    Level = LogLevel.Info,
                    Category = "Profiles",
                    Message = $"Profile '{profile.Name}' saved",
                    ConnectionName = profile.Name
                });
            }
            catch (Exception ex)
            {
                await LogActivityAsync(new ActivityLogEntry
                {
                    Level = LogLevel.Error,
                    Category = "Profiles",
                    Message = "Failed to save profile",
                    Details = ex.ToString()
                });
                throw;
            }
        }

        public async Task DeleteProfileAsync(Guid id)
        {
            try
            {
                var profiles = await GetProfilesAsync();
                var profile = profiles.FirstOrDefault(p => p.Id == id);

                if (profile != null)
                {
                    profiles.Remove(profile);
                    var json = JsonSerializer.Serialize(profiles, JsonOptions);
                    await File.WriteAllTextAsync(_profilesPath, json);

                    await LogActivityAsync(new ActivityLogEntry
                    {
                        Level = LogLevel.Info,
                        Category = "Profiles",
                        Message = $"Profile '{profile.Name}' deleted"
                    });
                }
            }
            catch (Exception ex)
            {
                await LogActivityAsync(new ActivityLogEntry
                {
                    Level = LogLevel.Error,
                    Category = "Profiles",
                    Message = "Failed to delete profile",
                    Details = ex.ToString()
                });
                throw;
            }
        }

        public async Task<List<QueryHistoryItem>> GetQueryHistoryAsync(int maxItems = 100)
        {
            try
            {
                if (File.Exists(_historyPath))
                {
                    var json = await File.ReadAllTextAsync(_historyPath);
                    var history = JsonSerializer.Deserialize<List<QueryHistoryItem>>(json, JsonOptions) ?? new List<QueryHistoryItem>();

                    return history
                        .OrderByDescending(h => h.ExecutedDate)
                        .Take(maxItems)
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                await LogActivityAsync(new ActivityLogEntry
                {
                    Level = LogLevel.Error,
                    Category = "History",
                    Message = "Failed to load query history",
                    Details = ex.ToString()
                });
            }

            return new List<QueryHistoryItem>();
        }

        public async Task SaveQueryHistoryAsync(QueryHistoryItem item)
        {
            try
            {
                var history = await GetQueryHistoryAsync(10000); // Load all for saving
                history.Insert(0, item);

                // Keep only last 100 items
                var settings = await LoadSettingsAsync();
                if (history.Count > settings.QueryHistory.MaxHistoryItems)
                {
                    history = history.Take(settings.QueryHistory.MaxHistoryItems).ToList();
                }

                var json = JsonSerializer.Serialize(history, JsonOptions);
                await File.WriteAllTextAsync(_historyPath, json);
            }
            catch (Exception ex)
            {
                await LogActivityAsync(new ActivityLogEntry
                {
                    Level = LogLevel.Error,
                    Category = "History",
                    Message = "Failed to save query history",
                    Details = ex.ToString()
                });
            }
        }

        public async Task ClearQueryHistoryAsync()
        {
            try
            {
                if (File.Exists(_historyPath))
                {
                    File.Delete(_historyPath);
                }

                await LogActivityAsync(new ActivityLogEntry
                {
                    Level = LogLevel.Info,
                    Category = "History",
                    Message = "Query history cleared"
                });
            }
            catch (Exception ex)
            {
                await LogActivityAsync(new ActivityLogEntry
                {
                    Level = LogLevel.Error,
                    Category = "History",
                    Message = "Failed to clear query history",
                    Details = ex.ToString()
                });
                throw;
            }
        }

        public async Task<List<ActivityLogEntry>> GetActivityLogsAsync(DateTime? since = null)
        {
            var logs = new List<ActivityLogEntry>();
            since ??= DateTime.Now.AddDays(-7); // Default: last 7 days

            try
            {
                var logFiles = Directory.GetFiles(_logsPath, "*.json")
                    .OrderByDescending(f => f);

                foreach (var file in logFiles)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        var entries = JsonSerializer.Deserialize<List<ActivityLogEntry>>(json, JsonOptions) ?? new List<ActivityLogEntry>();

                        logs.AddRange(entries.Where(e => e.Timestamp >= since.Value));
                    }
                    catch
                    {
                        // Skip corrupted log files
                        continue;
                    }
                }
            }
            catch
            {
                // Return empty list if logs directory doesn't exist or can't be read
            }

            return logs.OrderByDescending(l => l.Timestamp).ToList();
        }

        public async Task LogActivityAsync(ActivityLogEntry entry)
        {
            try
            {
                var date = entry.Timestamp.ToString("yyyy-MM-dd");
                var logFile = Path.Combine(_logsPath, $"log-{date}.json");

                List<ActivityLogEntry> entries;
                if (File.Exists(logFile))
                {
                    var json = await File.ReadAllTextAsync(logFile);
                    entries = JsonSerializer.Deserialize<List<ActivityLogEntry>>(json, JsonOptions) ?? new List<ActivityLogEntry>();
                }
                else
                {
                    entries = new List<ActivityLogEntry>();
                }

                entries.Add(entry);

                var newJson = JsonSerializer.Serialize(entries, JsonOptions);
                await File.WriteAllTextAsync(logFile, newJson);

                // Clean up old log files (keep last 30 days)
                await CleanupOldLogsAsync(30);
            }
            catch
            {
                // Silently fail - don't throw exceptions from logging
            }
        }

        private async Task CleanupOldLogsAsync(int keepDays)
        {
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-keepDays);
                var oldFiles = Directory.GetFiles(_logsPath, "*.json")
                    .Where(f => File.GetCreationTime(f) < cutoffDate);

                foreach (var file in oldFiles)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // Skip files that can't be deleted
                    }
                }

                await Task.CompletedTask;
            }
            catch
            {
                // Silently fail cleanup
            }
        }
    }
}