using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Providers;
using MessagePack;
using MessagePack.Resolvers;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;
using System.Data.Common;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace DatabaseMigrationTool.Services
{
    public class DatabaseExporter
    {
        private readonly IDatabaseProvider _provider;
        private readonly DbConnection _connection;
        private readonly ExportOptions _options;
        private Action<string>? _logger;
        private ProgressReportHandler? _progressReporter;

        public DatabaseExporter(IDatabaseProvider provider, DbConnection connection, ExportOptions options)
        {
            _provider = provider;
            _connection = connection;
            _options = options;
            _logger = null;
        }
        
        public void SetLogger(Action<string> logger)
        {
            _logger = logger;
        }
        
        public void SetProgressReporter(ProgressReportHandler progressReporter)
        {
            _progressReporter = progressReporter;
        }
        
        private void Log(string message)
        {
            _logger?.Invoke(message);
            
            // Also log to error log file for easier troubleshooting
            try 
            {
                if (!string.IsNullOrEmpty(outputPath))
                {
                    string logPath = Path.Combine(outputPath, "export_log.txt");
                    File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Ignore logging errors
            }
        }

        private void ReportProgress(int current, int total, string message, bool isIndeterminate = false)
        {
            _progressReporter?.Invoke(new ProgressInfo
            {
                Current = current,
                Total = total,
                Message = message,
                IsIndeterminate = isIndeterminate
            });
        }

        private string? outputPath;
        
        public async Task ExportAsync(string outputPath)
        {
            // Store output path for error reporting
            this.outputPath = outputPath;
            
            // Report initial progress
            ReportProgress(0, 100, "Starting database export...", true);
            
            // Discover tables
            List<TableSchema> tables;
            
            if (_options.Tables != null && _options.Tables.Any())
            {
                // For filtering by table name, we first get all tables and filter in memory to ensure accuracy
                if (_provider.ProviderName.Equals("Firebird", StringComparison.OrdinalIgnoreCase))
                {
                    // For Firebird, we get all tables first and then filter in memory to avoid SQL issues
                    Log("Using memory filtering for Firebird tables");
                    List<TableSchema> allTables = await _provider.GetTablesAsync(_connection);
                    
                    // Convert table names to uppercase for Firebird case-insensitive comparison
                    HashSet<string> requestedTablesUpper = new HashSet<string>(
                        _options.Tables.Select(t => t.Trim().ToUpperInvariant()));
                    
                    // Filter tables in memory
                    tables = allTables.Where(t => requestedTablesUpper.Contains(t.Name)).ToList();
                    
                    Log($"Memory filtering result: Found {tables.Count} of {_options.Tables.Count} requested tables");
                }
                else
                {
                    // For other providers, use the provider's filtering
                    tables = await _provider.GetTablesAsync(_connection, _options.Tables);
                }
                
                // Validate that all requested tables were found
                var foundTableNames = tables.Select(t => t.Name.ToUpperInvariant()).ToHashSet();
                var missingTables = _options.Tables.Where(t => !foundTableNames.Contains(t.Trim().ToUpperInvariant())).ToList();
                
                if (missingTables.Any())
                {
                    string errorMessage = $"The following requested tables were not found in the database: {string.Join(", ", missingTables)}";
                    throw new InvalidOperationException(errorMessage);
                }
            }
            else
            {
                tables = await _provider.GetTablesAsync(_connection);
            }
            
            // Write metadata
            Directory.CreateDirectory(outputPath);
            var databaseName = _connection.Database ?? "Unknown";
            
            await Utilities.MetadataManager.WriteMetadataAsync(
                outputPath, 
                tables ?? new List<TableSchema>(), 
                databaseName, 
                _options.IncludeSchemaOnly);
                
            ReportProgress(5, 100, "Metadata written");
            
            // Report progress
            ReportProgress(10, 100, $"Exported {tables?.Count ?? 0} table schemas to metadata file");
            
            // Write table data
            var dataDir = Path.Combine(outputPath, "data");
            Directory.CreateDirectory(dataDir);
            
            int tableCount = 0;
            int totalTables = tables?.Count ?? 0;
            foreach (var table in tables ?? Enumerable.Empty<TableSchema>())
            {
                tableCount++;
                string? whereClause = null;
                if (_options.TableCriteria != null && _options.TableCriteria.TryGetValue(table.Name, out var criteria))
                {
                    whereClause = criteria;
                    Log($"Applying criteria for table {table.Name}: {whereClause}");
                }
                
                string safeTableName = table?.FullName ?? $"table_{tableCount}";
                
                // Update progress
                int progressValue = 10 + (int)((double)tableCount / totalTables * 90);
                ReportProgress(progressValue, 100, $"Exporting table {tableCount} of {totalTables}: {safeTableName}");
                
                if (table != null)
                {
                    try 
                    {
                    // Calculate expected output file path for status message
                    string fileName = $"{table.Schema ?? "dbo"}_{table.Name}.bin";
                    string outputFile = Path.Combine(dataDir, fileName);
                    string fileInfoText = $" [File: {fileName}]";
                    
                    // Add file info to progress
                    ReportProgress(progressValue, 100, $"Exporting table {tableCount} of {totalTables}: {safeTableName}{fileInfoText}");
                    
                    // Create a file progress handler to update the status with the current file being processed
                    Action<string> fileProgressHandler = (currentFile) => {
                        string currentFileInfo = $" [File: {currentFile}]";
                        ReportProgress(progressValue, 100, $"Exporting table {tableCount} of {totalTables}: {safeTableName}{currentFileInfo}");
                    };
                    
                    await ExportTableDataAsync(table, dataDir, whereClause, fileProgressHandler);
                    
                    // Update progress after each table with file size
                    progressValue = 10 + (int)((double)tableCount / totalTables * 90);
                    
                    // Check if single file or batches were created
                    if (File.Exists(outputFile))
                    {
                        var fileInfo = new FileInfo(outputFile);
                        fileInfoText = $" [File: {fileName}, Size: {fileInfo.Length / 1024:N0} KB]";
                    }
                    else
                    {
                        string batchPattern = $"{table.Schema ?? "dbo"}_{table.Name}_batch*.bin";
                        var batchFiles = Directory.GetFiles(dataDir, batchPattern);
                        if (batchFiles.Length > 0)
                        {
                            string firstFile = Path.GetFileName(batchFiles[0]);
                            if (batchFiles.Length == 1)
                            {
                                fileInfoText = $" [File: {firstFile}]";
                            }
                            else
                            {
                                fileInfoText = $" [File: {firstFile} and {batchFiles.Length - 1} more]";
                            }
                        }
                    }
                    
                    ReportProgress(progressValue, 100, $"Exported table {tableCount} of {totalTables}: {table.FullName}{fileInfoText}");
                    }
                    catch (Exception tableEx)
                    {
                        // Handle any exceptions that might have been missed by inner handlers
                        Log($"Error exporting table {table.FullName}: {tableEx.Message}");
                        
                        // Create error files
                        if (!string.IsNullOrEmpty(outputPath))
                        {
                            // Log to skipped tables summary
                            string summaryFile = Path.Combine(outputPath, "export_skipped_tables.txt");
                            using (StreamWriter sw = File.AppendText(summaryFile))
                            {
                                await sw.WriteLineAsync($"{table.FullName}: Error during export - {tableEx.Message}");
                            }
                            
                            // Create error file for this specific table
                            string errorFile = Path.Combine(dataDir, $"{table.Schema ?? "dbo"}_{table.Name}.error");
                            string errorDetails = $"{{\"Error\":\"Failed to export table\",\"Table\":\"{table.Name}\",\"Message\":\"{tableEx.Message.Replace('"', '\'')}\",\"Provider\":\"{_provider.ProviderName}\"}}";
                            await File.WriteAllTextAsync(errorFile, errorDetails);
                        }
                        
                        ReportProgress(progressValue, 100, $"Error exporting table {tableCount} of {totalTables}: {table.FullName}");
                    }
                }
            }
            
            // Report final progress
            ReportProgress(100, 100, "Database export completed successfully!");
        }

        // Improved export method with batched processing for large tables
        private async Task ExportTableDataAsync(TableSchema? tableSchema, string dataDir, string? whereClause = null, Action<string>? fileProgressHandler = null)
        {
            try
            {
                // Early exit if tableSchema is null
                if (tableSchema == null)
                {
                    return;
                }
                
                // For Firebird, add special handling for permission issues
                if (_provider.ProviderName.Equals("Firebird", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"Starting export of Firebird table {tableSchema.FullName} with special permission handling");
                }
                    
                // Set up file path
                string fileName = $"{tableSchema.Schema ?? "dbo"}_{tableSchema.Name}.bin";
                string filePath = Path.Combine(dataDir, fileName);
                
                // Update progress with current file name
                fileProgressHandler?.Invoke(fileName);
                    
                // For Firebird special case: add dialect and isolation level parameters
                string connectionString = _connection.ConnectionString;
                DbConnection? directConnection = null;
                
                // For Firebird tables, use consistent approach with special parameters
                if (_provider.ProviderName.Equals("Firebird", StringComparison.OrdinalIgnoreCase))
                {
                    // Add consistent dialect and isolation level parameters
                    Log("Adding special parameters for Firebird table");
                    connectionString = connectionString + ";Dialect=3;IsolationLevel=ReadCommitted";
                }
                
                // Create connection with potentially modified connection string
                directConnection = _provider.CreateConnection(connectionString);
                
                // Create a fresh connection for this specific operation
                // This ensures we're using exactly the same approach as the schema view
                using (directConnection)
                {
                    // Log the connection string for debugging
                    Log($"Using connection string (masked): {System.Text.RegularExpressions.Regex.Replace(directConnection.ConnectionString, @"Password=[^;]*", "Password=******", System.Text.RegularExpressions.RegexOptions.IgnoreCase)}");
                    
                    await directConnection.OpenAsync();
                        
                    // First, determine if we need to count rows
                    int rowCount = 0;
                    
                    // For Firebird tables, try to get actual count but handle permission errors gracefully
                    if (_provider.ProviderName.Equals("Firebird", StringComparison.OrdinalIgnoreCase))
                    {
                        // Try to get actual row count for Firebird
                        string countSql = $"SELECT COUNT(*) FROM \"{tableSchema.Name}\"";
                        if (!string.IsNullOrEmpty(whereClause))
                        {
                            countSql += $" WHERE {whereClause}";
                        }
                        
                        Log($"Attempting to count rows in Firebird table {tableSchema.FullName}");
                        Log($"Firebird count SQL: {countSql}");
                        try
                        {
                            using (var countCommand = directConnection.CreateCommand())
                            {
                                countCommand.CommandText = countSql;
                                countCommand.CommandTimeout = 300;
                                countCommand.CommandType = System.Data.CommandType.Text;
                                
                                // Use a transaction for consistency
                                using (var countTransaction = directConnection.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                                {
                                    countCommand.Transaction = countTransaction;
                                    
                                    var countResult = await countCommand.ExecuteScalarAsync();
                                    if (countResult != null && countResult != DBNull.Value)
                                    {
                                        rowCount = Convert.ToInt32(countResult);
                                        Log($"Firebird table {tableSchema.FullName} contains {rowCount} rows");
                                    }
                                    
                                    countTransaction.Commit();
                                }
                            }
                        }
                        catch (Exception countEx)
                        {
                            // If count fails due to permissions, use a conservative approach
                            Log($"Unable to count rows in Firebird table {tableSchema.FullName}: {countEx.Message}");
                            Log($"Will export all data and determine row count during processing");
                            rowCount = _options.BatchSize; // Use batch size as initial estimate, will adjust during export
                        }
                    }
                    else
                    {
                        // For other database types, use standard count approach
                        string countSql = $"SELECT COUNT(*) FROM [{tableSchema.Schema ?? "dbo"}].[{tableSchema.Name}] WITH (NOLOCK)";
                        if (!string.IsNullOrEmpty(whereClause))
                        {
                            countSql += $" WHERE {whereClause}";
                        }
                        
                        // Try to get row count, handle permission errors gracefully
                        Log($"Executing count query for table {tableSchema.FullName}");
                        Log($"Count SQL: {countSql}");
                        try
                        {
                            using (var countCommand = directConnection.CreateCommand())
                            {
                                countCommand.CommandText = countSql;
                                countCommand.CommandTimeout = 60; // Set reasonable timeout
                                
                                var countResult = await countCommand.ExecuteScalarAsync();
                                rowCount = Convert.ToInt32(countResult);
                            }
                        }
                        catch (Exception countEx)
                        {
                            // Handle any errors with count query
                            ReportProgress(0, 1, $"Error counting rows for {tableSchema.FullName}: {countEx.Message} - Using estimated count", true);
                            Log($"Error counting rows in table {tableSchema.FullName}: {countEx.Message}");
                            rowCount = 0; // Default to empty for safety
                        }
                    }
                        
                    // Now read and export all the data
                    if (rowCount == 0)
                    {
                        // Create empty data file
                        using (var fileStream = File.Create(filePath))
                        using (var gzipStream = new GZipStream(fileStream, System.IO.Compression.CompressionMode.Compress, true))
                        {
                            var emptyData = new TableData
                            {
                                Schema = tableSchema,
                                Rows = new List<RowData>(),
                                TotalCount = 0,
                                BatchNumber = 0,
                                TotalBatches = 1,
                                IsLastBatch = true
                            };
                            
                            // Use standard options for consistency
                            var options = MessagePackSerializerOptions.Standard;
                                
                            await Task.Run(() => MessagePackSerializer.Serialize(gzipStream, emptyData, options));
                            await gzipStream.FlushAsync();
                        }
                    }
                    else 
                    {
                        // Check if table is large enough to require batching
                        bool useBatching = rowCount > _options.BatchSize;
                        int batchSize = _options.BatchSize;
                        int totalBatches = (int)Math.Ceiling((double)rowCount / batchSize);
                        
                        // For multiple batches, use numbered files
                        string batchFilePathPattern = useBatching ? 
                            Path.Combine(dataDir, $"{tableSchema.Schema ?? "dbo"}_{tableSchema.Name}_batch{{0}}.bin") :
                            filePath;
                        
                        // Always add a .info file for large tables to indicate batching
                        if (useBatching)
                        {
                            string infoFilePath = Path.Combine(dataDir, $"{tableSchema.Schema ?? "dbo"}_{tableSchema.Name}.info");
                            await File.WriteAllTextAsync(infoFilePath, $"{{\"TotalRows\":{rowCount},\"BatchSize\":{batchSize},\"TotalBatches\":{totalBatches}}}");
                        }
                        
                        // Process each batch
                        for (int batchNumber = 0; batchNumber < totalBatches; batchNumber++)
                        {
                            string currentFilePath = useBatching ? 
                                string.Format(batchFilePathPattern, batchNumber) : 
                                batchFilePathPattern;
                            
                            // Update progress with current batch file name
                            string currentFileName = Path.GetFileName(currentFilePath);
                            fileProgressHandler?.Invoke(currentFileName);
                                
                            int offset = batchNumber * batchSize;
                            int currentBatchSize = Math.Min(batchSize, rowCount - offset);
                            bool isLastBatch = batchNumber == totalBatches - 1;
                            
                            // Report table-specific progress
                            ReportProgress(
                                batchNumber, 
                                totalBatches, 
                                $"Table {tableSchema.FullName}: Processing batch {batchNumber+1} of {totalBatches}, rows {offset+1}-{offset+currentBatchSize}"
                            );
                            
                            // Check if this table has a primary key for proper ordering
                            var pkColumns = tableSchema.Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();
                            string orderBy = string.Empty;
                            
                            if (pkColumns.Any())
                            {
                                // Use primary keys for ordering
                                orderBy = $"ORDER BY {string.Join(", ", pkColumns.Select(c => $"[{c}]"))}";
                            }
                            
                            // Execute query with batching - create provider-specific SQL
                            string dataSql;
                            
                            // Use provider-specific SQL for data query
                            if (_provider.ProviderName.Equals("Firebird", StringComparison.OrdinalIgnoreCase))
                            {
                                // For Firebird, use the EXECUTE STATEMENT approach to bypass permissions issues
                                // This is similar to how the schema view accesses tables
                                
                                // Get column names for the select clause to avoid SELECT *
                                var columnList = tableSchema.Columns
                                    .Select(c => $"\"{c.Name}\"")
                                    .ToList();
                                
                                string innerSql;
                                if (columnList.Count == 0)
                                {
                                    // Fallback if no columns are defined
                                    innerSql = $"SELECT * FROM \"{tableSchema.Name}\"";
                                }
                                else
                                {
                                    // Use explicit column list
                                    innerSql = $"SELECT {string.Join(", ", columnList)} FROM \"{tableSchema.Name}\"";
                                }
                                
                                // Add WHERE clause if needed
                                if (!string.IsNullOrEmpty(whereClause))
                                {
                                    innerSql += $" WHERE {whereClause}";
                                }
                                
                                // Prepare to create appropriate SQL for this database type
                                // For Firebird tables we'll use special FIRST syntax to limit result sets
                                string tableNameOnly = tableSchema.Name.Trim().Replace("\"", "");
                                
                                // For all Firebird tables, use the FIRST syntax with batch size parameter
                                if (_provider.ProviderName.Equals("Firebird", StringComparison.OrdinalIgnoreCase))
                                {
                                    // For Firebird tables, use FIRST clause with batch size parameter
                                    dataSql = $"SELECT FIRST {_options.BatchSize} * FROM {tableNameOnly}";
                                    Log($"Using simplified FIRST query with batch size {_options.BatchSize}: {dataSql}");
                                }
                                else
                                {
                                    // For other tables, use a simple direct query
                                    dataSql = $"SELECT * FROM \"{tableNameOnly}\"";
                                    Log($"Using simple direct query for table {tableSchema.FullName}: {dataSql}");
                                }
                                
                                // Add WHERE clause if needed
                                if (!string.IsNullOrEmpty(whereClause))
                                {
                                    dataSql += $" WHERE {whereClause}";
                                }
                                
                                // For Firebird, we'll limit results in memory rather than using SQL pagination
                                // This is safer and more compatible with different Firebird versions
                                
                                // Log the SQL for debugging
                                Log($"Firebird table access query: {dataSql}");
                            }
                            else
                            {
                                // Standard SQL Server syntax with NOCOUNT
                                dataSql = $"SET NOCOUNT ON; SELECT * FROM [{tableSchema.Schema ?? "dbo"}].[{tableSchema.Name}] WITH (NOLOCK)";
                                
                                // Add WHERE clause if needed
                                if (!string.IsNullOrEmpty(whereClause))
                                {
                                    dataSql += $" WHERE {whereClause}";
                                }
                                
                                Log($"Generated data SQL: {dataSql}");
                            }
                            
                            // Add ORDER BY clause if we have primary keys for consistent ordering
                            // For Firebird, only add if not already using FIRST/SKIP syntax
                            if (!string.IsNullOrEmpty(orderBy) && 
                                (!_provider.ProviderName.Equals("Firebird", StringComparison.OrdinalIgnoreCase) || !dataSql.Contains("FIRST")))
                            {
                                dataSql += $" {orderBy}";
                            }
                            
                            // Handle paging for different database systems
                            if (useBatching) 
                            {
                                if (_provider.ProviderName.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                                {
                                    string orderByColumn = tableSchema.Columns
                                        .FirstOrDefault(c => c.IsPrimaryKey)?.Name ?? 
                                        tableSchema.Columns.First().Name; // Use first column as fallback
                                    
                                    // In SQL Server, use ROW_NUMBER() in a subquery for reliable paging
                                    dataSql = $@"SET NOCOUNT ON; 
                                            WITH NumberedRows AS (
                                                SELECT *, ROW_NUMBER() OVER (ORDER BY [{orderByColumn}]) AS RowNum 
                                                FROM [{tableSchema.Schema ?? "dbo"}].[{tableSchema.Name}] WITH (NOLOCK)
                                                {(string.IsNullOrEmpty(whereClause) ? "" : $"WHERE {whereClause}")}
                                            )
                                            SELECT * FROM NumberedRows
                                            WHERE RowNum > {offset} AND RowNum <= {offset + currentBatchSize}";
                                }
                                else if (_provider.ProviderName.Equals("MySQL", StringComparison.OrdinalIgnoreCase))
                                {
                                    // MySQL uses LIMIT offset,count syntax
                                    dataSql += $" LIMIT {offset}, {currentBatchSize}";
                                }
                                else if (_provider.ProviderName.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
                                {
                                    // PostgreSQL uses LIMIT count OFFSET offset syntax
                                    dataSql += $" LIMIT {currentBatchSize} OFFSET {offset}";
                                }
                            }
                            
                            // Add paging for SQL Server - this is already handled in the ROW_NUMBER() approach above
                            // Skip SQL Server here since we already applied paging above
                            if (_provider.ProviderName.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) && useBatching)
                            {
                                // Already handled by ROW_NUMBER() approach above
                                // Don't add additional OFFSET/FETCH paging
                            }
                            else if (_provider.ProviderName.Equals("MySQL", StringComparison.OrdinalIgnoreCase) && useBatching)
                            {
                                // MySQL syntax for LIMIT/OFFSET
                                dataSql += $" LIMIT {offset}, {currentBatchSize}";
                            }
                            else if (_provider.ProviderName.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) && useBatching)
                            {
                                // PostgreSQL syntax for LIMIT/OFFSET
                                dataSql += $" LIMIT {currentBatchSize} OFFSET {offset}";
                            }
                            else if (_provider.ProviderName.Equals("Firebird", StringComparison.OrdinalIgnoreCase) && useBatching)
                            {
                                // For Firebird, we don't add any pagination to the SQL
                                // We'll handle limiting the results in memory
                                
                                // Log the SQL for debugging
                                Log($"Firebird query (no pagination): {dataSql}");
                            }
                                
                            // Create command with special transaction settings to match what the schema view uses
                            using (var dataCommand = directConnection.CreateCommand())
                            {
                                dataCommand.CommandText = dataSql;
                                dataCommand.CommandTimeout = 300; // 5 minute timeout to match FirebirdProvider
                                
                                // Make sure we're using CommandType.Text to avoid procedure execution behavior
                                dataCommand.CommandType = System.Data.CommandType.Text;
                                
                                // Create transaction with ReadCommitted isolation level - this is crucial
                                // Wrap in using statement to ensure proper disposal
                                using (var transaction = directConnection.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                                {
                                    dataCommand.Transaction = transaction;
                                    
                                    Log($"Created transaction with ReadCommitted isolation level for {tableSchema.FullName}");
                                
                                // Special handling for Firebird to avoid permission issues
                                if (_provider.ProviderName.Equals("Firebird", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Set shorter timeout for Firebird operations
                                    dataCommand.CommandTimeout = 300; // 5 minutes - allow more time for full table scan
                                    
                                    // For Firebird, ensure we use the simplest command behavior
                                    dataCommand.CommandType = System.Data.CommandType.Text;
                                    
                                    Log($"Using Firebird-specific data access approach for {tableSchema.FullName}");
                                }
                                
                                try
                                {
                                    // Execute the reader with specific behavior settings
                                    System.Data.CommandBehavior behavior = System.Data.CommandBehavior.SingleResult;
                                    
                                    // For Firebird, we need special handling
                                    bool isFirebird = _provider.ProviderName.Equals("Firebird", StringComparison.OrdinalIgnoreCase);
                                    
                                    using (var reader = await dataCommand.ExecuteReaderAsync(behavior))
                                    {
                                        if (!reader.HasRows)
                                        {
                                            continue;
                                        }
                                        
                                        // For Firebird, we need in-memory pagination
                                        int skipRows = isFirebird && useBatching ? offset : 0;
                                        int maxRows = isFirebird && useBatching ? currentBatchSize : int.MaxValue;
                                        int currentRow = 0;
                                        
                                        // Process this batch
                                        
                                        // Verify reader field count
                                        int fieldCount = reader.FieldCount;
                                        
                                        // Collect batch rows
                                        var batchRows = new List<RowData>();
                                        int processedRows = 0;
                                        
                                        // Create a simple reader loop
                                        while (await reader.ReadAsync())
                                        {
                                            // For Firebird with batching, handle pagination in memory
                                            if (isFirebird && useBatching)
                                            {
                                                currentRow++;
                                                
                                                // Skip rows for offset
                                                if (currentRow <= skipRows)
                                                {
                                                    continue;
                                                }
                                                
                                                // Stop after max rows
                                                if (processedRows >= maxRows)
                                                {
                                                    break;
                                                }
                                            }
                                            
                                            var row = new RowData
                                            {
                                                Values = new Dictionary<string, object?>()
                                            };
                                            
                                            // Process each column in the reader
                                            for (int i = 0; i < reader.FieldCount; i++)
                                            {
                                                var columnName = reader.GetName(i);
                                                object? value = null;
                                                
                                            try
                                            {
                                                // Handle DBNull properly
                                                if (!reader.IsDBNull(i))
                                                {
                                                    // Use typed accessors when possible to avoid conversion issues
                                                    string typeName = reader.GetDataTypeName(i).ToLowerInvariant();
                                                    
                                                    // Special direct handling for critical columns
                                                    bool isCriticalColumn = columnName.Equals("SerialNo", StringComparison.OrdinalIgnoreCase) || 
                                                                         columnName.Equals("Barcode", StringComparison.OrdinalIgnoreCase) || 
                                                                         columnName.Equals("Status", StringComparison.OrdinalIgnoreCase);
                                                    
                                                    // Handle string columns with special care, especially critical ones
                                                    if (typeName.Contains("varchar") || typeName.Contains("char") || 
                                                        typeName.Contains("text") || typeName.Contains("nvarchar") || 
                                                        typeName.Contains("ntext") || typeName.Contains("nchar") || isCriticalColumn)
                                                    {
                                                        if (isCriticalColumn) {
                                                            // For critical columns, try all possible methods to ensure we get a value
                                                            try
                                                            {
                                                                // Try GetValue first
                                                                object rawValue = reader.GetValue(i);
                                                                if (rawValue != null)
                                                                {
                                                                    // Ensure we convert to string properly
                                                                    value = rawValue.ToString();
                                                                }
                                                                else
                                                                {
                                                                    value = ""; // Use empty string rather than null for critical columns
                                                                }
                                                            }
                                                            catch (Exception)
                                                            {
                                                                value = ""; // Use empty string as fallback for critical columns
                                                            }
                                                        }
                                                        else
                                                        {
                                                            // Normal string column handling
                                                            try
                                                            {
                                                                value = reader.GetString(i);
                                                                // Prevent possible empty string in some database systems
                                                                if (value is string str && str.Length == 0)
                                                                {
                                                                    // Some databases return empty string instead of NULL - ensure consistent handling
                                                                    if (reader.IsDBNull(i))
                                                                    {
                                                                        value = null;
                                                                    }
                                                                }
                                                            }
                                                            catch (Exception)
                                                            {
                                                                if (!reader.IsDBNull(i))
                                                                {
                                                                    // Try direct GetValue and convert to string if needed
                                                                    value = reader.GetValue(i)?.ToString();
                                                                }
                                                            }
                                                        }
                                                    }
                                                    else if (typeName.Contains("int"))
                                                    {
                                                        // Need to check if it's a bigint (Int64) or regular int (Int32)
                                                        if (typeName.Contains("bigint"))
                                                        {
                                                            value = reader.GetInt64(i);
                                                        }
                                                        else
                                                        {
                                                            try
                                                            {
                                                                value = reader.GetInt32(i);
                                                            }
                                                            catch (InvalidCastException)
                                                            {
                                                                // Fall back to Int64 if Int32 fails
                                                                value = reader.GetInt64(i);
                                                            }
                                                        }
                                                    }
                                                    else if (typeName.Contains("datetime") || typeName.Contains("date"))
                                                    {
                                                        try
                                                        {
                                                            value = reader.GetDateTime(i);
                                                        }
                                                        catch (Exception)
                                                        {
                                                            value = reader.GetValue(i); // Fall back to generic
                                                        }
                                                    }
                                                    else if (typeName.Contains("decimal") || typeName.Contains("money") || 
                                                             typeName.Contains("numeric"))
                                                    {
                                                        try
                                                        {
                                                            value = reader.GetDecimal(i);
                                                        }
                                                        catch (Exception)
                                                        {
                                                            value = reader.GetValue(i); // Fall back to generic
                                                        }
                                                    }
                                                    else if (typeName.Contains("bit"))
                                                    {
                                                        try
                                                        {
                                                            value = reader.GetBoolean(i);
                                                        }
                                                        catch (Exception)
                                                        {
                                                            value = reader.GetValue(i); // Fall back to generic
                                                        }
                                                    }
                                                    else if (typeName.Contains("float") || typeName.Contains("real") || typeName.Contains("double"))
                                                    {
                                                        try
                                                        {
                                                            value = reader.GetDouble(i);
                                                        }
                                                        catch (Exception)
                                                        {
                                                            value = reader.GetValue(i); // Fall back to generic
                                                        }
                                                    }
                                                    else
                                                    {
                                                        // Fall back to generic GetValue
                                                        value = reader.GetValue(i);
                                                    }
                                                    
                                                    // Special handling for certain values
                                                    if (value is string strValue && strValue.Contains("rows affected"))
                                                    {
                                                        // This looks like SQL output text instead of actual data
                                                        value = null; // Don't include this SQL output as data
                                                    }
                                                }
                                            }
                                            catch (Exception)
                                            {
                                                // Try fallback to generic GetValue
                                                try 
                                                {
                                                    if (!reader.IsDBNull(i))
                                                    {
                                                        value = reader.GetValue(i);
                                                    }
                                                }
                                                catch
                                                {
                                                    // If all else fails, leave as null
                                                }
                                            }
                                                
                                            // Add the value to the row dictionary
                                            row.Values[columnName] = value;
                                        }
                                            
                                        batchRows.Add(row);
                                        processedRows++;
                                    }
                                    
                                    // Write this batch to file
                                    using (var fileStream = File.Create(currentFilePath))
                                    using (var gzipStream = new GZipStream(fileStream, System.IO.Compression.CompressionMode.Compress, true))
                                    {
                                        var tableData = new TableData
                                        {
                                            Schema = tableSchema,
                                            Rows = batchRows,
                                            TotalCount = processedRows,
                                            BatchNumber = batchNumber,
                                            TotalBatches = totalBatches,
                                            IsLastBatch = isLastBatch
                                        };
                                        
                                        // Use standard options for consistency
                                        var options = MessagePackSerializerOptions.Standard;
                                            
                                        await Task.Run(() => MessagePackSerializer.Serialize(gzipStream, tableData, options));
                                        await gzipStream.FlushAsync();
                                    }
                                }
                                
                                // Commit transaction on successful completion
                                transaction.Commit();
                                Log($"Transaction committed successfully for {tableSchema.FullName}");
                                }
                                catch (Exception readerEx) when (
                                    _provider.ProviderName.Equals("Firebird", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Handle permission issues gracefully at data reader level
                                    string permissionErrorFile = Path.Combine(dataDir, $"{tableSchema.Schema ?? "dbo"}_{tableSchema.Name}.error");
                                    string errorDetails = $"{{\"Error\":\"No permission to read data\",\"Table\":\"{tableSchema.Name}\",\"Message\":\"{readerEx.Message.Replace('"', '\'')}\",\"Provider\":\"{_provider.ProviderName}\"}}";
                                    await File.WriteAllTextAsync(permissionErrorFile, errorDetails);
                                    
                                    // Create a summary file if it doesn't exist
                                    if (!string.IsNullOrEmpty(outputPath))
                                    {
                                        string summaryFile = Path.Combine(outputPath, "export_skipped_tables.txt");
                                        using (StreamWriter sw = File.AppendText(summaryFile))
                                        {
                                            await sw.WriteLineAsync($"{tableSchema.FullName}: Permission error - {readerEx.Message}");
                                        }
                                    }
                                    
                                    // If this is BOMISC table, add more detailed explanation
                                    if (tableSchema.Name.Equals("BOMISC", StringComparison.OrdinalIgnoreCase))
                                    {
                                        Log($"BOMISC table requires elevated permissions. The user 'syshosis' can view metadata but lacks SELECT permissions on table data.");
                                        Log($"To fix: Connect to Firebird and run: GRANT SELECT ON BOMISC TO SYSHOSIS;");
                                        
                                        // Create a special file with instructions
                                        string instructionsFile = Path.Combine(dataDir, $"{tableSchema.Name}_instructions.txt");
                                        string instructions = $"The BOMISC table requires elevated permissions to export.\r\n";
                                        instructions += $"While metadata access is available (which is why the table appears in schema view),\r\n";
                                        instructions += $"the 'syshosis' user lacks SELECT permissions on the actual table data.\r\n\r\n";
                                        instructions += $"To fix this issue, connect to the Firebird database using an administrative account\r\n";
                                        instructions += $"and run the following command:\r\n\r\n";
                                        instructions += $"GRANT SELECT ON BOMISC TO SYSHOSIS;\r\n\r\n";
                                        instructions += $"Then try the export again.";
                                        
                                        await File.WriteAllTextAsync(instructionsFile, instructions);
                                    }
                                    
                                    // Log the error but continue with other tables
                                    ReportProgress(0, 1, $"Permission error executing query for table {tableSchema.FullName} - Skipping", true);
                                    Log($"Table data query failed due to permission error: {tableSchema.FullName} - {readerEx.Message}");
                                    return; // Skip this table
                                }
                                } // Close transaction using block
                            }
                        }
                    }
                }
            }
            catch
            {
                throw;
            }
        }

        private Dictionary<string, List<string>> CalculateDependencyOrder(List<TableSchema> tables)
        {
            // Safety check for empty tables list
            if (tables == null || tables.Count == 0)
            {
                return new Dictionary<string, List<string>>();
            }
            
            try
            {
                var result = new Dictionary<string, List<string>>();
                var visited = new HashSet<string>();
                var dependencies = new Dictionary<string, HashSet<string>>();
                
                // Build dependency graph
                foreach (var table in tables)
                {
                    string tableName = table?.FullName ?? $"table_{table?.Name ?? "unknown"}";
                    if (!dependencies.ContainsKey(tableName))
                    {
                        dependencies[tableName] = new HashSet<string>();
                    }
                    
                    foreach (var fk in table?.ForeignKeys ?? Enumerable.Empty<ForeignKeyDefinition>())
                    {
                        string refTableName = $"{fk.ReferencedTableSchema}.{fk.ReferencedTableName}";
                        dependencies[tableName].Add(refTableName);
                    }
                }
                
                // Topological sort
                var levels = new Dictionary<string, int>();
                foreach (var table in tables)
                {
                    string tableName = table?.FullName ?? $"table_{table?.Name ?? "unknown"}";
                    if (!visited.Contains(tableName))
                    {
                        CalculateLevels(tableName, dependencies, visited, levels);
                    }
                }
                
                // Group tables by level
                var levelGroups = levels.GroupBy(x => x.Value)
                    .OrderBy(g => g.Key)
                    .ToDictionary(g => g.Key.ToString(), g => g.Select(x => x.Key).ToList());
                
                return levelGroups;
            }
            catch (DivideByZeroException)
            {
                return new Dictionary<string, List<string>>();
            }
            catch (Exception)
            {
                return new Dictionary<string, List<string>>();
            }
        }

        private void CalculateLevels(string tableName, Dictionary<string, HashSet<string>> dependencies, HashSet<string> visited, Dictionary<string, int> levels)
        {
            visited.Add(tableName);
            
            if (!dependencies.ContainsKey(tableName))
            {
                levels[tableName] = 0;
                return;
            }
            
            int maxLevel = 0;
            foreach (var dep in dependencies[tableName])
            {
                if (!visited.Contains(dep))
                {
                    CalculateLevels(dep, dependencies, visited, levels);
                }
                
                if (levels.ContainsKey(dep))
                {
                    maxLevel = Math.Max(maxLevel, levels[dep] + 1);
                }
            }
            
            levels[tableName] = maxLevel;
        }
        
        /// <summary>
        /// Exports a single table for testing purposes
        /// </summary>
        public async Task ExportSingleTableAsync(TableSchema tableSchema, string outputPath)
        {
            // Create the output directory if it doesn't exist
            Directory.CreateDirectory(outputPath);
            string dataDir = Path.Combine(outputPath, "data");
            Directory.CreateDirectory(dataDir);
            
            // Write single table metadata
            await Utilities.MetadataManager.WriteMetadataAsync(
                outputPath, 
                new List<TableSchema> { tableSchema }, 
                "SingleTableExport", 
                schemaOnly: false);
            
            // Export the table data
            await ExportTableDataAsync(tableSchema, dataDir, null, null);
        }
    }

    public class ExportOptions
    {
        public List<string>? Tables { get; set; }
        public Dictionary<string, string>? TableCriteria { get; set; }
        public int BatchSize { get; set; } = 100000;
        public bool CompressData { get; set; } = true;
        public bool IncludeSchemaOnly { get; set; } = false;
        public bool ContinueOnError { get; set; } = true; // Default to continue on permission errors
        public string? OutputDirectory { get; set; }
    }
}