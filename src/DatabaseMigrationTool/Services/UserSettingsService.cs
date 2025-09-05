using System.IO;
using System.Text.Json;
using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Utilities;

namespace DatabaseMigrationTool.Services
{
    public interface IUserSettingsService
    {
        UserSettings Settings { get; }
        Task LoadSettingsAsync();
        Task SaveSettingsAsync();
        void AddRecentDirectory(string directory, string type);
        void AddRecentFile(string filePath, string type);
        void UpdateWindowSettings(double width, double height, double left, double top, bool isMaximized);
        void ResetToDefaults();
    }

    public class UserSettingsService : IUserSettingsService
    {
        private readonly string _settingsFilePath;
        private UserSettings _settings;

        public UserSettings Settings => _settings;

        public UserSettingsService()
        {
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DatabaseMigrationTool");
            Directory.CreateDirectory(appDataPath);
            _settingsFilePath = Path.Combine(appDataPath, "user_settings.json");
            _settings = new UserSettings();
        }

        public async Task LoadSettingsAsync()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = await File.ReadAllTextAsync(_settingsFilePath);
                    var loadedSettings = JsonSerializer.Deserialize<UserSettings>(json, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    });

                    if (loadedSettings != null)
                    {
                        _settings = loadedSettings;
                        
                        // Validate and fix any invalid settings
                        ValidateSettings();
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.HandleError(ex, "Load User Settings", showUserMessage: false);
                // Continue with default settings if load fails
                _settings = new UserSettings();
            }
        }

        public async Task SaveSettingsAsync()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var json = JsonSerializer.Serialize(_settings, options);
                await File.WriteAllTextAsync(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                ErrorHandler.HandleError(ex, "Save User Settings", showUserMessage: false);
            }
        }

        public void AddRecentDirectory(string directory, string type)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return;

            var normalizedPath = Path.GetFullPath(directory);

            List<string>? targetList = type.ToLowerInvariant() switch
            {
                "output" => _settings.Recent.OutputDirectories,
                "input" => _settings.Recent.InputDirectories,
                "script" => _settings.Recent.ScriptPaths,
                _ => null
            };

            if (targetList != null)
            {
                // Remove if already exists
                targetList.Remove(normalizedPath);
                
                // Add to beginning
                targetList.Insert(0, normalizedPath);
                
                // Trim to max items
                while (targetList.Count > _settings.Recent.MaxRecentItems)
                {
                    targetList.RemoveAt(targetList.Count - 1);
                }
            }
        }

        public void AddRecentFile(string filePath, string type)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return;

            var normalizedPath = Path.GetFullPath(filePath);

            List<string>? targetList = type.ToLowerInvariant() switch
            {
                "config" or "configuration" => _settings.Recent.ConfigurationFiles,
                "criteria" => _settings.Recent.CriteriaFiles,
                _ => null
            };

            if (targetList != null)
            {
                // Remove if already exists
                targetList.Remove(normalizedPath);
                
                // Add to beginning
                targetList.Insert(0, normalizedPath);
                
                // Trim to max items
                while (targetList.Count > _settings.Recent.MaxRecentItems)
                {
                    targetList.RemoveAt(targetList.Count - 1);
                }
            }
        }

        public void UpdateWindowSettings(double width, double height, double left, double top, bool isMaximized)
        {
            _settings.MainWindow.Width = width;
            _settings.MainWindow.Height = height;
            _settings.MainWindow.Left = left;
            _settings.MainWindow.Top = top;
            _settings.MainWindow.IsMaximized = isMaximized;
        }

        public void ResetToDefaults()
        {
            _settings = new UserSettings();
        }

        private void ValidateSettings()
        {
            // Validate batch size
            if (_settings.Defaults.BatchSize < 1 || _settings.Defaults.BatchSize > 1000000)
            {
                _settings.Defaults.BatchSize = 100000;
            }

            // Validate timeouts
            if (_settings.Performance.ConnectionTimeout < 5 || _settings.Performance.ConnectionTimeout > 300)
            {
                _settings.Performance.ConnectionTimeout = 30;
            }

            if (_settings.Performance.CommandTimeout < 30 || _settings.Performance.CommandTimeout > 3600)
            {
                _settings.Performance.CommandTimeout = 300;
            }

            // Validate retry settings
            if (_settings.Performance.MaxRetryAttempts < 0 || _settings.Performance.MaxRetryAttempts > 10)
            {
                _settings.Performance.MaxRetryAttempts = 3;
            }

            if (_settings.Performance.RetryDelayMs < 100 || _settings.Performance.RetryDelayMs > 10000)
            {
                _settings.Performance.RetryDelayMs = 1000;
            }

            // Validate window settings
            if (_settings.MainWindow.Width < 800)
            {
                _settings.MainWindow.Width = 1200;
            }

            if (_settings.MainWindow.Height < 600)
            {
                _settings.MainWindow.Height = 900;
            }

            // Clean up recent items - remove non-existent paths
            _settings.Recent.OutputDirectories.RemoveAll(path => !Directory.Exists(path));
            _settings.Recent.InputDirectories.RemoveAll(path => !Directory.Exists(path));
            _settings.Recent.ScriptPaths.RemoveAll(path => !Directory.Exists(path));
            _settings.Recent.ConfigurationFiles.RemoveAll(path => !File.Exists(path));
            _settings.Recent.CriteriaFiles.RemoveAll(path => !File.Exists(path));

            // Validate provider
            var validProviders = new[] { "SqlServer", "MySQL", "PostgreSQL", "Firebird" };
            if (!validProviders.Contains(_settings.Defaults.DefaultProvider))
            {
                _settings.Defaults.DefaultProvider = "SqlServer";
            }
        }
    }
}