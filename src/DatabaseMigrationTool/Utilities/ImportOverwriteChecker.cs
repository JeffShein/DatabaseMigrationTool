using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Providers;
using DatabaseMigrationTool.Services;
using Dapper;
using MessagePack;
using SharpCompress.Compressors.BZip2;
using System.Data.Common;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace DatabaseMigrationTool.Utilities
{
    public static class ImportOverwriteChecker
    {
        public static async Task<ImportOverwriteResult> CheckForExistingDataAsync(
            IDatabaseProvider provider, 
            DbConnection connection, 
            string inputPath,
            ImportOptions options)
        {
            var result = new ImportOverwriteResult { HasConflictingData = false };
            
            try
            {
                // Validate export directory first
                if (!MetadataManager.IsValidExport(inputPath))
                {
                    result.Message = $"Directory '{inputPath}' does not contain a valid export (missing export_manifest.json)";
                    return result;
                }
                
                // First, read the export metadata to see what tables will be imported
                var exportMetadata = await ReadExportMetadataAsync(inputPath);
                if (exportMetadata == null || exportMetadata.Schemas == null)
                {
                    result.Message = "Could not read export metadata - export may be corrupted";
                    return result;
                }
                
                // Filter tables if specified in options
                var tablesToImport = exportMetadata.Schemas.ToList();
                if (options.Tables != null && options.Tables.Any())
                {
                    tablesToImport = tablesToImport
                        .Where(t => options.Tables.Any(filterName => 
                            t.Name.Equals(filterName, StringComparison.OrdinalIgnoreCase) ||
                            t.FullName.Equals(filterName, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                }
                
                result.TablesToImport = tablesToImport.Select(t => t.FullName).ToList();
                
                if (tablesToImport.Count == 0)
                {
                    result.Message = options.Tables != null && options.Tables.Any() 
                        ? $"No tables found matching the specified filter: {string.Join(", ", options.Tables)}"
                        : "No tables found in export metadata";
                    return result;
                }
                
                // Check connection
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }
                
                // Check which tables already exist in target database
                var existingTables = await provider.GetTablesAsync(connection);
                var existingTableNames = existingTables.Select(t => t.FullName.ToLowerInvariant()).ToHashSet();
                
                // Analyze ALL tables that will be imported
                foreach (var tableToImport in tablesToImport)
                {
                    string tableName = tableToImport.FullName.ToLowerInvariant();
                    bool tableExists = existingTableNames.Contains(tableName);
                    
                    if (tableExists)
                    {
                        // Table exists - this is a conflict
                        result.HasConflictingData = true;
                        
                        // Try to get row count from existing table
                        int rowCount = 0;
                        try
                        {
                            // Try to count rows in existing table
                            rowCount = await GetTableRowCountAsync(provider, connection, tableToImport);
                        }
                        catch
                        {
                            rowCount = -1; // Unknown count
                        }
                        
                        var conflictInfo = new TableConflictInfo
                        {
                            TableName = tableToImport.FullName,
                            ExistingRowCount = rowCount,
                            WillCreateSchema = options.CreateSchema,
                            WillImportData = !options.SchemaOnly,
                            ConflictType = DetermineConflictType(options, rowCount)
                        };
                        
                        result.ConflictingTables.Add(conflictInfo);
                    }
                    // Note: We don't need to do anything special for new tables - they're just counted
                }
                
                // Count tables that don't exist yet (will be created)
                result.NewTablesCount = tablesToImport.Count - result.ConflictingTables.Count;
                
                return result;
            }
            catch (Exception ex)
            {
                result.Message = $"Error checking for existing data: {ex.Message}";
                return result;
            }
        }
        
        private static async Task<DatabaseExport?> ReadExportMetadataAsync(string inputPath)
        {
            try
            {
                // Use the new MetadataManager to read the granular metadata
                var export = await MetadataManager.ReadMetadataAsync(inputPath);
                return export;
            }
            catch (Exception ex)
            {
                // If the new format fails, return null to indicate we couldn't read metadata
                System.Diagnostics.Debug.WriteLine($"Failed to read export metadata: {ex.Message}");
                return null;
            }
        }
        
        private static async Task<int> GetTableRowCountAsync(IDatabaseProvider provider, DbConnection connection, TableSchema table)
        {
            try
            {
                string countSql;
                
                if (provider.ProviderName.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                {
                    countSql = $"SELECT COUNT(*) FROM [{table.Schema ?? "dbo"}].[{table.Name}] WITH (NOLOCK)";
                }
                else if (provider.ProviderName.Equals("MySQL", StringComparison.OrdinalIgnoreCase))
                {
                    countSql = $"SELECT COUNT(*) FROM `{table.Schema ?? ""}`.`{table.Name}`";
                }
                else if (provider.ProviderName.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
                {
                    countSql = $"SELECT COUNT(*) FROM \"{table.Schema ?? "public"}\".\"{table.Name}\"";
                }
                else if (provider.ProviderName.Equals("Firebird", StringComparison.OrdinalIgnoreCase))
                {
                    countSql = $"SELECT COUNT(*) FROM \"{table.Name}\"";
                }
                else
                {
                    // Generic fallback
                    countSql = $"SELECT COUNT(*) FROM {table.FullName}";
                }
                
                using var command = connection.CreateCommand();
                command.CommandText = countSql;
                command.CommandTimeout = 30; // 30 second timeout
                
                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch
            {
                return -1; // Unable to determine count
            }
        }
        
        private static ConflictType DetermineConflictType(ImportOptions options, int existingRowCount)
        {
            if (options.CreateSchema && options.SchemaOnly)
            {
                return ConflictType.SchemaConflict; // Will try to create existing table
            }
            else if (options.CreateSchema && !options.SchemaOnly)
            {
                return ConflictType.SchemaAndDataConflict; // Will try to create table AND import data
            }
            else if (!options.CreateSchema && !options.SchemaOnly && existingRowCount > 0)
            {
                return ConflictType.DataAppend; // Will add data to existing table with data
            }
            else if (!options.CreateSchema && !options.SchemaOnly)
            {
                return ConflictType.DataImport; // Will add data to existing empty table
            }
            
            return ConflictType.None;
        }
    }
    
    public class ImportOverwriteResult
    {
        public bool HasConflictingData { get; set; }
        public List<TableConflictInfo> ConflictingTables { get; set; } = new List<TableConflictInfo>();
        public List<string> TablesToImport { get; set; } = new List<string>();
        public int NewTablesCount { get; set; }
        public string? Message { get; set; }
        
        public string GetSummaryText()
        {
            if (!HasConflictingData && NewTablesCount == 0)
            {
                return "No tables to import or conflicts detected.";
            }
            
            if (!HasConflictingData)
            {
                return $"Will create {NewTablesCount} new table(s). No conflicts detected.";
            }
            
            var summary = new List<string>();
            
            if (NewTablesCount > 0)
            {
                summary.Add($"{NewTablesCount} new table(s) will be created");
            }
            
            var schemaConflicts = ConflictingTables.Count(t => t.ConflictType == ConflictType.SchemaConflict || t.ConflictType == ConflictType.SchemaAndDataConflict);
            var dataConflicts = ConflictingTables.Count(t => t.ConflictType == ConflictType.DataAppend || t.ConflictType == ConflictType.DataImport);
            
            if (schemaConflicts > 0)
            {
                summary.Add($"{schemaConflicts} table(s) already exist and will cause schema conflicts");
            }
            
            if (dataConflicts > 0)
            {
                summary.Add($"{dataConflicts} table(s) will have data added/appended");
            }
            
            return string.Join(", ", summary) + ".";
        }
    }
    
    public class TableConflictInfo
    {
        public string TableName { get; set; } = string.Empty;
        public int ExistingRowCount { get; set; }
        public bool WillCreateSchema { get; set; }
        public bool WillImportData { get; set; }
        public ConflictType ConflictType { get; set; }
        
        public string GetDescription()
        {
            return ConflictType switch
            {
                ConflictType.SchemaConflict => $"Table exists - schema creation will fail",
                ConflictType.SchemaAndDataConflict => $"Table exists - schema creation will fail, data import not possible",
                ConflictType.DataAppend => ExistingRowCount > 0 ? 
                    $"Table has {ExistingRowCount:N0} rows - data will be appended" : 
                    "Table exists but is empty - data will be imported",
                ConflictType.DataImport => "Table exists - data will be imported",
                _ => "No conflict"
            };
        }
    }
    
    public enum ConflictType
    {
        None,
        SchemaConflict,        // Table exists, trying to create schema
        DataAppend,            // Table exists with data, will append more
        DataImport,            // Table exists, will import data
        SchemaAndDataConflict  // Table exists, trying to create schema AND import data
    }
}