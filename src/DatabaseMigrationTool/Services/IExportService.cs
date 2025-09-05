using DatabaseMigrationTool.Models;

namespace DatabaseMigrationTool.Services
{
    public interface IExportService
    {
        Task<ExportResult> ExportAsync(ExportConfig config, IProgress<ProgressInfo>? progress = null, CancellationToken cancellationToken = default);
        Task<ValidationResult> ValidateExportConfigAsync(ExportConfig config);
        Task<OperationResult<List<string>>> GetAvailableTablesAsync(string providerName, string connectionString, CancellationToken cancellationToken = default);
    }
    
    public interface IImportService
    {
        Task<ImportResult> ImportAsync(ImportConfig config, IProgress<ProgressInfo>? progress = null, CancellationToken cancellationToken = default);
        Task<ValidationResult> ValidateImportConfigAsync(ImportConfig config);
        Task<OperationResult<List<string>>> GetImportableTablesAsync(string importPath);
    }
    
    public interface ISchemaService
    {
        Task<OperationResult<List<TableSchema>>> GetSchemaAsync(SchemaConfig config, CancellationToken cancellationToken = default);
        Task<OperationResult> GenerateScriptsAsync(List<TableSchema> tables, string outputPath, string providerName);
        Task<ValidationResult> ValidateSchemaConfigAsync(SchemaConfig config);
    }
}