using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Providers;
using DatabaseMigrationTool.Utilities;

namespace DatabaseMigrationTool.Services
{
    public class OperationRecoveryService
    {
        private readonly OperationStateManager _stateManager;
        
        public OperationRecoveryService()
        {
            _stateManager = new OperationStateManager();
        }

        public async Task<bool> ResumeExportOperationAsync(
            OperationState operation, 
            IDatabaseProvider provider, 
            string connectionString,
            ProgressReportHandler? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate operation can be resumed
                if (!operation.CanResume)
                {
                    ErrorHandler.HandleError(
                        new ErrorInfo
                        {
                            Message = "Operation cannot be resumed",
                            Context = $"Export Operation {operation.OperationId}",
                            Severity = ErrorSeverity.Error,
                            SuggestedAction = "Start a new export operation instead"
                        });
                    return false;
                }

                // Update operation status
                operation.Status = "InProgress";
                _stateManager.SaveOperationState(operation);

                // Create export options from stored configuration
                var exportOptions = operation.ExportOptions ?? new ExportOptions
                {
                    OutputDirectory = operation.OutputPath,
                    Tables = operation.RemainingTables,
                    BatchSize = 100000
                };

                // Resume from remaining tables
                using var connection = provider.CreateConnection(connectionString);
                var exporter = new DatabaseExporter(provider, connection, exportOptions);
                
                if (progressReporter != null)
                {
                    exporter.SetProgressReporter(progressReporter);
                }

                // Set up progress tracking to update operation state
                exporter.SetProgressReporter(progress =>
                {
                    // Update operation progress
                    operation.ProcessedRows = progress.Current;
                    operation.TotalRows = progress.Total;
                    operation.CurrentTable = progress.Message?.Contains("Exporting table") == true 
                        ? ExtractTableNameFromMessage(progress.Message) 
                        : operation.CurrentTable;

                    // Save state periodically
                    if (progress.Current % 10000 == 0)
                    {
                        _stateManager.SaveOperationState(operation);
                    }

                    // Forward to original progress reporter
                    progressReporter?.Invoke(progress);
                });

                // Resume export with remaining tables
                exportOptions.Tables = operation.RemainingTables;
                await exporter.ExportAsync(exportOptions.OutputDirectory ?? operation.OutputPath);

                // Mark operation as completed
                operation.Status = "Completed";
                operation.EndTime = DateTime.Now;
                operation.RemainingTables.Clear();
                _stateManager.SaveOperationState(operation);

                return true;
            }
            catch (OperationCanceledException)
            {
                operation.Status = "Cancelled";
                _stateManager.SaveOperationState(operation);
                return false;
            }
            catch (Exception ex)
            {
                var errorInfo = ErrorHandler.HandleError(ex, $"Resume Export Operation {operation.OperationId}");
                
                operation.Status = "Failed";
                operation.AddError(errorInfo.Message, operation.CurrentTable, "ResumeError");
                _stateManager.SaveOperationState(operation);

                // Try recovery if possible
                if (errorInfo.IsRecoverable)
                {
                    return await ErrorHandler.TryRecoverAsync(errorInfo, async () =>
                    {
                        return await ResumeExportOperationAsync(operation, provider, connectionString, progressReporter, cancellationToken);
                    });
                }

                return false;
            }
        }

