using System.ComponentModel;
using System.Text.Json.Serialization;

namespace DatabaseMigrationTool.Models
{
    public class UserSettings
    {
        // Window Settings
        public WindowSettings MainWindow { get; set; } = new();

        // Default Values
        public DefaultValues Defaults { get; set; } = new();

        // UI Preferences
        public UiPreferences Interface { get; set; } = new();

        // Recent Items
        public RecentItems Recent { get; set; } = new();

        // Performance Settings
        public PerformanceSettings Performance { get; set; } = new();
    }

    public class WindowSettings
    {
        public double Width { get; set; } = 1200;
        public double Height { get; set; } = 900;
        public double Left { get; set; } = double.NaN;
        public double Top { get; set; } = double.NaN;
        public bool IsMaximized { get; set; } = false;
    }

    public class DefaultValues
    {
        public int BatchSize { get; set; } = 100000;
        public string DefaultProvider { get; set; } = "SqlServer";
        public bool SchemaOnly { get; set; } = false;
        public bool CreateSchema { get; set; } = true;
        public bool CreateForeignKeys { get; set; } = true;
        public bool ContinueOnError { get; set; } = false;
        public bool GenerateScripts { get; set; } = false;
    }

    public class UiPreferences
    {
        public int SelectedTabIndex { get; set; } = 0;
        public bool ShowDetailedProgress { get; set; } = false;
        public bool AutoClearProgress { get; set; } = true;
        public bool ConfirmOperations { get; set; } = true;
        public string Theme { get; set; } = "Default";
    }

    public class RecentItems
    {
        public List<string> OutputDirectories { get; set; } = new();
        public List<string> InputDirectories { get; set; } = new();
        public List<string> ScriptPaths { get; set; } = new();
        public List<string> ConfigurationFiles { get; set; } = new();
        public List<string> CriteriaFiles { get; set; } = new();
        public int MaxRecentItems { get; set; } = 10;
    }

    public class PerformanceSettings
    {
        public int ConnectionTimeout { get; set; } = 30;
        public int CommandTimeout { get; set; } = 300;
        public int MaxRetryAttempts { get; set; } = 3;
        public int RetryDelayMs { get; set; } = 1000;
        public bool EnableCompression { get; set; } = true;
        public bool OptimizeMemoryUsage { get; set; } = false;
    }
}