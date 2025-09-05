using DatabaseMigrationTool.Constants;
using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Providers;
using DatabaseMigrationTool.Utilities;
using System.IO;

namespace DatabaseMigrationTool.Services
{
    public class SchemaService : ISchemaService
    {
        private readonly IConnectionManager _connectionManager;
        private readonly IConfigurationValidator _validator;
        
        public SchemaService(IConnectionManager connectionManager, IConfigurationValidator validator)
        {
            _connectionManager = connectionManager;
            _validator = validator;
        }
        
        public async Task<OperationResult<List<TableSchema>>> GetSchemaAsync(SchemaConfig config, CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate configuration
                var validationResult = await ValidateSchemaConfigAsync(config);
                if (!((OperationResult)validationResult).Success)
                {
                    return OperationResult<List<TableSchema>>.Fail($"Configuration validation failed: {string.Join(", ", validationResult.Errors)}");
                }
                
                // Get database connection
                var connectionResult = await _connectionManager.GetConnectionAsync(
                    config.Provider!, config.ConnectionString!, cancellationToken);
                
                if (!((OperationResult)connectionResult).Success)
                {
                    return OperationResult<List<TableSchema>>.Fail($"Failed to connect to database: {connectionResult.ErrorMessage}");
                }
                
                try
                {
                    var provider = DatabaseProviderFactory.Create(config.Provider!);
                    var tableNames = StringUtilities.ParseTableNames(config.Tables);
                    
                    var tables = await provider.GetTablesAsync(connectionResult.Data!, tableNames);
                    
                    return OperationResult<List<TableSchema>>.Ok(tables);
                }
                finally
                {
                    await _connectionManager.ReturnConnectionAsync(connectionResult.Data!);
                }
            }
            catch (Exception ex)
            {
                return OperationResult<List<TableSchema>>.Fail(ex, "GetSchema");
            }
        }
        
        public async Task<OperationResult> GenerateScriptsAsync(List<TableSchema> tables, string outputPath, string providerName)
        {
            try
            {
                FileUtilities.EnsureDirectoryExists(outputPath);
                
                var provider = DatabaseProviderFactory.Create(providerName);
                
                foreach (var table in tables)
                {
                    var script = provider.GenerateTableCreationScript(table);
                    var fileName = FileUtilities.GetSafeFileName($"{table.Schema}_{table.Name}") + DatabaseConstants.FileExtensions.Sql;
                    var filePath = Path.Combine(outputPath, fileName);
                    
                    await File.WriteAllTextAsync(filePath, script);
                }
                
                return OperationResult.Ok();
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex, "GenerateScripts");
            }
        }
        
        public async Task<ValidationResult> ValidateSchemaConfigAsync(SchemaConfig config)
        {
            return await Task.FromResult(_validator.ValidateSchemaConfig(config));
        }
    }
}