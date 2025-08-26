using CommandLine;
using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Providers;
using DatabaseMigrationTool.Services;
using DatabaseMigrationTool.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace DatabaseMigrationTool.Commands
{
    [Verb("validate-ei", HelpText = "Validate export/import of a single table for troubleshooting")]
    public class ValidateExportImportOptions
    {
        [Option('p', "provider", Required = true, HelpText = "Database provider (sqlserver, mysql, postgresql)")]
        public string Provider { get; set; } = string.Empty;
        
        [Option('c', "connection", Required = true, HelpText = "Connection string")]
        public string ConnectionString { get; set; } = string.Empty;
        
        [Option('t', "table", Required = true, HelpText = "Table to validate (include schema prefix if needed, e.g. 'dbo.POSMisc')")]
        public string TableName { get; set; } = string.Empty;
        
        [Option('o', "output", Required = false, HelpText = "Output directory path (default: temp directory)")]
        public string OutputPath { get; set; } = Path.Combine(Path.GetTempPath(), "DatabaseMigrationTool_Validation");
        
        [Option('v', "verbose", Default = false, HelpText = "Show verbose output")]
        public bool Verbose { get; set; }
        
        [Option("clean", Default = true, HelpText = "Clean output directory after validation")]
        public bool CleanOutput { get; set; } = true;
        
        [Option("save-file", Default = false, HelpText = "Save the exported data file for inspection")]
        public bool SaveFile { get; set; } = false;
    }
    
    public class ValidateExportImportCommand
    {
        public static async Task<int> Execute(ValidateExportImportOptions options)
        {
            Console.WriteLine("======================================================");
            Console.WriteLine("DATABASE MIGRATION TOOL - EXPORT/IMPORT VALIDATION");
            Console.WriteLine("======================================================");
            Console.WriteLine($"Provider: {options.Provider}");
            Console.WriteLine($"Table: {options.TableName}");
            Console.WriteLine($"Output path: {options.OutputPath}");
            Console.WriteLine("------------------------------------------------------");
            
            try
            {
                // Input validation
                if (string.IsNullOrWhiteSpace(options.Provider))
                {
                    Console.WriteLine("Error: Provider is required.");
                    return 1;
                }
                
                if (string.IsNullOrWhiteSpace(options.ConnectionString))
                {
                    Console.WriteLine("Error: Connection string is required.");
                    return 1;
                }
                
                if (string.IsNullOrWhiteSpace(options.TableName))
                {
                    Console.WriteLine("Error: Table name is required.");
                    return 1;
                }
                
                // Validate output path
                try
                {
                    Path.GetFullPath(options.OutputPath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: Invalid output path '{options.OutputPath}': {ex.Message}");
                    return 1;
                }
                
                // Extract schema and table name
                string schemaName = "dbo";
                string tableName = options.TableName;
                
                if (options.TableName.Contains("."))
                {
                    var parts = options.TableName.Split('.');
                    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                    {
                        Console.WriteLine("Error: Invalid table name format. Use 'schema.table' or 'table'.");
                        return 1;
                    }
                    schemaName = parts[0];
                    tableName = parts[1];
                }
                
                // Create provider
                IDatabaseProvider provider = DatabaseProviderFactory.Create(options.Provider);
                
                // Configure the output directory
                Directory.CreateDirectory(options.OutputPath);
                string dataDir = Path.Combine(options.OutputPath, "data");
                Directory.CreateDirectory(dataDir);
                
                // Special logging
                Action<string> log = (message) => {
                    if (options.Verbose || message.Contains("ERROR") || message.StartsWith("###"))
                    {
                        Console.WriteLine(message);
                    }
                };
                
                // Set logger on provider
                provider.SetLogger(log);
                
                // STEP 1: Verify source table exists and get row count
                using (var connection = provider.CreateConnection(options.ConnectionString))
                {
                    await connection.OpenAsync();
                    
                    Console.WriteLine("Step 1: Verifying source table...");
                    
                    // Get table schema
                    var tables = await provider.GetTablesAsync(connection, new[] { tableName });
                    if (tables.Count == 0)
                    {
                        Console.WriteLine($"ERROR: Table '{options.TableName}' not found in source database");
                        return 1;
                    }
                    
                    var tableSchema = tables.Find(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase) && 
                        t.Schema?.Equals(schemaName, StringComparison.OrdinalIgnoreCase) == true);
                    
                    if (tableSchema == null)
                    {
                        Console.WriteLine($"ERROR: Table '{options.TableName}' not found in source database");
                        return 1;
                    }
                    
                    Console.WriteLine($"Found table: {tableSchema.Schema}.{tableSchema.Name} with {tableSchema.Columns.Count} columns");
                    
                    // Get row count
                    int rowCount = await provider.GetTableRowCountAsync(connection, tableSchema.Schema ?? "dbo", tableSchema.Name);
                    Console.WriteLine($"Source table row count: {rowCount}");
                    
                    // STEP 2: Export the table
                    Console.WriteLine("\nStep 2: Exporting table...");
                    
                    var exportOptions = new ExportOptions
                    {
                        Tables = new List<string> { tableSchema.Name },
                        BatchSize = 1000, // Use smaller batch for testing
                        OutputDirectory = options.OutputPath
                    };
                    
                    var exporter = new DatabaseExporter(provider, connection, exportOptions);
                    exporter.SetLogger(log);
                    
                    try
                    {
                        await exporter.ExportSingleTableAsync(tableSchema, options.OutputPath);
                        Console.WriteLine($"Export completed successfully!");
                        
                        // Check for the exported file
                        string exportedFilePath = Path.Combine(dataDir, $"{schemaName}_{tableName}.bin");
                        string batchPattern = Path.Combine(dataDir, $"{schemaName}_{tableName}_batch*.bin");
                        var batchFiles = Directory.GetFiles(dataDir, $"{schemaName}_{tableName}_batch*.bin");
                        
                        if (File.Exists(exportedFilePath))
                        {
                            var fileInfo = new FileInfo(exportedFilePath);
                            Console.WriteLine($"Exported file: {exportedFilePath}");
                            Console.WriteLine($"File size: {fileInfo.Length} bytes");
                            
                            // Run diagnostic on the file
                            Console.WriteLine("\nFile diagnostic:");
                            var diagnostic = await DiagnosticImport.ValidateImportFileAsync(exportedFilePath);
                            Console.WriteLine($"Compressed: {diagnostic.IsCompressed}");
                            Console.WriteLine($"Successfully deserialized: {diagnostic.Deserialized}");
                            Console.WriteLine($"Row count in file: {diagnostic.RowCount}");
                            
                            if (diagnostic.RowCount == 0)
                            {
                                Console.WriteLine("WARNING: Exported file contains 0 rows!");
                            }
                            
                            if (diagnostic.RowCount != rowCount)
                            {
                                Console.WriteLine($"WARNING: Exported row count ({diagnostic.RowCount}) doesn't match source table ({rowCount})");
                            }
                        }
                        else if (batchFiles.Length > 0)
                        {
                            Console.WriteLine($"Exported as {batchFiles.Length} batch files:");
                            
                            foreach (var batchFile in batchFiles)
                            {
                                var fileInfo = new FileInfo(batchFile);
                                Console.WriteLine($"- {Path.GetFileName(batchFile)}: {fileInfo.Length} bytes");
                                
                                // Run diagnostic on the first batch file
                                if (batchFile == batchFiles[0])
                                {
                                    Console.WriteLine("\nFirst batch file diagnostic:");
                                    var diagnostic = await DiagnosticImport.ValidateImportFileAsync(batchFile);
                                    Console.WriteLine($"Compressed: {diagnostic.IsCompressed}");
                                    Console.WriteLine($"Successfully deserialized: {diagnostic.Deserialized}");
                                    Console.WriteLine($"Row count in batch: {diagnostic.RowCount}");
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("WARNING: No export files found!");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ERROR: Export failed: {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                        }
                        return 1;
                    }
                    
                    // Create a temporary database for import testing
                    string tempDbName = $"TempDB_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                    string tempConnectionString = options.ConnectionString;
                    
                    // Try to create temporary database if provider supports it
                    bool usingTempDb = false;
                    try
                    {
                        if (options.Provider.Equals("sqlserver", StringComparison.OrdinalIgnoreCase))
                        {
                            // Try to create a temp database for SQL Server
                            Console.WriteLine("\nCreating temporary database for import test...");
                            
                            // Extract master connection string
                            string masterConnectionString = options.ConnectionString;
                            if (masterConnectionString.Contains("Database=") || masterConnectionString.Contains("Initial Catalog="))
                            {
                                masterConnectionString = masterConnectionString
                                    .Replace(new System.Text.RegularExpressions.Regex(@"(Database|Initial Catalog)\s*=\s*[^;]+").Match(masterConnectionString).Value, 
                                             $"Database=master");
                            }
                            
                            using (var masterConnection = provider.CreateConnection(masterConnectionString))
                            {
                                await masterConnection.OpenAsync();
                                
                                // Create the temp database
                                using (var cmd = masterConnection.CreateCommand())
                                {
                                    cmd.CommandText = $"CREATE DATABASE [{tempDbName}]";
                                    await cmd.ExecuteNonQueryAsync();
                                    Console.WriteLine($"Created temporary database: {tempDbName}");
                                    
                                    // Update connection string
                                    tempConnectionString = options.ConnectionString
                                        .Replace(new System.Text.RegularExpressions.Regex(@"(Database|Initial Catalog)\s*=\s*[^;]+").Match(options.ConnectionString).Value, 
                                                 $"Database={tempDbName}");
                                    
                                    usingTempDb = true;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"WARNING: Could not create temporary database: {ex.Message}");
                        Console.WriteLine("Will use original connection for import test");
                        tempConnectionString = options.ConnectionString;
                    }
                    
                    // STEP 3: Import the table
                    try
                    {
                        Console.WriteLine("\nStep 3: Importing table...");
                        
                        // Create new connection to temp or original db
                        using (var importConnection = provider.CreateConnection(tempConnectionString))
                        {
                            await importConnection.OpenAsync();
                            
                            var importOptions = new ImportOptions
                            {
                                Tables = new List<string> { tableSchema.Name },
                                BatchSize = 100, // Use even smaller batch for testing
                                CreateSchema = true,
                                CreateForeignKeys = false, // Skip foreign keys for this test
                                ContinueOnError = false
                            };
                            
                            var importer = new DatabaseImporter(provider, importConnection, importOptions);
                            await importer.ImportAsync(options.OutputPath);
                            
                            Console.WriteLine($"Import completed!");
                            
                            // Check imported table
                            int importedRowCount = await provider.GetTableRowCountAsync(importConnection, tableSchema.Schema ?? "dbo", tableSchema.Name);
                            Console.WriteLine($"Imported table row count: {importedRowCount}");
                            
                            if (importedRowCount != rowCount)
                            {
                                Console.WriteLine($"WARNING: Imported row count ({importedRowCount}) doesn't match source table ({rowCount})!");
                                return 1;
                            }
                            else
                            {
                                Console.WriteLine($"SUCCESS! Row count in imported table matches source ({rowCount} rows)");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ERROR: Import failed: {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                        }
                        return 1;
                    }
                    
                    // Clean up temp database if used
                    if (usingTempDb)
                    {
                        try
                        {
                            Console.WriteLine($"\nCleaning up temporary database {tempDbName}...");
                            
                            string masterConnectionString = options.ConnectionString;
                            if (masterConnectionString.Contains("Database=") || masterConnectionString.Contains("Initial Catalog="))
                            {
                                masterConnectionString = masterConnectionString
                                    .Replace(new System.Text.RegularExpressions.Regex(@"(Database|Initial Catalog)\s*=\s*[^;]+").Match(masterConnectionString).Value, 
                                             $"Database=master");
                            }
                            
                            using (var masterConnection = provider.CreateConnection(masterConnectionString))
                            {
                                await masterConnection.OpenAsync();
                                
                                // Drop the temp database
                                using (var cmd = masterConnection.CreateCommand())
                                {
                                    cmd.CommandText = $@"
                                        IF EXISTS (SELECT name FROM sys.databases WHERE name = '{tempDbName}')
                                        BEGIN
                                            ALTER DATABASE [{tempDbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                                            DROP DATABASE [{tempDbName}];
                                        END";
                                    await cmd.ExecuteNonQueryAsync();
                                    Console.WriteLine($"Temporary database {tempDbName} removed");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"WARNING: Failed to clean up temporary database: {ex.Message}");
                        }
                    }
                }
                
                // Clean up the output directory if requested
                if (options.CleanOutput && !options.SaveFile)
                {
                    try
                    {
                        Console.WriteLine("\nCleaning up temporary files...");
                        Directory.Delete(options.OutputPath, true);
                        Console.WriteLine("Temporary files removed");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"WARNING: Failed to clean up temporary files: {ex.Message}");
                    }
                }
                
                Console.WriteLine("\n======================================================");
                Console.WriteLine("VALIDATION COMPLETED SUCCESSFULLY!");
                Console.WriteLine("======================================================");
                
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nERROR: Validation failed: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                }
                return 1;
            }
        }
    }
}