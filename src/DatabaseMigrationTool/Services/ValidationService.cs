using System.ComponentModel.DataAnnotations;
using System.IO;
using DatabaseMigrationTool.Constants;
using DatabaseMigrationTool.Models;
using Microsoft.Extensions.Logging;

namespace DatabaseMigrationTool.Services
{
    public interface IValidationService
    {
        Models.ValidationResult ValidateObject<T>(T obj) where T : class;
        Models.ValidationResult ValidateConnectionString(string connectionString, string provider);
        Models.ValidationResult ValidatePath(string path, bool shouldExist = false);
        Models.ValidationResult ValidateTableNames(string? tableNames);
        Models.ValidationResult ValidateProvider(string provider);
    }

    public class ValidationService : IValidationService
    {
        private readonly ILogger<ValidationService> _logger;

        public ValidationService(ILogger<ValidationService> logger)
        {
            _logger = logger;
        }

        public Models.ValidationResult ValidateObject<T>(T obj) where T : class
        {
            if (obj == null)
            {
                return Models.ValidationResult.Invalid(new[] { "Object cannot be null" });
            }

            var context = new ValidationContext(obj);
            var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
            
            bool isValid = Validator.TryValidateObject(obj, context, results, true);
            
            if (isValid)
            {
                _logger.LogDebug("Object validation successful for type {Type}", typeof(T).Name);
                return Models.ValidationResult.Valid();
            }

            var errors = results.Select(r => r.ErrorMessage ?? "Unknown validation error").ToList();
            _logger.LogWarning("Object validation failed for type {Type}: {Errors}", 
                typeof(T).Name, string.Join(", ", errors));
            
            return Models.ValidationResult.Invalid(errors);
        }

        public Models.ValidationResult ValidateConnectionString(string connectionString, string provider)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return Models.ValidationResult.Invalid(new[] { "Connection string cannot be empty" });
            }

            if (string.IsNullOrWhiteSpace(provider))
            {
                return Models.ValidationResult.Invalid(new[] { "Provider cannot be empty" });
            }

            if (connectionString.Length < 10)
            {
                return Models.ValidationResult.Invalid(new[] { "Connection string is too short" });
            }

            // Provider-specific validation
            var errors = new List<string>();
            var lowerConnectionString = connectionString.ToLowerInvariant();

            switch (provider.ToLowerInvariant())
            {
                case "sqlserver":
                    if (!lowerConnectionString.Contains("server") && !lowerConnectionString.Contains("data source"))
                    {
                        errors.Add("SQL Server connection string must contain 'Server' or 'Data Source'");
                    }
                    if (!lowerConnectionString.Contains("database") && !lowerConnectionString.Contains("initial catalog"))
                    {
                        errors.Add("SQL Server connection string must contain 'Database' or 'Initial Catalog'");
                    }
                    break;

                case "mysql":
                    if (!lowerConnectionString.Contains("server") && !lowerConnectionString.Contains("host"))
                    {
                        errors.Add("MySQL connection string must contain 'Server' or 'Host'");
                    }
                    if (!lowerConnectionString.Contains("database"))
                    {
                        errors.Add("MySQL connection string must contain 'Database'");
                    }
                    break;

                case "postgresql":
                    if (!lowerConnectionString.Contains("host") && !lowerConnectionString.Contains("server"))
                    {
                        errors.Add("PostgreSQL connection string must contain 'Host' or 'Server'");
                    }
                    if (!lowerConnectionString.Contains("database"))
                    {
                        errors.Add("PostgreSQL connection string must contain 'Database'");
                    }
                    break;

                case "firebird":
                    if (!lowerConnectionString.Contains("datasource") && !lowerConnectionString.Contains("server") && !lowerConnectionString.Contains("database"))
                    {
                        errors.Add("Firebird connection string must contain 'DataSource', 'Server', or 'Database'");
                    }
                    break;

                default:
                    errors.Add($"Unknown provider: {provider}");
                    break;
            }

            return errors.Any() 
                ? Models.ValidationResult.Invalid(errors) 
                : Models.ValidationResult.Valid();
        }

        public Models.ValidationResult ValidatePath(string path, bool shouldExist = false)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return Models.ValidationResult.Invalid(new[] { "Path cannot be empty" });
            }

            // Check for invalid characters
            var invalidChars = Path.GetInvalidPathChars();
            if (path.Any(c => invalidChars.Contains(c)))
            {
                return Models.ValidationResult.Invalid(new[] { "Path contains invalid characters" });
            }

            if (shouldExist)
            {
                if (!Directory.Exists(path) && !File.Exists(path))
                {
                    return Models.ValidationResult.Invalid(new[] { $"Path does not exist: {path}" });
                }
            }

            try
            {
                // Try to get the full path to validate format
                Path.GetFullPath(path);
                return Models.ValidationResult.Valid();
            }
            catch (Exception ex)
            {
                return Models.ValidationResult.Invalid(new[] { $"Invalid path format: {ex.Message}" });
            }
        }

        public Models.ValidationResult ValidateTableNames(string? tableNames)
        {
            if (string.IsNullOrWhiteSpace(tableNames))
            {
                return Models.ValidationResult.Valid(); // Empty is valid (means all tables)
            }

            var tables = tableNames.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var errors = new List<string>();

            foreach (var table in tables)
            {
                var trimmedTable = table.Trim();
                
                // Basic table name validation
                if (string.IsNullOrWhiteSpace(trimmedTable))
                {
                    errors.Add("Empty table name found");
                    continue;
                }

                // Check for valid identifier characters
                if (!IsValidIdentifier(trimmedTable))
                {
                    errors.Add($"Invalid table name: {trimmedTable}");
                }
            }

            return errors.Any() 
                ? Models.ValidationResult.Invalid(errors) 
                : Models.ValidationResult.Valid();
        }

        public Models.ValidationResult ValidateProvider(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider))
            {
                return Models.ValidationResult.Invalid(new[] { "Provider cannot be empty" });
            }

            var validProviders = new[] { 
                DatabaseConstants.ProviderNames.SqlServer.ToLowerInvariant(),
                DatabaseConstants.ProviderNames.MySQL.ToLowerInvariant(),
                DatabaseConstants.ProviderNames.PostgreSQL.ToLowerInvariant(),
                DatabaseConstants.ProviderNames.Firebird.ToLowerInvariant(),
                "sqlserver", "mysql", "postgresql", "firebird" // Accept lowercase versions
            };

            if (!validProviders.Contains(provider.ToLowerInvariant()))
            {
                return Models.ValidationResult.Invalid(new[] { 
                    $"Invalid provider: {provider}. Valid providers are: sqlserver, mysql, postgresql, firebird" 
                });
            }

            return Models.ValidationResult.Valid();
        }

        private static bool IsValidIdentifier(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
                return false;

            // Handle schema.table format
            var parts = identifier.Split('.');
            if (parts.Length > 2)
                return false; // Too many parts

            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part))
                    return false;

                // Must start with letter or underscore
                if (!char.IsLetter(part[0]) && part[0] != '_')
                    return false;

                // Rest can be letters, digits, or underscore
                for (int i = 1; i < part.Length; i++)
                {
                    if (!char.IsLetterOrDigit(part[i]) && part[i] != '_')
                        return false;
                }
            }

            return true;
        }
    }
}