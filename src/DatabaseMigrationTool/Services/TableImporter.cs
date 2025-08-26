using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Providers;
using MessagePack;
using MessagePack.Resolvers;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace DatabaseMigrationTool.Services
{
    /// <summary>
    /// A simplified, reliable implementation for importing table data
    /// This class handles both single files and batched files with a clean approach
    /// </summary>
    public class TableImporter
    {
        private readonly IDatabaseProvider _provider;
        private readonly DbConnection _connection;
        private readonly int _batchSize;
        private readonly Action<string> _logger;
        private ProgressReportHandler? _progressReporter;
        
        public TableImporter(IDatabaseProvider provider, DbConnection connection, int batchSize, Action<string> logger)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _batchSize = batchSize;
            _logger = logger ?? (s => { /* No output when logger is null */ });
        }
        
        public void SetProgressReporter(ProgressReportHandler progressReporter)
        {
            _progressReporter = progressReporter;
        }
        
        private void Log(string message) => _logger(message);
        
        /// <summary>
        /// Import data for a table from either a single file or multiple batch files
        /// </summary>
        /// <param name="fileProgressHandler">Optional callback to report the current file being processed</param>
        public async Task<ImportResult> ImportTableDataAsync(TableSchema tableSchema, string dataDir, Action<string>? fileProgressHandler = null)
        {
            var result = new ImportResult();
            
            try
            {
                // Add detailed logging to help diagnose issues
                Log($"### TableImporter starting import for {tableSchema.FullName} ###");
                Log($"### Using provider: {_provider.ProviderName}, BatchSize: {_batchSize} ###");
                
                // For SQL Server, always use 'dbo' schema for SQL operations
                string effectiveSchema = _provider.ProviderName.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) 
                    ? "dbo" 
                    : (tableSchema.Schema ?? "dbo");
                
                // But use original schema for file name searching (files were exported with original schema)
                string fileSearchSchema = tableSchema.Schema ?? "dbo";
                string tableFileName = $"{fileSearchSchema}_{tableSchema.Name}";
                
                if (_provider.ProviderName.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(tableSchema.Schema) && !tableSchema.Schema.Equals("dbo", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"### Mapping schema '{tableSchema.Schema}' to 'dbo' for SQL Server import (file search uses '{fileSearchSchema}') ###");
                }
                Log($"Starting import for table {tableSchema.FullName}");
                
                // First, find all applicable files
                var files = FindTableDataFiles(dataDir, tableFileName);
                
                if (files.Count == 0)
                {
                    Log($"No data files found for table {tableSchema.FullName}");
                    result.Message = "No data files found";
                    result.Success = true; // Not an error, just no data
                    result.RowsImported = 0;
                    return result;
                }
                
                Log($"Found {files.Count} data files for {tableSchema.FullName}");
                
                // Check if the table has identity columns - need special handling
                bool hasIdentity = tableSchema.Columns.Any(c => c.IsIdentity);
                bool identityInsertEnabled = false;
                
                if (hasIdentity && _provider.ProviderName.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                {
                    try 
                    {
                        await _connection.ExecuteScalarAsync($"SET IDENTITY_INSERT [{effectiveSchema}].[{tableSchema.Name}] ON");
                        identityInsertEnabled = true;
                        Log($"Identity insert enabled for {tableSchema.FullName}");
                    }
                    catch (Exception ex)
                    {
                        Log($"Warning: Failed to enable identity insert: {ex.Message}");
                    }
                }
                
                try
                {
                    int totalRowsImported = 0;
                    
                    // Sort files to ensure consistent order and for proper batch progress
                    files.Sort();
                    int totalFiles = files.Count;
                    
                    // Process each file
                    for (int fileIndex = 0; fileIndex < files.Count; fileIndex++)
                    {
                        string filePath = files[fileIndex];
                        string currentFileName = Path.GetFileName(filePath);
                        
                        // Calculate batch progress (0-100)
                        int batchProgress = (int)((double)(fileIndex) / totalFiles * 100);
                        
                        // Calculate batch-specific progress values
                        int batchCurrent = fileIndex + 1;
                        int batchTotal = totalFiles;
                        string progressInfo = $"Processing file {batchCurrent} of {batchTotal}";
                        
                        // Invoke special progress reporting for batch files
                        _logger($"[BATCH] {progressInfo} - {currentFileName}");
                        
                        // Use the specialized batch message format that our UI will recognize
                        if (_progressReporter != null)
                        {
                            _progressReporter(new ProgressInfo
                            {
                                Current = batchCurrent,
                                Total = batchTotal,
                                Message = $"[Batch] {progressInfo}",
                                IsIndeterminate = false
                            });
                        }
                        
                        fileProgressHandler?.Invoke(progressInfo);
                        
                        var fileResult = await ImportDataFileAsync(tableSchema, filePath);
                        if (fileResult.Success)
                        {
                            totalRowsImported += fileResult.RowsImported;
                            Log($"Successfully imported {fileResult.RowsImported} rows from {currentFileName} ({fileIndex + 1}/{totalFiles})");
                        }
                        else
                        {
                            Log($"Error importing {currentFileName}: {fileResult.Message}");
                            if (!string.IsNullOrEmpty(fileResult.Details))
                            {
                                Log($"Details: {fileResult.Details}");
                            }
                            result.Success = false;
                            result.Message = $"Failed to import file {currentFileName}: {fileResult.Message}";
                            return result;
                        }
                    }
                    
                    result.Success = true;
                    result.RowsImported = totalRowsImported;
                    result.Message = $"Successfully imported {totalRowsImported} rows";
                    
                    return result;
                }
                finally
                {
                    // Make sure to disable identity insert if enabled
                    if (identityInsertEnabled)
                    {
                        try 
                        {
                            await _connection.ExecuteScalarAsync($"SET IDENTITY_INSERT [{effectiveSchema}].[{tableSchema.Name}] OFF");
                            Log($"Identity insert disabled for {tableSchema.FullName}");
                        }
                        catch (Exception ex)
                        {
                            Log($"Warning: Failed to disable identity insert: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error importing table {tableSchema.FullName}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Log($"Inner error: {ex.InnerException.Message}");
                }
                result.Success = false;
                result.Message = ex.Message;
                result.Details = ex.ToString();
                return result;
            }
        }
        
        /// <summary>
        /// Finds all data files for a specific table
        /// </summary>
        private List<string> FindTableDataFiles(string dataDir, string tableFileName)
        {
            var result = new List<string>();
            
            // First look for a single file
            string singleFilePath = Path.Combine(dataDir, $"{tableFileName}.bin");
            if (File.Exists(singleFilePath))
            {
                result.Add(singleFilePath);
                return result;
            }
            
            // If no single file exists, look for batch files
            string pattern = $"{tableFileName}_batch*.bin";
            try
            {
                // Get all batch files and explicitly sort them by batch number
                var batchFiles = Directory.GetFiles(dataDir, pattern);
                
                // Use numeric sorting for batch numbers
                var sortedBatchFiles = batchFiles
                    .Select(f => new { 
                        FilePath = f, 
                        BatchNumber = ExtractBatchNumber(f) 
                    })
                    .OrderBy(f => f.BatchNumber)
                    .Select(f => f.FilePath)
                    .ToList();
                
                if (sortedBatchFiles.Count > 0)
                {
                    Log($"Found {sortedBatchFiles.Count} batch files. First: {Path.GetFileName(sortedBatchFiles.First())}, Last: {Path.GetFileName(sortedBatchFiles.Last())}");
                    result.AddRange(sortedBatchFiles);
                    return result;
                }
            }
            catch (Exception ex)
            {
                Log($"Error finding batch files: {ex.Message}");
            }
            
            return result;
        }
        
        /// <summary>
        /// Extracts the batch number from a batch file name
        /// </summary>
        private int ExtractBatchNumber(string filePath)
        {
            try
            {
                // Extract the number after _batch
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                int batchIndex = fileName.IndexOf("_batch");
                if (batchIndex >= 0)
                {
                    string batchPart = fileName.Substring(batchIndex + 6); // Skip "_batch"
                    if (int.TryParse(batchPart, out int batchNumber))
                    {
                        return batchNumber;
                    }
                }
            }
            catch
            {
                // Ignore errors and return default
            }
            return 0;
        }
        
        /// <summary>
        /// Imports data from a single file
        /// </summary>
        private async Task<ImportResult> ImportDataFileAsync(TableSchema tableSchema, string filePath)
        {
            var result = new ImportResult();
            
            try
            {
                bool isBoVouchersn = tableSchema.Name.Equals("bovouchersn", StringComparison.OrdinalIgnoreCase);
                string tableId = isBoVouchersn ? "[BOVOUCHERSN]" : tableSchema.FullName;
                
                Log($"{tableId} Processing file: {Path.GetFileName(filePath)}");
                
                if (isBoVouchersn)
                {
                    Log($"{tableId} SPECIAL HANDLING: Found bovouchersn table - will perform detailed diagnostics");
                }
                
                // Read file
                byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
                Log($"{tableId} Read {fileBytes.Length} bytes from file");
                
                if (fileBytes.Length < 10)
                {
                    Log("File is too small to be valid");
                    result.Success = false;
                    result.Message = "File is too small to be valid";
                    return result;
                }
                
                // Check if it's GZip compressed
                bool isGZip = fileBytes.Length > 2 && fileBytes[0] == 0x1F && fileBytes[1] == 0x8B;
                Log($"File is{(isGZip ? "" : " not")} GZip compressed");
                
                // Decompress if needed
                byte[] dataBytes;
                if (isGZip)
                {
                    using (var ms = new MemoryStream(fileBytes))
                    using (var gzipStream = new GZipStream(ms, CompressionMode.Decompress))
                    using (var decompressedStream = new MemoryStream())
                    {
                        await gzipStream.CopyToAsync(decompressedStream);
                        dataBytes = decompressedStream.ToArray();
                    }
                    Log($"Decompressed to {dataBytes.Length} bytes");
                }
                else
                {
                    dataBytes = fileBytes;
                }
                
                // Deserialize with MessagePack
                TableData tableData;
                try
                {
                    // Try with standard options
                    tableData = MessagePackSerializer.Deserialize<TableData>(
                        dataBytes, 
                        MessagePackSerializerOptions.Standard);
                    Log("Deserialized with standard options");
                }
                catch
                {
                    // Fall back to contractless resolver
                    try
                    {
                        var options = MessagePackSerializerOptions.Standard.WithResolver(
                            ContractlessStandardResolver.Instance);
                        tableData = MessagePackSerializer.Deserialize<TableData>(dataBytes, options);
                        Log("Deserialized with contractless resolver");
                    }
                    catch (Exception ex)
                    {
                        Log($"Failed to deserialize: {ex.Message}");
                        result.Success = false;
                        result.Message = "Failed to deserialize data";
                        result.Details = ex.ToString();
                        return result;
                    }
                }
                
                // Validate deserialized data
                if (tableData == null || tableData.Rows == null)
                {
                    Log("Deserialized data or rows collection is null");
                    result.Success = false;
                    result.Message = "Deserialized data is null or invalid";
                    return result;
                }
                
                string logPrefix = $"[{tableSchema.FullName}]";
                
                int rowCount = tableData.Rows.Count;
                Log($"{logPrefix} Found {rowCount} rows in file");
                
                if (rowCount == 0)
                {
                    // No error, just no data
                    result.Success = true;
                    result.RowsImported = 0;
                    return result;
                }
                
                // Check for empty/null rows
                int emptyRowCount = 0;
                
                for (int i = 0; i < tableData.Rows.Count; i++)
                {
                    var row = tableData.Rows[i];
                    bool isRowEmpty = row.Values.Count == 0;
                    bool hasOnlyNulls = row.Values.Count > 0 && row.Values.All(v => v.Value == null);
                    
                    if (isRowEmpty || hasOnlyNulls)
                    {
                        emptyRowCount++;
                        if (emptyRowCount <= 5) // Log details for first few empty rows
                        {
                            Log($"{logPrefix} WARNING: Row {i+1} is {(isRowEmpty ? "empty" : "all nulls")} with {row.Values.Count} columns");
                        }
                    }
                    
                    // Check for missing primary key values in the first few rows
                    if (i < 5)
                    {
                        // Find primary key columns
                        var pkColumns = tableSchema.Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();
                        if (pkColumns.Any())
                        {
                            foreach (var pkCol in pkColumns)
                            {
                                if (!row.Values.ContainsKey(pkCol) || row.Values[pkCol] == null)
                                {
                                    Log($"{logPrefix} WARNING: Row {i+1} has NULL or MISSING primary key '{pkCol}'");
                                }
                            }
                        }
                    }
                }
                
                if (emptyRowCount > 0)
                {
                    Log($"{logPrefix} WARNING: Found {emptyRowCount} empty or all-null rows in import data");
                }
                
                // Handle primary keys and empty rows before importing
                // Get the primary key columns from the schema
                var primaryKeyColumns = tableSchema.Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();
                bool hasPrimaryKeys = primaryKeyColumns.Any();
                
                // Filter empty rows and check for missing primary key values
                if (emptyRowCount > 0 || hasPrimaryKeys)
                {
                    int originalCount = tableData.Rows.Count;
                    
                    // Filter in-place to avoid creating a new list
                    for (int i = tableData.Rows.Count - 1; i >= 0; i--)
                    {
                        var row = tableData.Rows[i];
                        bool isRowEmpty = row.Values.Count == 0 || row.Values.All(v => v.Value == null);
                        
                        if (isRowEmpty)
                        {
                            // Remove empty rows
                            tableData.Rows.RemoveAt(i);
                            continue;
                        }
                        
                        // Check if row has all primary keys
                        if (hasPrimaryKeys)
                        {
                            foreach (var pk in primaryKeyColumns)
                            {
                                if (!row.Values.ContainsKey(pk) || row.Values[pk] == null)
                                {
                                    // Generate a unique value for missing primary key
                                    string newValue = "GEN_" + Guid.NewGuid().ToString();
                                    row.Values[pk] = newValue;
                                    Log($"{logPrefix} WARNING: Generated value '{newValue}' for missing primary key '{pk}'");
                                }
                            }
                        }
                    }
                    
                    int filteredCount = originalCount - tableData.Rows.Count;
                    if (filteredCount > 0)
                    {
                        Log($"{logPrefix} Filtered {filteredCount} empty rows, {tableData.Rows.Count} rows remaining");
                        Log($"Filtered {filteredCount} empty rows from {tableSchema.FullName}");
                    }
                }
                
                // Import the data
                await ImportRowsAsync(tableSchema, tableData.Rows);
                
                result.Success = true;
                result.RowsImported = rowCount;
                return result;
            }
            catch (Exception ex)
            {
                Log($"Error importing file: {ex.Message}");
                result.Success = false;
                result.Message = ex.Message;
                result.Details = ex.ToString();
                return result;
            }
        }
        
        /// <summary>
        /// Imports rows in batches using SQL inserts
        /// </summary>
        private async Task ImportRowsAsync(TableSchema tableSchema, List<RowData> rows)
        {
            // Get column names from the schema
            var columnNames = tableSchema.Columns.Select(c => c.Name).ToList();
            Log($"Table has {columnNames.Count} columns defined in schema");
            
            // Filter out empty rows (those with no values or only null values)
            int originalCount = rows.Count;
            rows = rows.Where(r => r.Values.Count > 0 && r.Values.Any(v => v.Value != null)).ToList();
            int filteredCount = originalCount - rows.Count;
            
            if (filteredCount > 0)
            {
                Log($"Filtered out {filteredCount} empty or all-null records from {tableSchema.FullName}");
            }
            
            // Log first row for debugging
            if (rows.Count > 0)
            {
                var firstRow = rows[0];
                Log($"First row has {firstRow.Values.Count} values");
                
                // Check for mismatches
                var missingColumns = columnNames.Except(firstRow.Values.Keys).ToList();
                var extraColumns = firstRow.Values.Keys.Except(columnNames).ToList();
                
                if (missingColumns.Any())
                {
                    Log($"Warning: Row is missing these schema columns: {string.Join(", ", missingColumns)}");
                }
                
                if (extraColumns.Any())
                {
                    Log($"Warning: Row has extra columns not in schema: {string.Join(", ", extraColumns)}");
                }
            }
            
            // Process in batches
            int batchSize = Math.Min(_batchSize, 100); // Use a moderate batch size for reliability
            Log($"Using batch size of {batchSize} for inserts");
            
            for (int i = 0; i < rows.Count; i += batchSize)
            {
                int currentBatchSize = Math.Min(batchSize, rows.Count - i);
                var batch = rows.GetRange(i, currentBatchSize);
                
                // Generate INSERT statements
                var insertStatements = batch.Select(row => 
                    GenerateInsertStatement(tableSchema, row)).ToList();
                
                // Filter out skipped/commented statements
                var validInsertStatements = insertStatements.Where(stmt => !stmt.StartsWith("--")).ToList();
                
                if (validInsertStatements.Count == 0)
                {
                    Log($"Batch {i / batchSize + 1}: All rows were skipped (empty/null data)");
                    continue;
                }
                
                // Execute as a batch
                string batchSql = string.Join(";\r\n", validInsertStatements);
                
                try
                {
                    int rowsAffected = await _connection.ExecuteNonQueryAsync(batchSql);
                    Log($"Batch {i / batchSize + 1}: {rowsAffected} rows affected");
                    
                    // Verify expected row count
                    if (rowsAffected != currentBatchSize)
                    {
                        Log($"Warning: Expected {currentBatchSize} rows to be affected, but got {rowsAffected}");
                    }
                }
                catch (Exception ex)
                {
                    // On batch failure, try one by one
                    Log($"Batch insert failed: {ex.Message}");
                    Log("Attempting row-by-row insert");
                    
                    int successCount = 0;
                    for (int j = 0; j < validInsertStatements.Count; j++)
                    {
                        try
                        {
                            // Skip commented-out statements
                            if (validInsertStatements[j].StartsWith("--"))
                            {
                                continue;
                            }
                            
                            await _connection.ExecuteNonQueryAsync(validInsertStatements[j]);
                            successCount++;
                        }
                        catch (Exception rowEx)
                        {
                            Log($"Error inserting row {i + j + 1}: {rowEx.Message}");
                        }
                    }
                    
                    Log($"Row-by-row insert: {successCount}/{currentBatchSize} succeeded");
                    
                    if (successCount == 0)
                    {
                        // If all individual inserts failed, throw exception
                        throw new Exception($"All inserts failed for batch {i / batchSize + 1}", ex);
                    }
                }
            }
        }
        
        /// <summary>
        /// Generates an INSERT statement for a single row
        /// </summary>
        private string GenerateInsertStatement(TableSchema tableSchema, RowData row)
        {
            // For SQL Server, always use 'dbo' schema regardless of export metadata
            string effectiveSchema = _provider.ProviderName.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) 
                ? "dbo" 
                : (tableSchema.Schema ?? "dbo");
                
            string tableName = $"[{effectiveSchema}].[{tableSchema.Name}]";
            
            // Safety check - ensure row has at least some non-null values
            if (row.Values.Count == 0 || row.Values.All(v => v.Value == null))
            {
                Log($"Warning: Skipping generation of INSERT statement for empty row in {tableSchema.FullName}");
                // Return a commented-out statement for logging purposes
                return $"-- Skipped empty row for {tableName}";
            }
            
            // Get columns and values
            var columns = new List<string>();
            var values = new List<string>();
            
            foreach (var pair in row.Values)
            {
                // Only include columns that exist in the schema
                if (!tableSchema.Columns.Any(c => c.Name == pair.Key))
                {
                    continue;
                }
                
                columns.Add($"[{pair.Key}]");
                
                // Format value based on type
                string formattedValue;
                if (pair.Value == null)
                {
                    formattedValue = "NULL";
                }
                else if (pair.Value is string str)
                {
                    // Escape quotes in strings
                    formattedValue = $"'{str.Replace("'", "''")}'";
                }
                else if (pair.Value is DateTime dt)
                {
                    // Format dates consistently
                    formattedValue = $"'{dt:yyyy-MM-dd HH:mm:ss.fff}'";
                }
                else if (pair.Value is bool b)
                {
                    // Convert bool to bit
                    formattedValue = b ? "1" : "0";
                }
                else if (pair.Value is byte[] bytes)
                {
                    // Convert binary data to hex
                    formattedValue = $"0x{BitConverter.ToString(bytes).Replace("-", "")}";
                }
                else
                {
                    // Use simple ToString for numbers and other types
                    formattedValue = pair.Value.ToString() ?? "NULL";
                }
                
                values.Add(formattedValue);
            }
            
            // Build SQL
            return $"INSERT INTO {tableName} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)})";
        }
    }
    
    /// <summary>
    /// Result of an import operation
    /// </summary>
    public class ImportResult
    {
        public bool Success { get; set; }
        public int RowsImported { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// Extension methods for database connections
    /// </summary>
    public static class DbConnectionExtensions
    {
        /// <summary>
        /// Executes a non-query SQL command and returns the number of rows affected
        /// Ensures the connection is open before executing
        /// </summary>
        public static async Task<int> ExecuteNonQueryAsync(this DbConnection connection, string sql)
        {
            // Make sure the connection is open
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
            }
            
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 300; // Set a longer timeout (5 minutes)
            return await command.ExecuteNonQueryAsync();
        }
        
        /// <summary>
        /// Executes a scalar SQL command and returns the result
        /// Ensures the connection is open before executing
        /// </summary>
        public static async Task<object?> ExecuteScalarAsync(this DbConnection connection, string sql)
        {
            // Make sure the connection is open
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
            }
            
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 300; // Set a longer timeout (5 minutes)
            return await command.ExecuteScalarAsync();
        }
    }
}