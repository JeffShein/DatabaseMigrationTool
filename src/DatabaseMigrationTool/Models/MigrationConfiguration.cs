using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace DatabaseMigrationTool.Models
{
    /// <summary>
    /// Configuration file model that can store all parameters for export, import, and schema operations
    /// </summary>
    public class MigrationConfiguration
    {
        /// <summary>
        /// Configuration format version for backward compatibility
        /// </summary>
        public string Version { get; set; } = "1.0";
        
        /// <summary>
        /// User-friendly name for this configuration
        /// </summary>
        public string? Name { get; set; }
        
        /// <summary>
        /// Optional description of what this configuration does
        /// </summary>
        public string? Description { get; set; }
        
        /// <summary>
        /// When this configuration was created
        /// </summary>
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// Export operation configuration
        /// </summary>
        public ExportConfig? Export { get; set; }
        
        /// <summary>
        /// Import operation configuration  
        /// </summary>
        public ImportConfig? Import { get; set; }
        
        /// <summary>
        /// Schema viewing/export configuration
        /// </summary>
        public SchemaConfig? Schema { get; set; }
        
        /// <summary>
        /// Global settings that apply to multiple operations
        /// </summary>
        public GlobalConfig Global { get; set; } = new GlobalConfig();
    }
    
    public class ExportConfig
    {
        /// <summary>
        /// Database provider (sqlserver, mysql, postgresql, firebird)
        /// </summary>
        [Required(ErrorMessage = "Database provider is required")]
        [RegularExpression(@"^(sqlserver|mysql|postgresql|firebird)$", ErrorMessage = "Provider must be one of: sqlserver, mysql, postgresql, firebird")]
        public string Provider { get; set; } = string.Empty;
        
        /// <summary>
        /// Connection string for source database
        /// </summary>
        [Required(ErrorMessage = "Connection string is required")]
        [MinLength(10, ErrorMessage = "Connection string must be at least 10 characters")]
        public string ConnectionString { get; set; } = string.Empty;
        
        /// <summary>
        /// Output directory path for export files
        /// </summary>
        [Required(ErrorMessage = "Output path is required")]
        [MinLength(3, ErrorMessage = "Output path must be at least 3 characters")]
        public string OutputPath { get; set; } = string.Empty;
        
        /// <summary>
        /// Comma-separated list of tables to export (empty = all tables)
        /// </summary>
        [RegularExpression(@"^[a-zA-Z_][a-zA-Z0-9_]*(\.[a-zA-Z_][a-zA-Z0-9_]*)?(\s*,\s*[a-zA-Z_][a-zA-Z0-9_]*(\.[a-zA-Z_][a-zA-Z0-9_]*)?)*$|^$", 
            ErrorMessage = "Tables must be valid table names separated by commas")]
        public string? Tables { get; set; }
        
        /// <summary>
        /// Path to JSON file containing table criteria filters
        /// </summary>
        [RegularExpression(@"^.*\.json$", ErrorMessage = "Table criteria file must be a .json file")]
        public string? TableCriteriaFile { get; set; }
        
        /// <summary>
        /// Table criteria as dictionary (alternative to file)
        /// </summary>
        public Dictionary<string, string>? TableCriteria { get; set; }
        
        /// <summary>
        /// Number of rows to process in a single batch
        /// </summary>
        [Range(1, 1000000, ErrorMessage = "Batch size must be between 1 and 1,000,000")]
        public int BatchSize { get; set; } = 100000;
        
        /// <summary>
        /// Export schema only (no data)
        /// </summary>
        public bool SchemaOnly { get; set; } = false;
    }
    
    public class ImportConfig
    {
        /// <summary>
        /// Database provider (sqlserver, mysql, postgresql, firebird)
        /// </summary>
        public string Provider { get; set; } = string.Empty;
        
        /// <summary>
        /// Connection string for target database
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;
        
        /// <summary>
        /// Input directory path containing export files
        /// </summary>
        public string InputPath { get; set; } = string.Empty;
        
        /// <summary>
        /// Comma-separated list of tables to import (empty = all tables)
        /// </summary>
        public string? Tables { get; set; }
        
        /// <summary>
        /// Number of rows to process in a single batch
        /// </summary>
        public int BatchSize { get; set; } = 100000;
        
        /// <summary>
        /// Skip schema creation (assume tables already exist)
        /// </summary>
        public bool NoCreateSchema { get; set; } = false;
        
        /// <summary>
        /// Skip foreign key creation
        /// </summary>
        public bool NoCreateForeignKeys { get; set; } = false;
        
        /// <summary>
        /// Import schema only (no data)
        /// </summary>
        public bool SchemaOnly { get; set; } = false;
        
        /// <summary>
        /// Continue processing on errors
        /// </summary>
        public bool ContinueOnError { get; set; } = false;
    }
    
    public class SchemaConfig
    {
        /// <summary>
        /// Database provider (sqlserver, mysql, postgresql, firebird)
        /// </summary>
        public string Provider { get; set; } = string.Empty;
        
        /// <summary>
        /// Connection string for database
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;
        
        /// <summary>
        /// Comma-separated list of tables to view (empty = all tables)
        /// </summary>
        public string? Tables { get; set; }
        
        /// <summary>
        /// Show detailed schema information
        /// </summary>
        public bool Verbose { get; set; } = false;
        
        /// <summary>
        /// Generate SQL scripts
        /// </summary>
        public bool GenerateScripts { get; set; } = false;
        
        /// <summary>
        /// Output path for SQL scripts
        /// </summary>
        public string? ScriptPath { get; set; }
    }
    
    public class GlobalConfig
    {
        /// <summary>
        /// Default timeout for database operations (seconds)
        /// </summary>
        public int TimeoutSeconds { get; set; } = 300;
        
        /// <summary>
        /// Enable detailed logging
        /// </summary>
        public bool VerboseLogging { get; set; } = false;
        
        /// <summary>
        /// Automatically confirm overwrite operations (dangerous - use with caution)
        /// </summary>
        public bool AutoConfirmOverwrites { get; set; } = false;
        
        /// <summary>
        /// Skip overwrite checks entirely (very dangerous)
        /// </summary>
        public bool SkipOverwriteChecks { get; set; } = false;
    }
    
    /// <summary>
    /// Helper extensions for working with MigrationConfiguration
    /// </summary>
    public static class MigrationConfigurationExtensions
    {
        /// <summary>
        /// Convert ExportConfig to ExportCommandOptions
        /// </summary>
        public static ExportCommandOptions ToExportCommandOptions(this ExportConfig config)
        {
            return new ExportCommandOptions
            {
                Provider = config.Provider,
                ConnectionString = config.ConnectionString,
                OutputPath = config.OutputPath,
                Tables = config.Tables,
                TableCriteriaFile = config.TableCriteriaFile,
                BatchSize = config.BatchSize,
                SchemaOnly = config.SchemaOnly
            };
        }
        
        /// <summary>
        /// Convert ImportConfig to ImportCommandOptions
        /// </summary>
        public static ImportCommandOptions ToImportCommandOptions(this ImportConfig config)
        {
            return new ImportCommandOptions
            {
                Provider = config.Provider,
                ConnectionString = config.ConnectionString,
                InputPath = config.InputPath,
                Tables = config.Tables,
                BatchSize = config.BatchSize,
                NoCreateSchema = config.NoCreateSchema,
                NoCreateForeignKeys = config.NoCreateForeignKeys,
                SchemaOnly = config.SchemaOnly,
                ContinueOnError = config.ContinueOnError
            };
        }
        
        /// <summary>
        /// Convert SchemaConfig to SchemaCommandOptions
        /// </summary>
        public static SchemaCommandOptions ToSchemaCommandOptions(this SchemaConfig config)
        {
            return new SchemaCommandOptions
            {
                Provider = config.Provider,
                ConnectionString = config.ConnectionString,
                Tables = config.Tables,
                Verbose = config.Verbose,
                ScriptOutput = config.GenerateScripts,
                ScriptPath = config.ScriptPath
            };
        }
        
        /// <summary>
        /// Create ExportConfig from ExportCommandOptions
        /// </summary>
        public static ExportConfig FromExportCommandOptions(ExportCommandOptions options)
        {
            return new ExportConfig
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                OutputPath = options.OutputPath,
                Tables = options.Tables,
                TableCriteriaFile = options.TableCriteriaFile,
                BatchSize = options.BatchSize,
                SchemaOnly = options.SchemaOnly
            };
        }
        
        /// <summary>
        /// Create ImportConfig from ImportCommandOptions
        /// </summary>
        public static ImportConfig FromImportCommandOptions(ImportCommandOptions options)
        {
            return new ImportConfig
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                InputPath = options.InputPath,
                Tables = options.Tables,
                BatchSize = options.BatchSize,
                NoCreateSchema = options.NoCreateSchema,
                NoCreateForeignKeys = options.NoCreateForeignKeys,
                SchemaOnly = options.SchemaOnly,
                ContinueOnError = options.ContinueOnError
            };
        }
        
        /// <summary>
        /// Create SchemaConfig from SchemaCommandOptions
        /// </summary>
        public static SchemaConfig FromSchemaCommandOptions(SchemaCommandOptions options)
        {
            return new SchemaConfig
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                Tables = options.Tables,
                Verbose = options.Verbose,
                GenerateScripts = options.ScriptOutput,
                ScriptPath = options.ScriptPath
            };
        }
    }
}