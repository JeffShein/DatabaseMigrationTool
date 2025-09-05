using DatabaseMigrationTool.Constants;
using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Utilities;
using System.IO;

namespace DatabaseMigrationTool.Services
{
    public interface IConfigurationValidator
    {
        ValidationResult ValidateExportConfig(ExportConfig config);
        ValidationResult ValidateImportConfig(ImportConfig config);
        ValidationResult ValidateSchemaConfig(SchemaConfig config);
        ValidationResult ValidateConnectionString(string connectionString, string providerName);
    }
    
    public class ConfigurationValidator : IConfigurationValidator
    {
        public ValidationResult ValidateExportConfig(ExportConfig config)
        {
            var result = ValidationResult.Valid();
            
            if (config == null)
            {
                return ValidationResult.Invalid(new[] { "Export configuration is null" });
            }
            
            // Provider validation
            if (string.IsNullOrWhiteSpace(config.Provider))
            {
                result.AddError("Provider is required");
            }
            else if (!StringUtilities.IsValidProviderName(config.Provider))
            {
                result.AddError($"Invalid provider: {config.Provider}. Valid providers are: {string.Join(", ", GetValidProviders())}");
            }
            
            // Connection string validation
            var connectionValidation = ValidateConnectionString(config.ConnectionString ?? string.Empty, config.Provider ?? string.Empty);
            if (!connectionValidation.Success)
            {
                result.Errors.AddRange(connectionValidation.Errors);
            }
            
            // Output path validation
            if (string.IsNullOrWhiteSpace(config.OutputPath))
            {
                result.AddError("Output path is required");
            }
            else if (!FileUtilities.IsValidPath(config.OutputPath))
            {
                result.AddError("Output path is not valid");
            }
            
            // Batch size validation
            if (config.BatchSize < DatabaseConstants.ValidationConstants.MinBatchSize || 
                config.BatchSize > DatabaseConstants.ValidationConstants.MaxBatchSize)
            {
                result.AddError($"Batch size must be between {DatabaseConstants.ValidationConstants.MinBatchSize:N0} and {DatabaseConstants.ValidationConstants.MaxBatchSize:N0}");
            }
            
            // Table criteria file validation
            if (!string.IsNullOrWhiteSpace(config.TableCriteriaFile))
            {
                if (!File.Exists(config.TableCriteriaFile))
                {
                    result.AddWarning($"Table criteria file does not exist: {config.TableCriteriaFile}");
                }
                else if (!config.TableCriteriaFile.EndsWith(DatabaseConstants.FileExtensions.Json, StringComparison.OrdinalIgnoreCase))
                {
                    result.AddWarning("Table criteria file should be a JSON file");
                }
            }
            
            // Tables validation
            if (!string.IsNullOrWhiteSpace(config.Tables))
            {
                var tableNames = StringUtilities.ParseTableNames(config.Tables);
                if (!tableNames.Any())
                {
                    result.AddWarning("No valid table names found in Tables parameter");
                }
            }
            
            if (result.HasErrors)
            {
                return ValidationResult.Invalid(result.Errors, result.Warnings);
            }
            return result;
        }
        
        public ValidationResult ValidateImportConfig(ImportConfig config)
        {
            var result = ValidationResult.Valid();
            
            if (config == null)
            {
                return ValidationResult.Invalid(new[] { "Import configuration is null" });
            }
            
            // Provider validation
            if (string.IsNullOrWhiteSpace(config.Provider))
            {
                result.AddError("Provider is required");
            }
            else if (!StringUtilities.IsValidProviderName(config.Provider))
            {
                result.AddError($"Invalid provider: {config.Provider}. Valid providers are: {string.Join(", ", GetValidProviders())}");
            }
            
            // Connection string validation
            var connectionValidation = ValidateConnectionString(config.ConnectionString ?? string.Empty, config.Provider ?? string.Empty);
            if (!connectionValidation.Success)
            {
                result.Errors.AddRange(connectionValidation.Errors);
            }
            
            // Input path validation
            if (string.IsNullOrWhiteSpace(config.InputPath))
            {
                result.AddError("Input path is required");
            }
            else if (!Directory.Exists(config.InputPath))
            {
                result.AddError($"Input directory does not exist: {config.InputPath}");
            }
            else if (!Utilities.MetadataManager.IsValidExport(config.InputPath))
            {
                result.AddError($"Input directory does not contain a valid export: {config.InputPath}");
            }
            
            // Batch size validation
            if (config.BatchSize < DatabaseConstants.ValidationConstants.MinBatchSize || 
                config.BatchSize > DatabaseConstants.ValidationConstants.MaxBatchSize)
            {
                result.AddError($"Batch size must be between {DatabaseConstants.ValidationConstants.MinBatchSize:N0} and {DatabaseConstants.ValidationConstants.MaxBatchSize:N0}");
            }
            
            // Logical validation
            if (config.SchemaOnly && config.NoCreateSchema)
            {
                result.AddWarning("Schema-only import with NoCreateSchema flag will result in no operation");
            }
            
            // Tables validation
            if (!string.IsNullOrWhiteSpace(config.Tables))
            {
                var tableNames = StringUtilities.ParseTableNames(config.Tables);
                if (!tableNames.Any())
                {
                    result.AddWarning("No valid table names found in Tables parameter");
                }
            }
            
            if (result.HasErrors)
            {
                return ValidationResult.Invalid(result.Errors, result.Warnings);
            }
            return result;
        }
        
