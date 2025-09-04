using DatabaseMigrationTool.Models;
using System.IO;
using System.Text.Json;

namespace DatabaseMigrationTool.Utilities
{
    /// <summary>
    /// Manages saving and loading migration configuration files
    /// </summary>
    public static class ConfigurationManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true
        };
        
        /// <summary>
        /// Save configuration to JSON file
        /// </summary>
        public static async Task SaveConfigurationAsync(MigrationConfiguration config, string filePath)
        {
            try
            {
                // Ensure directory exists
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                // Serialize to JSON
                var json = JsonSerializer.Serialize(config, JsonOptions);
                
                // Write to file
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save configuration to '{filePath}': {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Load configuration from JSON file
        /// </summary>
        public static async Task<MigrationConfiguration> LoadConfigurationAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"Configuration file not found: {filePath}");
                }
                
                var json = await File.ReadAllTextAsync(filePath);
                
                var config = JsonSerializer.Deserialize<MigrationConfiguration>(json, JsonOptions);
                
                if (config == null)
                {
                    throw new InvalidOperationException("Failed to deserialize configuration - file may be corrupted");
                }
                
                // Validate and upgrade if needed
                ValidateAndUpgradeConfiguration(config);
                
                return config;
            }
            catch (Exception ex) when (!(ex is FileNotFoundException))
            {
                throw new InvalidOperationException($"Failed to load configuration from '{filePath}': {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Create a sample configuration file with comments
        /// </summary>
        public static async Task CreateSampleConfigurationAsync(string filePath)
        {
            var sampleConfig = new MigrationConfiguration
            {
                Name = "Sample Migration Configuration",
                Description = "This is a sample configuration file showing all available options",
                Export = new ExportConfig
                {
                    Provider = "sqlserver",
                    ConnectionString = "Server=localhost;Database=SourceDB;User Id=sa;Password=your_password;TrustServerCertificate=True;",
                    OutputPath = "./export_output",
                    Tables = "Customers,Orders,Products",
                    BatchSize = 100000,
                    SchemaOnly = false
                },
                Import = new ImportConfig
                {
                    Provider = "mysql", 
                    ConnectionString = "Server=localhost;Database=TargetDB;User=root;Password=your_password;",
                    InputPath = "./export_output",
                    BatchSize = 100000,
                    NoCreateSchema = false,
                    NoCreateForeignKeys = false,
                    SchemaOnly = false,
                    ContinueOnError = false
                },
                Schema = new SchemaConfig
                {
                    Provider = "postgresql",
                    ConnectionString = "Host=localhost;Database=mydb;Username=postgres;Password=your_password;",
                    Verbose = true,
                    GenerateScripts = true,
                    ScriptPath = "./schema_scripts"
                },
                Global = new GlobalConfig
                {
                    TimeoutSeconds = 300,
                    VerboseLogging = false,
                    AutoConfirmOverwrites = false,
                    SkipOverwriteChecks = false
                }
            };
            
            await SaveConfigurationAsync(sampleConfig, filePath);
        }
        
        /// <summary>
        /// Validate configuration and perform any necessary upgrades
        /// </summary>
        private static void ValidateAndUpgradeConfiguration(MigrationConfiguration config)
        {
            // Set default version if missing
            if (string.IsNullOrEmpty(config.Version))
            {
                config.Version = "1.0";
            }
            
            // Ensure global config exists
            if (config.Global == null)
            {
                config.Global = new GlobalConfig();
            }
            
            // Validate provider names
            var validProviders = new[] { "sqlserver", "mysql", "postgresql", "firebird" };
            
            if (config.Export != null && !string.IsNullOrEmpty(config.Export.Provider))
            {
                if (!validProviders.Contains(config.Export.Provider.ToLowerInvariant()))
                {
                    throw new InvalidOperationException($"Invalid export provider: {config.Export.Provider}. Valid providers: {string.Join(", ", validProviders)}");
                }
            }
            
            if (config.Import != null && !string.IsNullOrEmpty(config.Import.Provider))
            {
                if (!validProviders.Contains(config.Import.Provider.ToLowerInvariant()))
                {
                    throw new InvalidOperationException($"Invalid import provider: {config.Import.Provider}. Valid providers: {string.Join(", ", validProviders)}");
                }
            }
            
            if (config.Schema != null && !string.IsNullOrEmpty(config.Schema.Provider))
            {
                if (!validProviders.Contains(config.Schema.Provider.ToLowerInvariant()))
                {
                    throw new InvalidOperationException($"Invalid schema provider: {config.Schema.Provider}. Valid providers: {string.Join(", ", validProviders)}");
                }
            }
            
            // Validate batch sizes
            if (config.Export != null && config.Export.BatchSize <= 0)
            {
                config.Export.BatchSize = 100000;
            }
            
            if (config.Import != null && config.Import.BatchSize <= 0)
            {
                config.Import.BatchSize = 100000;
            }
            
            // Validate timeout
            if (config.Global.TimeoutSeconds <= 0)
            {
                config.Global.TimeoutSeconds = 300;
            }
        }
        
        /// <summary>
        /// Check if a file appears to be a valid configuration file
        /// </summary>
        public static async Task<bool> IsValidConfigurationFileAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return false;
                }
                
                var json = await File.ReadAllTextAsync(filePath);
                var config = JsonSerializer.Deserialize<MigrationConfiguration>(json, JsonOptions);
                
                return config != null && !string.IsNullOrEmpty(config.Version);
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Create configuration from current GUI state
        /// </summary>
        public static MigrationConfiguration CreateFromGuiState(
            string? name,
            string? description,
            ExportConfig? exportConfig,
            ImportConfig? importConfig, 
            SchemaConfig? schemaConfig,
            GlobalConfig? globalConfig = null)
        {
            return new MigrationConfiguration
            {
                Name = name,
                Description = description,
                CreatedDate = DateTime.UtcNow,
                Export = exportConfig,
                Import = importConfig,
                Schema = schemaConfig,
                Global = globalConfig ?? new GlobalConfig()
            };
        }
        
        /// <summary>
        /// Get default configuration file name based on operation type
        /// </summary>
        public static string GetDefaultConfigFileName(string operationType)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return $"{operationType}_config_{timestamp}.json";
        }
    }
}