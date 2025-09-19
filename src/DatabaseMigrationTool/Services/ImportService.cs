using DatabaseMigrationTool.Constants;
using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Providers;
using DatabaseMigrationTool.Utilities;
using System.Diagnostics;

namespace DatabaseMigrationTool.Services
{
    public class ImportService : IImportService
    {
        private readonly IConnectionManager _connectionManager;
        private readonly IConfigurationValidator _validator;
        
        public ImportService(IConnectionManager connectionManager, IConfigurationValidator validator)
        {
            _connectionManager = connectionManager;
            _validator = validator;
        }
        
        public async Task<ImportResult> ImportAsync(ImportConfig config, IProgress<ProgressInfo>? progress = null, CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                // Validate configuration
                var validationResult = await ValidateImportConfigAsync(config);
                if (!((OperationResult)validationResult).Success)
                {
                    return ImportResult.Fail($"Configuration validation failed: {string.Join(", ", validationResult.Errors)}");
                }
                
                progress?.Report(new ProgressInfo 
                { 
                    Message = DatabaseConstants.ProgressMessages.StartingImport, 
                    IsIndeterminate = true 
                });
                
                // Get database connection
                var connectionResult = await _connectionManager.GetConnectionAsync(
                    config.Provider!, config.ConnectionString!, cancellationToken);
                
                if (!((OperationResult)connectionResult).Success)
                {
                    return ImportResult.Fail($"Failed to connect to database: {connectionResult.ErrorMessage}");
                }
                
                try
                {
                    // Create provider and import options
                    var provider = DatabaseProviderFactory.Create(config.Provider!);
                    var importOptions = CreateImportOptions(config);
                    
                    // Check for existing data and handle overwrite
                    var overwriteCheck = await HandleImportOverwriteAsync(config, provider, connectionResult.Data!, progress);
                    if (!overwriteCheck.Success)
                    {
                        return ImportResult.Fail(overwriteCheck.ErrorMessage ?? "Import overwrite check failed");
                    }
                    
                    // Perform the import
                    var importer = new DatabaseImporter(provider, connectionResult.Data!, importOptions);
                    
                    // Set up progress reporting
                    if (progress != null)
                    {
                        importer.SetProgressReporter(progressInfo => progress.Report(progressInfo));
                    }
                    
                    await importer.ImportAsync(config.InputPath!);
                    
                    return ImportResult.Create(
                        tablesImported: importOptions.Tables?.Count ?? 0,
                        rowsImported: 0, // Would need to track this during import
                        duration: stopwatch.Elapsed
                    );
                }
                finally
                {
                    await _connectionManager.ReturnConnectionAsync(connectionResult.Data!);
                }
            }
            catch (OperationCanceledException)
            {
                return ImportResult.Fail("Import operation was cancelled");
            }
            catch (Exception ex)
            {
                return ImportResult.Fail(ex, "Import");
            }
        }
        
        public async Task<ValidationResult> ValidateImportConfigAsync(ImportConfig config)
        {
            return await Task.FromResult(_validator.ValidateImportConfig(config));
        }
        
        public async Task<OperationResult<List<string>>> GetImportableTablesAsync(string importPath)
        {
            try
            {
                if (!MetadataManager.IsValidExport(importPath))
                {
                    return OperationResult<List<string>>.Fail("Invalid export directory");
                }
                
                var export = await MetadataManager.ReadMetadataAsync(importPath);
                var tableNames = export.Schemas?.Select(s => s.FullName).ToList() ?? new List<string>();
                
                return OperationResult<List<string>>.Ok(tableNames);
            }
            catch (Exception ex)
            {
                return OperationResult<List<string>>.Fail(ex, "GetImportableTables");
            }
        }
        
        private static ImportOptions CreateImportOptions(ImportConfig config)
        {
            return new ImportOptions
            {
                Tables = StringUtilities.ParseTableNames(config.Tables),
                BatchSize = config.BatchSize,
                CreateSchema = !config.NoCreateSchema,
                CreateForeignKeys = !config.NoCreateForeignKeys,
                SchemaOnly = config.SchemaOnly,
                ContinueOnError = config.ContinueOnError
            };
        }
        
        private async Task<OperationResult> HandleImportOverwriteAsync(
            ImportConfig config, 
            IDatabaseProvider provider, 
            System.Data.Common.DbConnection connection,
            IProgress<ProgressInfo>? progress)
        {
            try
            {
                progress?.Report(new ProgressInfo 
                { 
                    Message = "Checking for existing data in target database...", 
                    IsIndeterminate = true 
                });
                
                var importOptions = CreateImportOptions(config);
                var overwriteResult = await ImportOverwriteChecker.CheckForExistingDataAsync(
                    provider, config.ConnectionString!, config.InputPath!, importOptions);
                
                if (overwriteResult.HasConflictingData)
                {
                    // In a service, we might want to handle this differently
                    // For now, we'll assume the caller has already confirmed the overwrite
                    progress?.Report(new ProgressInfo 
                    { 
                        Message = "Proceeding with import - existing data may be overwritten", 
                        IsIndeterminate = true 
                    });
                }
                
                return OperationResult.Ok();
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex, "HandleImportOverwrite");
            }
        }
    }
}