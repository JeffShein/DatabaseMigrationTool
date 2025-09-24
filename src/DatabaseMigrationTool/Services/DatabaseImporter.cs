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
        /// Handles Firebird table recreation with proper schema difference detection
        /// </summary>
        private async Task HandleFirebirdTableRecreationAsync(string tableName)
        {
            Log($"Firebird embedded mode - checking if schema recreation is needed for {tableName}");

            try
            {
                // Get existing table schema from database
                var existingTables = await _provider.GetTablesAsync(_connection, new[] { tableName });
                var existingTable = existingTables.FirstOrDefault();

                if (existingTable == null)
                {
                    Log($"Table {tableName} doesn't exist - will be created fresh");
                    return;
                }

                Log($"Table {tableName} exists - attempting complete recreation for schema consistency");

                // Step 1: Drop all foreign keys that reference this table from other tables
                await DropReferencingForeignKeysAsync(tableName).ConfigureAwait(false);

                // Step 2: Drop all dependencies of this table (foreign keys, indexes, constraints)
                await DropFirebirdTableDependenciesAsync(tableName).ConfigureAwait(false);

                // Step 3: Clear all data first (safer than DROP TABLE)
                await ClearFirebirdTableDataAsync(tableName).ConfigureAwait(false);

                // Step 4: Try to drop the table structure
                await AttemptFirebirdTableDropAsync(tableName).ConfigureAwait(false);

                Log($"Successfully prepared {tableName} for recreation");
            }
            catch (Exception ex)
            {
                Log($"Error during Firebird table recreation for {tableName}: {ex.Message}");
                Log($"Will attempt to continue with existing table structure - data has been cleared");
            }
        }

        /// <summary>
        /// Drops foreign keys from other tables that reference the specified table
        /// </summary>
        private async Task DropReferencingForeignKeysAsync(string tableName)
        {
            Log($"Dropping foreign keys that reference table {tableName}");

            try
            {
                // Get all tables to find foreign keys that reference our table
                var allTables = await _provider.GetTablesAsync(_connection);

                foreach (var table in allTables)
                {
                    if (table.ForeignKeys?.Any() == true)
                    {
                        foreach (var fk in table.ForeignKeys)
                        {
                            if (fk.ReferencedTableName?.Equals(tableName, StringComparison.OrdinalIgnoreCase) == true)
                            {
                                try
                                {
                                    string dropFkSql = $"ALTER TABLE \"{table.Name}\" DROP CONSTRAINT \"{fk.Name}\"";
                                    using var command = _connection.CreateCommand();
                                    command.CommandText = dropFkSql;
                                    await command.ExecuteNonQueryAsync();
                                    Log($"Dropped referencing foreign key {fk.Name} from table {table.Name}");
                                }
                                catch (Exception ex)
                                {
                                    Log($"Could not drop referencing foreign key {fk.Name}: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error dropping referencing foreign keys: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears data from Firebird table using DELETE
        /// </summary>
        private async Task ClearFirebirdTableDataAsync(string tableName)
        {
            Log($"Clearing data from Firebird table {tableName}");

            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = $"DELETE FROM \"{tableName}\"";
                int rowsDeleted = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                Log($"Successfully cleared {rowsDeleted} rows from Firebird table {tableName}");
            }
            catch (Exception ex)
            {
                Log($"Could not clear data from Firebird table {tableName}: {ex.Message}");
                // Don't throw - continue with import even if data clearing failed
            }
        }

        /// <summary>
        /// Attempts to drop Firebird table after dependencies are cleared
        /// </summary>
        private async Task AttemptFirebirdTableDropAsync(string tableName)
        {
            Log($"Attempting to drop Firebird table structure for {tableName}");

            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = $"DROP TABLE \"{tableName}\"";
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                Log($"Successfully dropped Firebird table {tableName}");
            }
            catch (Exception ex)
            {
                Log($"Could not drop Firebird table {tableName}: {ex.Message}");
                Log($"Table structure will remain - continuing with existing schema");
            }
        }

        /// <summary>
        /// Drops all dependencies of a Firebird table (foreign keys, indexes, constraints)
        /// </summary>
        private async Task DropFirebirdTableDependenciesAsync(string tableName)
        {
            Log($"Dropping dependencies for Firebird table {tableName}");

            try
            {
                // Get the table schema to see what dependencies exist
                var existingTables = await _provider.GetTablesAsync(_connection, new[] { tableName });
                var table = existingTables.FirstOrDefault();

                if (table == null)
                {
                    Log($"Table {tableName} not found - no dependencies to drop");
                    return;
                }

                // Drop foreign keys first (they reference other tables)
                foreach (var fk in table.ForeignKeys)
                {
                    try
                    {
                        string dropFkSql = $"ALTER TABLE \"{tableName}\" DROP CONSTRAINT \"{fk.Name}\"";
                        using var command = _connection.CreateCommand();
                        command.CommandText = dropFkSql;
                        await command.ExecuteNonQueryAsync();
                        Log($"Dropped foreign key {fk.Name}");
                    }
                    catch (Exception ex)
                    {
                        Log($"Could not drop foreign key {fk.Name}: {ex.Message}");
                    }
                }

                // Drop indexes (except primary key indexes which will be dropped with constraints)
                foreach (var index in table.Indexes)
                {
                    try
                    {
                        string dropIndexSql = $"DROP INDEX \"{index.Name}\"";
                        using var command = _connection.CreateCommand();
                        command.CommandText = dropIndexSql;
                        await command.ExecuteNonQueryAsync();
                        Log($"Dropped index {index.Name}");
                    }
                    catch (Exception ex)
                    {
                        Log($"Could not drop index {index.Name}: {ex.Message}");
                    }
                }

                // Drop constraints (except primary key which will be dropped with table)
                foreach (var constraint in table.Constraints.Where(c => c.Type != "PRIMARY KEY"))
                {
                    try
                    {
                        string dropConstraintSql = $"ALTER TABLE \"{tableName}\" DROP CONSTRAINT \"{constraint.Name}\"";
                        using var command = _connection.CreateCommand();
                        command.CommandText = dropConstraintSql;
                        await command.ExecuteNonQueryAsync();
                        Log($"Dropped constraint {constraint.Name}");
                    }
                    catch (Exception ex)
                    {
                        Log($"Could not drop constraint {constraint.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error getting table dependencies for {tableName}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Drops a Firebird table after dependencies have been removed
        /// </summary>
        private async Task DropFirebirdTableAsync(string tableName)
        {
            Log($"Dropping Firebird table {tableName}");

            string dropSql = $"DROP TABLE \"{tableName}\"";
            using var command = _connection.CreateCommand();
            command.CommandText = dropSql;
            await command.ExecuteNonQueryAsync();
            Log($"Successfully dropped table {tableName}");
        }

        /// <summary>
        /// Drops a table if it exists
        /// </summary>
        private async Task DropTableAsync(string tableName)
        {
            try
            {
                Log($"Preparing to drop table {tableName}");

                // For Firebird, handle schema differences and dependencies properly
                if (_provider.ProviderName.Equals("Firebird", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleFirebirdTableRecreationAsync(tableName).ConfigureAwait(false);
                    return;
                }
                else
                {
                    // For non-Firebird providers, use the original approach
                    string dropSql = $"DROP TABLE \"{tableName}\"";
                    Log($"Executing DROP TABLE for {tableName}: {dropSql}");

                    using var command = _connection.CreateCommand();
                    command.CommandText = dropSql;
                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);

                    Log($"Successfully dropped table {tableName}");
                }
            }
            catch (Exception ex)
            {
                Log($"Error dropping table {tableName}: {ex.Message}");

                // For Firebird connection lock errors, provide specific guidance
                if (ex.Message.Contains("lock conflict") || ex.Message.Contains("object TABLE") && ex.Message.Contains("is in use"))
                {
                    Log($"Detected Firebird metadata lock conflict for table {tableName}. This usually indicates another connection is still active.");
                    Log($"The table data has been cleared and dependencies dropped - proceeding with import");

                    // Don't throw for Firebird lock conflicts - the table has been prepared for import
                    return;
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
            Log($"Initial table list loaded: {tablesToImport.Count} tables");
            Log($"Tables in export: {string.Join(", ", tablesToImport.Select(t => t.Name))}");

            if (_options.UseDependencyOrder && export.DependencyOrder?.Any() == true)
            {
                Log("Using dependency order for import");
                tablesToImport = ReorderTablesByDependency(export.Schemas ?? new List<TableSchema>(), export.DependencyOrder);
            }

            // Filter tables if specified - do this BEFORE schema creation
            if (_options.Tables != null && _options.Tables.Any())
            {
                Log($"Table filtering requested. Filter list: {string.Join(", ", _options.Tables)}");
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

                Log($"After filtering: {tablesToImport.Count} tables selected");
                Log($"Filtered tables: {string.Join(", ", tablesToImport.Select(t => t.Name))}");

                // Log the filter results
                Log($"[CRITICAL] After filtering, {tablesToImport.Count} tables remain in import list", true);
                foreach (var table in tablesToImport)
                {
                    Log($"[CRITICAL] - Will import: {table.Schema}.{table.Name}", true);
                }
            }
            else
            {
                Log($"No table filtering specified - importing all {tablesToImport.Count} tables");
                Log($"Tables to import: {string.Join(", ", tablesToImport.Select(t => t.Name))}");
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

                                // For Firebird, ensure any metadata cursors from GetTablesAsync are released
                                if (_provider.ProviderName.Equals("Firebird", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Clear references and force garbage collection
                                    existingTables = null;
                                    GC.Collect();
                                    GC.WaitForPendingFinalizers();

                                    // Small delay to ensure Firebird releases metadata locks
                                    await Task.Delay(500);
                                }

                                await DropTableAsync(table.Name).ConfigureAwait(false);

                                // Log success - but for Firebird this will be skipped internally
                                if (_provider.ProviderName.Equals("Firebird", StringComparison.OrdinalIgnoreCase))
                                {
                                    Log($"Table {table.FullName} drop was skipped (Firebird embedded mode)");
                                }
                                else
                                {
                                    Log($"Table {table.FullName} dropped successfully");
                                }
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
                    catch (Exception ex) when (_provider.ProviderName.Equals("Firebird", StringComparison.OrdinalIgnoreCase) &&
                                             (ex.Message.Contains("already exists") || ex.Message.Contains("lock conflict") || ex.Message.Contains("is in use")))
                    {
                        // For Firebird, if table creation fails due to existing table or lock conflicts,
                        // log the issue but continue - the table might already exist from a previous run
                        Log($"Firebird table creation issue for {table.FullName}: {ex.Message}");
                        Log($"Continuing import - table may already exist or be locked by another process");

                        // Don't throw - continue with the next steps (indexes, etc.)
                        // This allows the import process to continue even if table creation had conflicts
                    }
                    catch (Exception ex)
                    {
                        Log($"[CRITICAL] ERROR creating table structure: {ex.Message}", true);
                        throw; // Re-throw to maintain original behavior for non-Firebird or other errors
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