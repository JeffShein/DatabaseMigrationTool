using DatabaseMigrationTool.Constants;
using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Providers;
using DatabaseMigrationTool.Utilities;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;

namespace DatabaseMigrationTool.Services
{
    public class ExportService : IExportService
    {
        private readonly IConnectionManager _connectionManager;
        private readonly IConfigurationValidator _validator;
        private readonly ILogger<ExportService> _logger;
        
        public ExportService(
            IConnectionManager connectionManager, 
            IConfigurationValidator validator,
            ILogger<ExportService> logger)
        {
            _connectionManager = connectionManager;
            _validator = validator;
            _logger = logger;
        }
        
        public async Task<ExportResult> ExportAsync(ExportConfig config, IProgress<ProgressInfo>? progress = null, CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            
            _logger.LogInformation("Starting export operation for provider {Provider} to path {OutputPath}", 
                config.Provider, config.OutputPath);
            
            try
            {
                // Validate configuration
                _logger.LogDebug("Validating export configuration");
                var validationResult = await ValidateExportConfigAsync(config);
                if (!validationResult.Success)
                {
                    _logger.LogError("Export configuration validation failed: {Errors}", 
                        string.Join(", ", validationResult.Errors));
                    return ExportResult.Fail($"Configuration validation failed: {string.Join(", ", validationResult.Errors)}");
                }
                
                progress?.Report(new ProgressInfo 
                { 
                    Message = DatabaseConstants.ProgressMessages.StartingExport, 
                    IsIndeterminate = true 
                });
                
                // Get database connection
                _logger.LogDebug("Establishing connection to {Provider} database", config.Provider);
                var connectionResult = await _connectionManager.GetConnectionAsync(
                    config.Provider!, config.ConnectionString!, cancellationToken);
                
                if (!((OperationResult)connectionResult).Success)
                {
                    _logger.LogError("Failed to connect to database: {ErrorMessage}", connectionResult.ErrorMessage);
                    return ExportResult.Fail($"Failed to connect to database: {connectionResult.ErrorMessage}");
                }
                
                try
                {
                    // Create provider and export options
                    var provider = DatabaseProviderFactory.Create(config.Provider!);
                    var exportOptions = CreateExportOptions(config);
                    
                    // Check for existing export and handle overwrite
                    var overwriteCheck = await HandleExportOverwriteAsync(config, progress);
                    if (!overwriteCheck.Success)
                    {
                        return ExportResult.Fail(overwriteCheck.ErrorMessage ?? "Export overwrite check failed");
                    }
                    
                    // Perform the export
                    var exporter = new DatabaseExporter(provider, connectionResult.Data!, exportOptions);
                    
                    // Set up progress reporting
                    if (progress != null)
                    {
                        exporter.SetProgressReporter(progressInfo => progress.Report(progressInfo));
                    }
                    
                    await exporter.ExportAsync(config.OutputPath!);
                    
                    // Calculate results
                    var outputSize = FileUtilities.GetDirectorySize(config.OutputPath!);
                    
                    return ExportResult.Create(
                        tablesExported: exportOptions.Tables?.Count ?? 0,
                        totalRows: 0, // Would need to track this during export
                        totalBytes: outputSize,
                        duration: stopwatch.Elapsed,
                        outputPath: config.OutputPath!
                    );
                }
                finally
                {
                    await _connectionManager.ReturnConnectionAsync(connectionResult.Data!);
                }
            }
            catch (OperationCanceledException)
            {
                return ExportResult.Fail("Export operation was cancelled");
            }
            catch (Exception ex)
            {
                return ExportResult.Fail(ex, "Export");
            }
        }
        
        public async Task<ValidationResult> ValidateExportConfigAsync(ExportConfig config)
        {
            return await Task.FromResult(_validator.ValidateExportConfig(config));
        }
        
        public async Task<OperationResult<List<string>>> GetAvailableTablesAsync(string providerName, string connectionString, CancellationToken cancellationToken = default)
        {
            try
            {
                var connectionResult = await _connectionManager.GetConnectionAsync(providerName, connectionString, cancellationToken);
                if (!((OperationResult)connectionResult).Success)
                {
                    return OperationResult<List<string>>.Fail($"Failed to connect: {connectionResult.ErrorMessage}");
                }
                
                try
                {
                    var provider = DatabaseProviderFactory.Create(providerName);
                    var tables = await provider.GetTablesAsync(connectionResult.Data!);
                    var tableNames = tables.Select(t => t.FullName).ToList();
                    
                    return OperationResult<List<string>>.Ok(tableNames);
                }
                finally
                {
                    await _connectionManager.ReturnConnectionAsync(connectionResult.Data!);
                }
            }
            catch (Exception ex)
            {
                return OperationResult<List<string>>.Fail(ex, "GetAvailableTables");
            }
        }
        
        private static ExportOptions CreateExportOptions(ExportConfig config)
        {
            Dictionary<string, string>? tableCriteria = null;
            
            if (!string.IsNullOrWhiteSpace(config.TableCriteriaFile) && File.Exists(config.TableCriteriaFile))
            {
                try
                {
                    var json = File.ReadAllText(config.TableCriteriaFile);
                    tableCriteria = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                }
                catch
                {
                    // Ignore criteria file errors for now
                }
            }
            
            return new ExportOptions
            {
                Tables = StringUtilities.ParseTableNames(config.Tables),
                TableCriteria = tableCriteria,
                BatchSize = config.BatchSize,
                OutputDirectory = config.OutputPath!,
                IncludeSchemaOnly = config.SchemaOnly
            };
        }
        
        private async Task<OperationResult> HandleExportOverwriteAsync(ExportConfig config, IProgress<ProgressInfo>? progress)
        {
            try
            {
                var tablesList = StringUtilities.ParseTableNames(config.Tables);
                var overwriteResult = await ExportOverwriteChecker.CheckForTableSpecificOverwriteAsync(
                    config.OutputPath!, tablesList);
                
                if (overwriteResult.HasExistingExport)
                {
                    progress?.Report(new ProgressInfo 
                    { 
                        Message = "Cleaning existing export files...", 
                        IsIndeterminate = true 
                    });
                    
                    // In a service, we might want to handle this differently
                    // For now, we'll delete existing files automatically
                    if (overwriteResult.ConflictingTables.Count > 0)
                    {
                        ExportOverwriteChecker.DeleteConflictingTables(config.OutputPath!, overwriteResult.ConflictingTables);
                    }
                    else
                    {
                        ExportOverwriteChecker.DeleteExistingExport(config.OutputPath!);
                    }
                }
                
                return OperationResult.Ok();
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex, "HandleExportOverwrite");
            }
        }
    }
}