        public ValidationResult ValidateSchemaConfig(SchemaConfig config)
        {
            var result = ValidationResult.Valid();
            
            if (config == null)
            {
                return ValidationResult.Invalid(new[] { "Schema configuration is null" });
            }
            
            // Provider validation
            if (string.IsNullOrWhiteSpace(config.Provider))
            {
                result.AddError("Provider is required");
            }
            else if (!StringUtilities.IsValidProviderName(config.Provider))
            {
                result.AddError($"Invalid provider: {config.Provider}. Valid providers are: {string.Join(", ", GetValidProviders())}");
            }
            
            // Connection string validation
            var connectionValidation = ValidateConnectionString(config.ConnectionString ?? string.Empty, config.Provider ?? string.Empty);
            if (!connectionValidation.Success)
            {
                result.Errors.AddRange(connectionValidation.Errors);
            }
            
            // Script path validation
            if (config.GenerateScripts)
            {
                if (string.IsNullOrWhiteSpace(config.ScriptPath))
                {
                    result.AddError("Script path is required when GenerateScripts is enabled");
                }
                else if (!FileUtilities.IsValidPath(config.ScriptPath))
                {
                    result.AddError("Script path is not valid");
                }
            }
            
            // Tables validation
            if (!string.IsNullOrWhiteSpace(config.Tables))
            {
                var tableNames = StringUtilities.ParseTableNames(config.Tables);
                if (!tableNames.Any())
                {
                    result.AddWarning("No valid table names found in Tables parameter");
                }
            }
            
            if (result.HasErrors)
            {
                return ValidationResult.Invalid(result.Errors, result.Warnings);
            }
            return result;
        }
        
        public ValidationResult ValidateConnectionString(string connectionString, string providerName)
        {
            var result = ValidationResult.Valid();
            
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                result.AddError("Connection string is required");
                return result;
            }
            
            if (string.IsNullOrWhiteSpace(providerName))
            {
                result.AddError("Provider name is required for connection string validation");
                return result;
            }
            
            // Provider-specific validation
            switch (providerName)
            {
                case DatabaseConstants.ProviderNames.SqlServer:
                    ValidateSqlServerConnectionString(connectionString, result);
                    break;
                case DatabaseConstants.ProviderNames.MySQL:
                    ValidateMySqlConnectionString(connectionString, result);
                    break;
                case DatabaseConstants.ProviderNames.PostgreSQL:
                    ValidatePostgreSqlConnectionString(connectionString, result);
                    break;
                case DatabaseConstants.ProviderNames.Firebird:
                    ValidateFirebirdConnectionString(connectionString, result);
                    break;
            }
            
            if (result.HasErrors)
            {
                return ValidationResult.Invalid(result.Errors, result.Warnings);
            }
            return result;
        }
        
        private void ValidateSqlServerConnectionString(string connectionString, ValidationResult result)
        {
            var requiredKeys = new[] { "server", "data source", "addr" };
            var authenticationKeys = new[] { "integrated security", "user id", "uid", "username" };
            
            var lowerConnectionString = connectionString.ToLowerInvariant();
            
            // Check for server specification
            if (!requiredKeys.Any(key => lowerConnectionString.Contains(key)))
            {
                result.AddError("SQL Server connection string must specify a server (Server, Data Source, or Addr)");
            }
            
            // Check for authentication method
            bool hasIntegratedSecurity = lowerConnectionString.Contains("integrated security=true") || 
                                       lowerConnectionString.Contains("integrated security=sspi");
            bool hasUserCredentials = authenticationKeys.Skip(1).Any(key => lowerConnectionString.Contains(key));
            
            if (!hasIntegratedSecurity && !hasUserCredentials)
            {
                result.AddWarning("SQL Server connection string should specify either Integrated Security or user credentials");
            }
        }
        
        private void ValidateMySqlConnectionString(string connectionString, ValidationResult result)
        {
            var lowerConnectionString = connectionString.ToLowerInvariant();
            
            if (!lowerConnectionString.Contains("server") && !lowerConnectionString.Contains("host"))
            {
                result.AddError("MySQL connection string must specify a server or host");
            }
            
            if (!lowerConnectionString.Contains("database") && !lowerConnectionString.Contains("initial catalog"))
            {
                result.AddWarning("MySQL connection string should specify a database");
            }
        }
        
        private void ValidatePostgreSqlConnectionString(string connectionString, ValidationResult result)
        {
            var lowerConnectionString = connectionString.ToLowerInvariant();
            
            if (!lowerConnectionString.Contains("host") && !lowerConnectionString.Contains("server"))
            {
                result.AddError("PostgreSQL connection string must specify a host");
            }
            
            if (!lowerConnectionString.Contains("database"))
            {
                result.AddWarning("PostgreSQL connection string should specify a database");
            }
        }
        
        private void ValidateFirebirdConnectionString(string connectionString, ValidationResult result)
        {
            var lowerConnectionString = connectionString.ToLowerInvariant();
            
            if (!lowerConnectionString.Contains("datasource") && !lowerConnectionString.Contains("server"))
            {
                result.AddError("Firebird connection string must specify a DataSource or Server");
            }
            
            if (!lowerConnectionString.Contains("database") && !lowerConnectionString.Contains("initial catalog"))
            {
                result.AddError("Firebird connection string must specify a Database or Initial Catalog");
            }
        }
        
        private static string[] GetValidProviders()
        {
            return new[]
            {
                DatabaseConstants.ProviderNames.SqlServer,
                DatabaseConstants.ProviderNames.MySQL,
                DatabaseConstants.ProviderNames.PostgreSQL,
                DatabaseConstants.ProviderNames.Firebird
            };
        }
    }
}