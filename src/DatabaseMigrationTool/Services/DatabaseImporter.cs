using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Providers;
using MessagePack;
using MessagePack.Resolvers;
using MessagePack.Formatters;
using System.Text.Json;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;
using System.Data.Common;
using System.IO;
using System.IO.Compression;
using Dapper;

namespace DatabaseMigrationTool.Services
{
    public class DatabaseImporter
    {
        private readonly IDatabaseProvider _provider;
        private readonly DbConnection _connection;
        private readonly ImportOptions _options;
        private ProgressReportHandler? _progressReporter;
        private StreamWriter? _logWriter;
        private string? _logFilePath;
        
        public DatabaseImporter(IDatabaseProvider provider, DbConnection connection, ImportOptions options)
        {
            _provider = provider;
            _connection = connection;
            _options = options;
        }
        
        public void SetProgressReporter(ProgressReportHandler progressReporter)
        {
            _progressReporter = progressReporter;
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
        
        private void Log(string message, bool critical = false)
        {
            // If critical, make it stand out
            string logMessage = critical ? $"[CRITICAL] {message}" : message;

            _logWriter?.WriteLine(logMessage);
            _logWriter?.Flush(); // Ensure log is written immediately
        }

        /// <summary>
        /// Drops a table if it exists
        /// </summary>
        private async Task DropTableAsync(string tableName)
        {
            try
            {
                // For Firebird, we need to ensure no other connections are holding locks
                // Use a separate transaction with proper isolation for metadata operations
                string dropSql = $"DROP TABLE \"{tableName}\"";
                Log($"Executing DROP TABLE for {tableName}: {dropSql}");

                // For Firebird, use explicit transaction control with proper isolation
                if (_provider.ProviderName.Equals("Firebird", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"Preparing Firebird DDL operation for table {tableName}");

                    // For Firebird, DDL operations require exclusive access
                    // We need to create a completely new connection to avoid any transaction conflicts
                    var connectionString = _connection.ConnectionString;

                    // Force garbage collection to release any lingering resources
                    GC.Collect();
                    GC.WaitForPendingFinalizers();

                    // Wait a moment for Firebird to release locks
                    await Task.Delay(500);

                    // Create a completely new connection for the DDL operation
                    using var dedicatedConnection = _provider.CreateConnection(connectionString);
                    await dedicatedConnection.OpenAsync();

                    Log($"Created dedicated connection for Firebird DDL operation on {tableName}");

                    // Use a dedicated transaction for the DROP operation
                    using var transaction = dedicatedConnection.BeginTransaction(System.Data.IsolationLevel.ReadCommitted);
                    using var command = dedicatedConnection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = dropSql;
                    command.CommandTimeout = 60; // Longer timeout for DDL operations

                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                    await transaction.CommitAsync().ConfigureAwait(false);

                    Log($"Successfully dropped table {tableName} using dedicated Firebird connection");
                }
                else
                {
                    // For non-Firebird providers, use the original approach
                    using var command = _connection.CreateCommand();
                    command.CommandText = dropSql;
                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log($"Error dropping table {tableName}: {ex.Message}");

                // For Firebird connection lock errors, provide specific guidance
                if (ex.Message.Contains("lock conflict") || ex.Message.Contains("object TABLE") && ex.Message.Contains("is in use"))
                {
                    Log($"Detected Firebird metadata lock conflict for table {tableName}. This usually indicates another connection is still active.");
                    Log($"Attempting to resolve by ensuring all connections are closed and retrying...");

                    // Wait a moment for any lingering connections to release
                    await Task.Delay(1000);
                    throw;
                }
                throw;
            }
        }

        public async Task ImportAsync(string inputPath)
        {
            // Create log file in the inputPath directory
            _logFilePath = Path.Combine(inputPath, $"import_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            try
            {
                _logWriter = new StreamWriter(_logFilePath, false);
                Log($"Log file created at: {_logFilePath}", true);
                
                // Ensure all providers have the logger set
                if (_provider is IDatabaseProvider provider)
                {
                    // Create a wrapper that adapts our Log method to Action<string>
                    Action<string> logWrapper = (msg) => Log(msg);
                    provider.SetLogger(logWrapper);
                }
            }
            catch
            {
                _logWriter = null!;
            }
            
            Log("================ DATABASE IMPORT STARTED =================", true);
            
            // Report initial progress
            ReportProgress(0, 100, "Starting database import...", true);
            Log($"Import started at: {DateTime.Now}");
            Log($"Provider: {_provider.ProviderName}");
            Log($"Input path: {inputPath}");
            Log($"Batch size: {_options.BatchSize}");
            Log($"Create schema: {_options.CreateSchema}");
            Log($"Create foreign keys: {_options.CreateForeignKeys}");
            Log($"Continue on error: {_options.ContinueOnError}");
            if (_options.Tables != null && _options.Tables.Any())
            {
                Log($"Tables filter: {string.Join(", ", _options.Tables)}");
            }
            
            // Check if input directory exists and list contents
            if (!Directory.Exists(inputPath))
            {
                Log($"ERROR: Input directory does not exist: {inputPath}", true);
                throw new DirectoryNotFoundException($"Input directory not found: {inputPath}");
            }
            
            Log("Input directory contents:");
            foreach (var file in Directory.GetFiles(inputPath))
            {
                var fi = new FileInfo(file);
                Log($"- {Path.GetFileName(file)}: {fi.Length} bytes, Last modified: {fi.LastWriteTime}");
            }
            
            // Also check data subdirectory
            string dataSubDir = Path.Combine(inputPath, "data");
            if (Directory.Exists(dataSubDir))
            {
                Log("Data directory contents:");
                foreach (var file in Directory.GetFiles(dataSubDir))
                {
                    var fi = new FileInfo(file);
                    Log($"- {Path.GetFileName(file)}: {fi.Length} bytes, Last modified: {fi.LastWriteTime}");
                }
            }
            else
            {
                Log($"WARNING: Data directory not found: {dataSubDir}", true);
            }

            // Report progress
            ReportProgress(5, 100, "Checking input directory...");
            
            // Read metadata
            DatabaseExport export;
            
            try
            {
                if (!Utilities.MetadataManager.IsValidExport(inputPath))
                {
                    Log($"ERROR: Directory does not contain a valid export: {inputPath}", true);
                    throw new InvalidOperationException($"Invalid export format. Expected valid export in: {inputPath}");
                }

                Log("Reading metadata...");
                export = await Utilities.MetadataManager.ReadMetadataAsync(inputPath).ConfigureAwait(false);
                Log($"Successfully read metadata: {export.Schemas?.Count ?? 0} tables");
                
                Log($"Export database name: {export.DatabaseName}");
                Log($"Export tables count: {export.Schemas?.Count ?? 0}");
                Log($"Export date: {export.ExportDate}");
            }
            catch (Exception ex)
            {
                Log($"Failed to read metadata: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
                throw;
            }

            Log($"Found export from database '{export.DatabaseName}' with {export.Schemas?.Count ?? 0} tables");

            // Report progress
            ReportProgress(10, 100, $"Loaded metadata: {export.Schemas?.Count ?? 0} tables from '{export.DatabaseName}'");

            // Prepare table list for import (apply filtering early so schema creation is also filtered)
            var tablesToImport = (export.Schemas ?? new List<TableSchema>()).ToList();
            if (_options.UseDependencyOrder && export.DependencyOrder?.Any() == true)
            {
                Log("Using dependency order for import");
                tablesToImport = ReorderTablesByDependency(export.Schemas ?? new List<TableSchema>(), export.DependencyOrder);
            }

            // Filter tables if specified - do this BEFORE schema creation
            if (_options.Tables != null && _options.Tables.Any())
            {
                // Check if all specified tables exist in the export (check both Name and FullName)
                var availableTableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var table in tablesToImport)
                {
                    availableTableNames.Add(table.Name);
                    availableTableNames.Add(table.FullName);
                }
                var missingTables = _options.Tables.Where(t => !availableTableNames.Contains(t)).ToList();

                if (missingTables.Any())
                {
                    string errorMessage = $"The following requested tables were not found in the export data: {string.Join(", ", missingTables)}";
                    Log(errorMessage, true);
                    throw new InvalidOperationException(errorMessage);
                }

                tablesToImport = tablesToImport
                    .Where(t => _options.Tables.Any(filterName =>
                        t.Name.Equals(filterName, StringComparison.OrdinalIgnoreCase) ||
                        t.FullName.Equals(filterName, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                // Log the filter results
                Log($"[CRITICAL] After filtering, {tablesToImport.Count} tables remain in import list", true);
                foreach (var table in tablesToImport)
                {
                    Log($"[CRITICAL] - Will import: {table.Schema}.{table.Name}", true);
                }
            }

            // Create schemas and tables using filtered table list
            if (_options.CreateSchema)
            {
                // Report progress
                ReportProgress(15, 100, "Creating database schema...");
                await CreateSchemas(tablesToImport);
            }

            // Report progress
            if (_options.CreateSchema)
            {
                ReportProgress(40, 100, "Schema creation completed");
            }

            // Import data
            if (!_options.SchemaOnly)
            {
                var dataDir = Path.Combine(inputPath, "data");
                if (!Directory.Exists(dataDir))
                {
                    throw new DirectoryNotFoundException($"Data directory not found: {dataDir}");
                }

                // Enhanced functionality: Scan for data files not referenced in metadata
                Log("Scanning for data files not referenced in metadata...");
                await ScanForUnreferencedTables(dataDir, tablesToImport);

                // Tables are already filtered above, no need to filter again here

                int tableCount = 0;
                int totalTables = tablesToImport.Count;
                foreach (var table in tablesToImport)
                {
                    tableCount++;
                    
                    Log($"[{tableCount}/{tablesToImport.Count}] Importing data for {table.FullName}...");
                    
                    // Report progress
                    int progressValue = 40 + (int)((double)tableCount / totalTables * 55);
                    ReportProgress(progressValue, 100, $"Importing table {tableCount} of {totalTables}: {table.FullName}");
                    
                    // Create a progress handler that updates with each file
                    Action<string> fileProgressHandler = (fileInfo) => {
                        // Progress message already includes the file information
                        ReportProgress(progressValue, 100, $"Importing table {tableCount} of {totalTables}: {table.FullName} {fileInfo}");
                    };
                    
                    // Pass the file progress handler to ImportTableDataAsync
                    await ImportTableDataAsync(table, dataDir, fileProgressHandler).ConfigureAwait(false);
                    
                    // Report progress after each table
                    int completedProgressValue = 40 + (int)((double)(tableCount + 1) / totalTables * 55);
                    ReportProgress(completedProgressValue, 100, $"Imported table {tableCount} of {totalTables}: {table.FullName}");
                }
            }

            Log("Database import completed successfully!");
            
            // Report final progress
            ReportProgress(100, 100, "Database import completed successfully!");
            
            // Close log file
            if (_logWriter != null)
            {
                _logWriter.Close();
                _logWriter.Dispose();
            }
        }

        private async Task CreateSchemas(List<TableSchema> schemas)
        {
            Log($"================ SCHEMA CREATION STARTED =================");
            Log($"Found {schemas.Count} tables to create");

            var createdSchemas = new HashSet<string>();

            // Create schemas first
            Log($"Step 1: Creating necessary schemas...");

            // Store original schema names before any mapping for file lookup purposes
            foreach (var table in schemas)
            {
                if (table.AdditionalProperties == null)
                    table.AdditionalProperties = new Dictionary<string, string>();

                // Store original schema name for file lookup
                table.AdditionalProperties["OriginalSchema"] = table.Schema ?? "dbo";
            }

            // Apply schema mapping for SQL Server before creating schemas
            if (_provider.ProviderName.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                Log($"Applying SQL Server schema mapping (all schemas -> dbo)");
                foreach (var table in schemas)
                {
                    if (!string.IsNullOrWhiteSpace(table.Schema) && !table.Schema.Equals("dbo", StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"Mapping schema '{table.Schema}' to 'dbo' for SQL Server");
                        table.Schema = "dbo";
                    }
                }
            }
            
            foreach (var table in schemas)
            {
                if (!string.IsNullOrEmpty(table.Schema) && !createdSchemas.Contains(table.Schema))
                {
                    try
                    {
                        Log($"Creating schema '{table.Schema}' if it doesn't exist");
                        
                        // SQL Server syntax for creating a schema if it doesn't exist
                        Console.WriteLine($"[PROVIDER DEBUG] Provider name: '{_provider.ProviderName}'");
                        if (_provider.ProviderName.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                        {
                            // Skip creating 'dbo' schema as it always exists in SQL Server
                            if (table.Schema.Equals("dbo", StringComparison.OrdinalIgnoreCase))
                            {
                                Log($"Skipping creation of 'dbo' schema - it already exists in SQL Server");
                                createdSchemas.Add(table.Schema);
                                continue;
                            }
                            
                            string sql = $"IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{table.Schema}') EXEC('CREATE SCHEMA {_provider.EscapeIdentifier(table.Schema)}');";
                            Log($"Executing SQL: {sql}");
                            Console.WriteLine($"[SQL SERVER DEBUG] About to execute SQL Server schema creation: {sql}");
                            await _connection.ExecuteNonQueryAsync(sql).ConfigureAwait(false);
                        }
                        else if (_provider.ProviderName.Equals("Firebird", StringComparison.OrdinalIgnoreCase))
                        {
                            // Firebird doesn't have CREATE SCHEMA - the schema is the user/owner name
                            // In Firebird, when a user creates a table, they become the owner automatically
                            Console.WriteLine($"[FIREBIRD DEBUG] Detected Firebird provider - skipping schema creation for '{table.Schema}'");
                            Log($"Skipping schema creation for Firebird - schemas are handled automatically by user ownership");
                        }
                        else
                        {
                            // Generic syntax for other database systems (MySQL, PostgreSQL, etc.)
                            string sql = $"CREATE SCHEMA IF NOT EXISTS {_provider.EscapeIdentifier(table.Schema)}";
                            Log($"Executing SQL: {sql}");
                            await _connection.ExecuteNonQueryAsync(sql).ConfigureAwait(false);
                        }
                        createdSchemas.Add(table.Schema);
                        Log($"Schema '{table.Schema}' created or already exists");
                    }
                    catch (Exception ex)
                    {
                        // Schema might already exist or not supported
                        Log($"WARNING: Error creating schema '{table.Schema}': {ex.Message}");
                        createdSchemas.Add(table.Schema); // Still consider it created to avoid repeated errors
                    }
                }
            }

            // Create tables in dependency order if available
            var tablesToCreate = schemas;
            Log($"Step 2: Creating {tablesToCreate.Count} tables...");

            int tableCount = 0;
            foreach (var table in tablesToCreate)
            {
                tableCount++;
                Log($"[{tableCount}/{tablesToCreate.Count}] Creating table {table.FullName}...", true);
                Log($"Table details: {table.Columns?.Count ?? 0} columns, {table.Indexes?.Count ?? 0} indexes, {table.Constraints?.Count ?? 0} constraints, {table.ForeignKeys?.Count ?? 0} foreign keys");
                
                try
                {
                    // Check if we need to drop existing table first
                    if (_options.OverwriteExistingTables)
                    {
                        Log($"Checking if table {table.FullName} exists (overwrite mode enabled)");
                        try
                        {
                            // Check if table exists by trying to get its schema
                            var existingTables = await _provider.GetTablesAsync(_connection, new[] { table.Name }).ConfigureAwait(false);
                            if (existingTables.Any(t => t.Name.Equals(table.Name, StringComparison.OrdinalIgnoreCase)))
                            {
                                Log($"Table {table.FullName} exists - dropping before recreation");
                                await DropTableAsync(table.Name).ConfigureAwait(false);
                                Log($"Table {table.FullName} dropped successfully");
                            }
                            else
                            {
                                Log($"Table {table.FullName} does not exist - will create new");
                            }
                        }
                        catch (Exception dropCheckEx)
                        {
                            Log($"Warning: Could not check/drop existing table {table.FullName}: {dropCheckEx.Message}");
                            // Continue anyway - maybe table doesn't exist
                        }
                    }

                    // Create the table first
                    Log($"Creating table structure for {table.FullName}");
                    // We don't have direct access to the SQL, just log the creation attempt
                    Log($"Executing CreateTableAsync for {table.FullName}");

                    try
                    {
                        await _provider.CreateTableAsync(_connection, table).ConfigureAwait(false);
                        Log($"Table {table.FullName} structure created successfully");
                    }
                    catch (Exception ex)
                    {
                        Log($"[CRITICAL] ERROR creating table structure: {ex.Message}", true);
                        throw; // Re-throw to maintain original behavior
                    }
                    
                    // Then create indexes
                    if (table.Indexes?.Any() == true)
                    {
                        Log($"Creating {table.Indexes?.Count ?? 0} indexes for {table.FullName}");
                        await _provider.CreateIndexesAsync(_connection, table).ConfigureAwait(false);
                        Log($"Indexes for {table.FullName} created successfully");
                    }
                    
                    // Then create constraints
                    if (table.Constraints?.Any() == true)
                    {
                        Log($"Creating {table.Constraints?.Count ?? 0} constraints for {table.FullName}");
                        await _provider.CreateConstraintsAsync(_connection, table).ConfigureAwait(false);
                        Log($"Constraints for {table.FullName} created successfully");
                    }
                    
                    Log($"Table {table.FullName} fully created with all its components", true);
                }
                catch (Exception ex)
                {
                    Log($"ERROR creating table {table.FullName}: {ex.Message}", true);
                    Log($"Stack trace: {ex.StackTrace}");
                    
                    if (ex.InnerException != null)
                    {
                        Log($"Inner exception: {ex.InnerException.Message}");
                        Log($"Inner stack trace: {ex.InnerException.StackTrace}");
                    }
                    
                    if (!_options.ContinueOnError)
                    {
                        throw;
                    }
                }
            }
            
            // Foreign keys need to be created after all tables
            if (_options.CreateForeignKeys)
            {
                foreach (var table in tablesToCreate)
                {
                    if (table.ForeignKeys?.Any() == true)
                    {
                        Log($"Creating foreign keys for {table.FullName}...");
                        try
                        {
                            await _provider.CreateForeignKeysAsync(_connection, table).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Log($"Error creating foreign keys for {table.FullName}: {ex.Message}");
                            if (!_options.ContinueOnError)
                            {
                                throw;
                            }
                        }
                    }
                }
            }
        }

        private async Task ImportTableDataAsync(TableSchema table, string dataDir, Action<string>? fileProgressHandler = null)
        {
            Log($"================ TABLE DATA IMPORT FOR {table.FullName} =================", true);

            // Use original schema name for file lookup (before any schema mapping)
            string originalSchema = table.AdditionalProperties?.GetValueOrDefault("OriginalSchema") ?? table.Schema ?? "dbo";

            if (originalSchema != table.Schema)
            {
                Log($"Schema mapping detected: '{originalSchema}' -> '{table.Schema}'. Using original schema for file lookup.");
            }

            // Check for data files existence first
            string expectedFileName = $"{originalSchema}_{table.Name}.bin";
            string expectedPath = Path.Combine(dataDir, expectedFileName);
            string batchPattern = $"{originalSchema}_{table.Name}_batch*.bin";

            Log($"Checking for data files for {table.FullName}");
            Log($"Using original schema '{originalSchema}' for file lookup");
            Log($"Expecting file: {expectedPath}");
            Log($"Or batch pattern: {Path.Combine(dataDir, batchPattern)}");
            
            bool fileExists = File.Exists(expectedPath);
            var batchFileArray = Directory.GetFiles(dataDir, batchPattern);
            
            if (fileExists)
            {
                Log($"Found main data file: {expectedPath}");
                var fileInfo = new FileInfo(expectedPath);
                Log($"File size: {fileInfo.Length} bytes, Last modified: {fileInfo.LastWriteTime}");
            }
            else if (batchFileArray.Length > 0)
            {
                Log($"Found {batchFileArray.Length} batch files:");
                foreach (var file in batchFileArray)
                {
                    var fileInfo = new FileInfo(file);
                    Log($"- {file}: {fileInfo.Length} bytes, Last modified: {fileInfo.LastWriteTime}");
                }
            }
            else
            {
                Log($"WARNING: No data files found for {table.FullName}. Expected {expectedPath} or batch files.", true);
                Log($"Directory contents of {dataDir}:");
                foreach (var file in Directory.GetFiles(dataDir))
                {
                    Log($"- {Path.GetFileName(file)}");
                }
                return; // Skip import as there's no data
            }
            
            // Use the TableImporter implementation
            try
            {
                Log($"[{table.FullName}] Starting import using TableImporter");
                
                // Create a TableImporter instance for this table
                // Create a wrapper that adapts our Log method to Action<string>
                Action<string> logWrapper = (msg) => Log(msg);
                var importer = new TableImporter(_provider, _connection, _options.BatchSize, logWrapper);
                
                // Pass the progress reporter to TableImporter for batch updates
                if (_progressReporter != null)
                {
                    importer.SetProgressReporter(_progressReporter);
                }
                
                // Import the table data
                Log($"[{table.FullName}] Calling ImportTableDataAsync");
                var result = await importer.ImportTableDataAsync(table, dataDir).ConfigureAwait(false);
                
                if (result.Success)
                {                    
                    Log($"[{table.FullName}] Import successful: {result.RowsImported} rows imported", true);
                    return;
                }
                else
                {
                    Log($"[{table.FullName}] Import failed: {result.Message}");
                    
                    if (!_options.ContinueOnError)
                    {
                        throw new Exception($"Failed to import table {table.FullName}: {result.Message}");
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                Log($"Error in TableImporter for {table.FullName}: {ex.Message}");
                throw;
            }
        }
        
        // Helper method to scan for tables not in metadata
        private async Task ScanForUnreferencedTables(string dataDir, List<TableSchema> tablesToImport)
        {
            try
            {
                // Debug logging for table filtering
                Log($"[SCAN DEBUG] _options.Tables is null: {_options.Tables == null}");
                if (_options.Tables != null)
                {
                    Log($"[SCAN DEBUG] _options.Tables.Count: {_options.Tables.Count}");
                    Log($"[SCAN DEBUG] _options.Tables contents: [{string.Join(", ", _options.Tables)}]");
                }

                // If specific tables are being imported (filtering is active), skip scanning for unreferenced tables
                // This prevents adding back tables that were intentionally filtered out
                if (_options.Tables != null && _options.Tables.Any())
                {
                    Log($"Table filtering is active - skipping scan for unreferenced tables to respect filter");
                    Log($"Only importing specified tables: {string.Join(", ", _options.Tables)}");
                    return;
                }

                // Get all data files in the directory
                var allDataFiles = Directory.GetFiles(dataDir, "*.bin")
                    .Where(f => !Path.GetFileName(f).Contains("_batch"))  // Exclude batch files
                    .ToList();

                Log($"Found {allDataFiles.Count} data files in directory");

                // Check which files correspond to tables not in the import list
                foreach (var dataFile in allDataFiles)
                {
                    string fileName = Path.GetFileNameWithoutExtension(dataFile);

                    // Try to extract schema and table name from file name (format: schema_table.bin)
                    if (fileName.Contains("_"))
                    {
                        string[] parts = fileName.Split('_', 2);
                        string schema = parts[0];
                        string tableName = parts[1];

                        // Check if this table is already in the import list
                        bool tableExists = tablesToImport.Any(t =>
                            t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase) &&
                            t.Schema?.Equals(schema, StringComparison.OrdinalIgnoreCase) == true);

                        if (!tableExists)
                        {
                            Log($"[CRITICAL] Found data file for table not in metadata: {fileName}", true);

                            // Try to discover table schema from data file
                            try
                            {
                                var tableData = await ReadTableDataFromFile(dataFile);
                                if (tableData != null && tableData.Schema != null)
                                {
                                    // Use the schema from the data file
                                    tablesToImport.Add(tableData.Schema);
                                    Log($"[CRITICAL] Successfully added {schema}.{tableName} to import list from data file", true);
                                }
                                else
                                {
                                    // Create a minimal schema
                                    var minimalSchema = new TableSchema
                                    {
                                        Name = tableName,
                                        Schema = schema
                                    };
                                    tablesToImport.Add(minimalSchema);
                                    Log($"[CRITICAL] Added minimal schema for {schema}.{tableName} to import list", true);
                                }
                            }
                            catch (Exception ex)
                            {
                                Log($"[CRITICAL] Error reading table data from file {fileName}: {ex.Message}", true);
                                // Still create a minimal schema as fallback
                                var minimalSchema = new TableSchema
                                {
                                    Name = tableName,
                                    Schema = schema
                                };
                                tablesToImport.Add(minimalSchema);
                                Log($"[CRITICAL] Added minimal fallback schema for {schema}.{tableName} to import list", true);
                            }
                        }
                    }
                }

                Log($"Total tables to import after scanning: {tablesToImport.Count}");
            }
            catch (Exception ex)
            {
                Log($"Error scanning for unreferenced tables: {ex.Message}");
            }
        }
        
        // Helper method to read table data from a file
        private async Task<TableData?> ReadTableDataFromFile(string filePath)
        {
            try
            {
                // Check file size
                var fileInfo = new FileInfo(filePath);
                Log($"Reading table data from file: {Path.GetFileName(filePath)}, size: {fileInfo.Length} bytes");
                
                if (fileInfo.Length < 10)
                {
                    Log($"File is too small to be valid: {fileInfo.Length} bytes");
                    return null;
                }
                
                // Read file bytes
                byte[] fileBytes = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
                
                // Check if it's GZip compressed
                bool isGZip = fileBytes.Length > 2 && fileBytes[0] == 0x1F && fileBytes[1] == 0x8B;
                
                // Decompress if needed
                byte[] dataBytes;
                if (isGZip)
                {
                    using (var ms = new MemoryStream(fileBytes))
                    using (var gzipStream = new GZipStream(ms, System.IO.Compression.CompressionMode.Decompress))
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
                
                // Try to deserialize
                try
                {
                    // Try with standard options first
                    var options = MessagePackSerializerOptions.Standard;
                    var tableData = MessagePackSerializer.Deserialize<TableData>(dataBytes, options);
                    return tableData;
                }
                catch
                {
                    // Try with contractless resolver as fallback
                    try
                    {
                        var fallbackOptions = MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);
                        var tableData = MessagePackSerializer.Deserialize<TableData>(dataBytes, fallbackOptions);
                        return tableData;
                    }
                    catch (Exception ex)
                    {
                        Log($"Failed to deserialize data file: {ex.Message}");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error reading table data from file: {ex.Message}");
                return null;
            }
        }

        private List<TableSchema> ReorderTablesByDependency(List<TableSchema> tables, Dictionary<string, List<string>> dependencyOrder)
        {
            var result = new List<TableSchema>();
            var tablesByFullName = tables.ToDictionary(t => t.FullName ?? string.Empty);
            
            foreach (var level in dependencyOrder.OrderBy(kv => int.TryParse(kv.Key, out int result) ? result : 0))
            {
                foreach (var tableName in level.Value)
                {
                    if (tablesByFullName.TryGetValue(tableName, out var table))
                    {
                        result.Add(table);
                    }
                }
            }
            
            // Add any tables not in dependency order at the end
            foreach (var table in tables)
            {
                if (!result.Contains(table))
                {
                    result.Add(table);
                }
            }
            
            return result;
        }
    }

    public class ImportOptions
    {
        public List<string>? Tables { get; set; }
        public bool CreateSchema { get; set; } = true;
        public bool CreateForeignKeys { get; set; } = true;
        public bool SchemaOnly { get; set; } = false;
        public bool ContinueOnError { get; set; } = false;
        public int BatchSize { get; set; } = 100000;
        public bool UseDependencyOrder { get; set; } = true;
        public bool OverwriteExistingTables { get; set; } = false;
    }
}