        public async Task<bool> ResumeImportOperationAsync(
            OperationState operation,
            IDatabaseProvider provider,
            string connectionString,
            ProgressReportHandler? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate operation can be resumed
                if (!operation.CanResume)
                {
                    ErrorHandler.HandleError(
                        new ErrorInfo
                        {
                            Message = "Operation cannot be resumed",
                            Context = $"Import Operation {operation.OperationId}",
                            Severity = ErrorSeverity.Error,
                            SuggestedAction = "Start a new import operation instead"
                        });
                    return false;
                }

                // Update operation status
                operation.Status = "InProgress";
                _stateManager.SaveOperationState(operation);

                // Create import options from stored configuration
                var importOptions = operation.ImportOptions ?? new ImportOptions
                {
                    Tables = operation.RemainingTables,
                    BatchSize = 100000,
                    CreateSchema = true,
                    CreateForeignKeys = true,
                    ContinueOnError = true
                };

                // Resume with remaining tables
                using var connection = provider.CreateConnection(connectionString);
                var importer = new DatabaseImporter(provider, connection, importOptions);
                
                if (progressReporter != null)
                {
                    importer.SetProgressReporter(progressReporter);
                }

                // Set up progress tracking to update operation state
                importer.SetProgressReporter(progress =>
                {
                    // Update operation progress
                    operation.ProcessedRows = progress.Current;
                    operation.TotalRows = progress.Total;
                    operation.CurrentTable = progress.Message?.Contains("Importing table") == true 
                        ? ExtractTableNameFromMessage(progress.Message) 
                        : operation.CurrentTable;

                    // Save state periodically
                    if (progress.Current % 10000 == 0)
                    {
                        _stateManager.SaveOperationState(operation);
                    }

                    // Forward to original progress reporter
                    progressReporter?.Invoke(progress);
                });

                // Resume import with remaining tables
                importOptions.Tables = operation.RemainingTables;
                await importer.ImportAsync(operation.InputPath);

                // Mark operation as completed
                operation.Status = "Completed";
                operation.EndTime = DateTime.Now;
                operation.RemainingTables.Clear();
                _stateManager.SaveOperationState(operation);

                return true;
            }
            catch (OperationCanceledException)
            {
                operation.Status = "Cancelled";
                _stateManager.SaveOperationState(operation);
                return false;
            }
            catch (Exception ex)
            {
                var errorInfo = ErrorHandler.HandleError(ex, $"Resume Import Operation {operation.OperationId}");
                
                operation.Status = "Failed";
                operation.AddError(errorInfo.Message, operation.CurrentTable, "ResumeError");
                _stateManager.SaveOperationState(operation);

                // Try recovery if possible
                if (errorInfo.IsRecoverable)
                {
                    return await ErrorHandler.TryRecoverAsync(errorInfo, async () =>
                    {
                        return await ResumeImportOperationAsync(operation, provider, connectionString, progressReporter, cancellationToken);
                    });
                }

                return false;
            }
        }

        public List<OperationState> GetRecoverableOperations()
        {
            return _stateManager.GetRecoverableOperations();
        }

        public bool ValidateOperationForResume(OperationState operation)
        {
            try
            {
                // Check if operation can be resumed
                if (!operation.CanResume)
                {
                    return false;
                }

                // Validate paths exist
                if (operation.OperationType == "Export")
                {
                    if (!Directory.Exists(operation.OutputPath))
                    {
                        operation.AddError("Output directory no longer exists", null, "ValidationError");
                        _stateManager.SaveOperationState(operation);
                        return false;
                    }
                }
                else if (operation.OperationType == "Import")
                {
                    if (!Directory.Exists(operation.InputPath))
                    {
                        operation.AddError("Input directory no longer exists", null, "ValidationError");
                        _stateManager.SaveOperationState(operation);
                        return false;
                    }
                }

                // Check if there are tables left to process
                if (operation.RemainingTables.Count == 0)
                {
                    operation.Status = "Completed";
                    _stateManager.SaveOperationState(operation);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                ErrorHandler.HandleError(ex, $"Validate Operation {operation.OperationId}");
                return false;
            }
        }

        public Task<bool> RepairCorruptedOperationAsync(OperationState operation)
        {
            try
            {
                // Attempt to repair common issues
                bool repaired = false;

                // Fix missing paths
                if (string.IsNullOrEmpty(operation.OutputPath) && string.IsNullOrEmpty(operation.InputPath))
                {
                    // Cannot repair without path information
                    return Task.FromResult(false);
                }

                // Recreate directory if it was deleted
                if (operation.OperationType == "Export" && !string.IsNullOrEmpty(operation.OutputPath))
                {
                    if (!Directory.Exists(operation.OutputPath))
                    {
                        Directory.CreateDirectory(operation.OutputPath);
                        repaired = true;
                    }
                }

                // Reset stuck operations
                if (operation.Status == "InProgress" && operation.Duration.TotalHours > 24)
                {
                    operation.Status = "Failed";
                    operation.AddError("Operation timed out", null, "TimeoutError");
                    repaired = true;
                }

                // Clear invalid table states
                if (operation.RemainingTables.Count == 0 && operation.CompletedTables.Count == 0)
                {
                    // Try to reconstruct table list from available data
                    // This would require examining the export/import directory
                    // Implementation depends on specific file structure
                }

                if (repaired)
                {
                    _stateManager.SaveOperationState(operation);
                }

                return Task.FromResult(repaired);
            }
            catch (Exception ex)
            {
                ErrorHandler.HandleError(ex, $"Repair Operation {operation.OperationId}");
                return Task.FromResult(false);
            }
        }

        private static string? ExtractTableNameFromMessage(string message)
        {
            // Extract table name from progress messages like "Exporting table: TableName"
            if (message.Contains(":"))
            {
                var parts = message.Split(':', 2);
                if (parts.Length > 1)
                {
                    return parts[1].Trim();
                }
            }
            return null;
        }
    }
}