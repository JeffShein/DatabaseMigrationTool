using CommandLine;
using DatabaseMigrationTool.Commands;
using DatabaseMigrationTool.Providers;
using DatabaseMigrationTool.Services;
using DatabaseMigrationTool.Utilities;
using DatabaseMigrationTool.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Data.Common;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using SharpCompress.Compressors.BZip2;
using SharpCompress.Compressors;
using MessagePack;

namespace DatabaseMigrationTool
{
    public class Program
    {
        private static IServiceProvider? _serviceProvider;
        
        // Entry point that will handle both GUI and console modes
        [STAThread] // Required for WPF
        public static void Main(string[] args)
        {
            // Initialize dependency injection
            _serviceProvider = ConfigureServices();
            
            // Initialize error handling system
            ErrorHandler.Initialize();
            ErrorHandler.RegisterUnhandledExceptionHandlers();
            
            try
            {
            // Raw file dumper for examining binary files
            if (args.Length >= 2 && args[0] == "dump-file")
            {
                Console.WriteLine("===========================================");
                Console.WriteLine("FILE DUMPER TOOL STARTED");
                Console.WriteLine("===========================================");
                string filePath = args[1];
                Console.WriteLine($"Dumping file content: {filePath}");
                
                try
                {
                    if (!File.Exists(filePath))
                    {
                        Console.WriteLine("File does not exist!");
                        return;
                    }
                    
                    var fileInfo = new FileInfo(filePath);
                    Console.WriteLine($"File size: {fileInfo.Length} bytes");
                    
                    // Read the first few bytes to check header
                    byte[] header = new byte[Math.Min(16, (int)fileInfo.Length)];
                    using (var stream = File.OpenRead(filePath))
                    {
                        int bytesRead = 0;
                        int totalBytesRead = 0;
                        int bytesToRead = header.Length;
                        
                        // Read exact number of bytes or until end of file
                        while (totalBytesRead < bytesToRead && 
                               (bytesRead = stream.Read(header, totalBytesRead, bytesToRead - totalBytesRead)) > 0)
                        {
                            totalBytesRead += bytesRead;
                        }
                        
                        Console.WriteLine($"Read {totalBytesRead} bytes from file header");
                    }
                    
                    // Display header bytes
                    Console.WriteLine("File header (hex):");
                    Console.WriteLine(BitConverter.ToString(header));
                    
                    // Try to identify file type
                    if (header.Length >= 2)
                    {
                        if (header[0] == 0x1F && header[1] == 0x8B)
                        {
                            Console.WriteLine("File appears to be GZip compressed");
                            
                            // Try to decompress some data
                            try
                            {
                                using var fileStream = File.OpenRead(filePath);
                                using var gzipStream = new GZipStream(fileStream, System.IO.Compression.CompressionMode.Decompress);
                                using var outputStream = new MemoryStream();
                                
                                gzipStream.CopyTo(outputStream);
                                Console.WriteLine($"Successfully decompressed. Uncompressed size: {outputStream.Length} bytes");
                                
                                // Show some bytes from the decompressed stream
                                outputStream.Position = 0;
                                byte[] sampleBytes = new byte[Math.Min(32, (int)outputStream.Length)];
                                outputStream.Read(sampleBytes, 0, sampleBytes.Length);
                                
                                Console.WriteLine("Sample of decompressed data (hex):");
                                Console.WriteLine(BitConverter.ToString(sampleBytes));
                                
                                // Try to interpret as UTF-8 text
                                outputStream.Position = 0;
                                byte[] textBytes = new byte[Math.Min(100, (int)outputStream.Length)];
                                outputStream.Read(textBytes, 0, textBytes.Length);
                                string text = System.Text.Encoding.UTF8.GetString(textBytes)
                                    .Replace("\0", "[NULL]")
                                    .Replace("\r", "[CR]")
                                    .Replace("\n", "[LF]");
                                
                                Console.WriteLine("Sample of decompressed data (interpreted as text):");
                                Console.WriteLine(text);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error during decompression: {ex.Message}");
                            }
                        }
                        else if (header.Length >= 3 && header[0] == 'B' && header[1] == 'Z' && header[2] == 'h')
                        {
                            Console.WriteLine("File appears to be BZip2 compressed");
                            
                            // Try to decompress BZip2 data
                            try
                            {
                                using var fileStream = File.OpenRead(filePath);
                                using var bzipStream = new SharpCompress.Compressors.BZip2.BZip2Stream(fileStream, SharpCompress.Compressors.CompressionMode.Decompress, true);
                                using var outputStream = new MemoryStream();
                                
                                bzipStream.CopyTo(outputStream);
                                Console.WriteLine($"Successfully decompressed. Uncompressed size: {outputStream.Length} bytes");
                                
                                // Show some bytes from the decompressed stream
                                outputStream.Position = 0;
                                byte[] sampleBytes = new byte[Math.Min(32, (int)outputStream.Length)];
                                outputStream.Read(sampleBytes, 0, sampleBytes.Length);
                                
                                Console.WriteLine("Sample of decompressed data (hex):");
                                Console.WriteLine(BitConverter.ToString(sampleBytes));
                                
                                // Try to deserialize as DatabaseExport if this looks like metadata
                                if (filePath.EndsWith("metadata.bin", StringComparison.OrdinalIgnoreCase))
                                {
                                    try
                                    {
                                        outputStream.Position = 0;
                                        var databaseExport = MessagePack.MessagePackSerializer.Deserialize<DatabaseMigrationTool.Models.DatabaseExport>(outputStream);
                                        
                                        Console.WriteLine("\nDeserialized DatabaseExport metadata:");
                                        Console.WriteLine($"Database Name: {databaseExport.DatabaseName}");
                                        Console.WriteLine($"Export Date: {databaseExport.ExportDate}");
                                        Console.WriteLine($"Format Version: {databaseExport.FormatVersion}");
                                        Console.WriteLine($"Number of Tables: {databaseExport.Schemas?.Count ?? 0}");
                                        
                                        if (databaseExport.Schemas != null)
                                        {
                                            foreach (var table in databaseExport.Schemas)
                                            {
                                                Console.WriteLine($"\nTable: {table.FullName}");
                                                Console.WriteLine($"  Columns: {table.Columns?.Count ?? 0}");
                                                
                                                if (table.Columns != null)
                                                {
                                                    foreach (var column in table.Columns.OrderBy(c => c.OrdinalPosition))
                                                    {
                                                        string nullable = column.IsNullable ? "NULL" : "NOT NULL";
                                                        string pk = column.IsPrimaryKey ? " PK" : "";
                                                        string identity = column.IsIdentity ? " IDENTITY" : "";
                                                        
                                                        // Check for empty column names
                                                        string columnName = string.IsNullOrEmpty(column.Name) ? "[EMPTY COLUMN NAME]" : column.Name;
                                                        if (string.IsNullOrEmpty(column.Name))
                                                        {
                                                            Console.WriteLine($"    *** WARNING: {columnName} ({column.DataType}) {nullable}{pk}{identity} - EMPTY COLUMN NAME DETECTED ***");
                                                        }
                                                        else
                                                        {
                                                            Console.WriteLine($"    {columnName} ({column.DataType}) {nullable}{pk}{identity}");
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception deserEx)
                                    {
                                        Console.WriteLine($"Failed to deserialize as DatabaseExport: {deserEx.Message}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error during BZip2 decompression: {ex.Message}");
                            }
                        }
                        else
                        {
                            Console.WriteLine("File does not appear to be GZip or BZip2 compressed");
                        }
                        
                        // Try to interpret header as text
                        string headerText = System.Text.Encoding.UTF8.GetString(header)
                            .Replace("\0", "[NULL]")
                            .Replace("\r", "[CR]")
                            .Replace("\n", "[LF]");
                        
                        Console.WriteLine("Header as text:");
                        Console.WriteLine(headerText);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during file dump: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
                
                Console.WriteLine("===========================================");
                Console.WriteLine("FILE DUMPER TOOL FINISHED");
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey();
                return;
            }
            
            // Special debug commands for analyzing/importing data files
            if (args.Length >= 2 && args[0] == "debug-analyze-file")
            {
                // Extract file path from args
                string filePath = args[1];
                Console.WriteLine($"Running diagnostic analysis on file: {filePath}");
                DebugTools.AnalyzeDataFileAsync(filePath).Wait();
                return;
            }
            
            // Direct export-import command to bypass batch processing completely
            if (args.Length >= 5 && args[0] == "direct-transfer")
            {
                Console.WriteLine("===========================================");
                Console.WriteLine("DIRECT TABLE TRANSFER UTILITY STARTED");
                Console.WriteLine("===========================================");
                
                // Extract arguments
                string providerName = args[1];
                string sourceConnectionString = args[2];
                string destConnectionString = args[3];
                string tableName = args[4];
                
                // Optional limit argument
                int? limit = null;
                if (args.Length >= 6 && int.TryParse(args[5], out int parsedLimit))
                {
                    limit = parsedLimit;
                }
                
                Console.WriteLine($"Provider: {providerName}");
                Console.WriteLine($"Table: {tableName}");
                Console.WriteLine($"Limit: {limit?.ToString() ?? "No limit"}");
                
                try
                {
                    // Create provider
                    IDatabaseProvider provider;
                    switch (providerName.ToLowerInvariant())
                    {
                        case "sqlserver":
                            provider = new SqlServerProvider();
                            break;
                        case "mysql":
                            provider = new MySqlProvider();
                            break;
                        case "postgresql":
                            provider = new PostgreSqlProvider();
                            break;
                        case "firebird":
                            provider = new FirebirdProvider();
                            break;
                        default:
                            Console.WriteLine($"Unknown provider: {providerName}");
                            return;
                    }
                    
                    // Extract schema and table name
                    string schemaName = "dbo";
                    string tableNameOnly = tableName;
                    if (tableName.Contains("."))
                    {
                        var parts = tableName.Split('.');
                        schemaName = parts[0];
                        tableNameOnly = parts[1];
                    }
                    
                    Console.WriteLine($"Running direct transfer for {schemaName}.{tableNameOnly}...");
                    DirectTransferUtility.DirectTransferTableAsync(
                        provider, sourceConnectionString, destConnectionString,
                        schemaName, tableNameOnly, limit).Wait();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during direct transfer: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                    }
                }
                
                Console.WriteLine("===========================================");
                Console.WriteLine("DIRECT TABLE TRANSFER UTILITY FINISHED");
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey();
                return;
            }
            
            // Emergency direct import from batch file
            if (args.Length >= 2 && args[0] == "emergency-import")
            {
                Console.WriteLine("===========================================");
                Console.WriteLine("EMERGENCY IMPORT TOOL STARTED");
                Console.WriteLine("===========================================");
                Console.WriteLine("Starting emergency import process...");
                Console.WriteLine($"Arguments: {string.Join(" ", args.Skip(1))}");
                
                if (args.Length < 5)
                {
                    Console.WriteLine("Error: Not enough arguments for emergency import.");
                    Console.WriteLine("Usage: emergency-import <file-path> <provider> <connection-string> <table-name>");
                    Console.WriteLine("Example: emergency-import \"C:\\path\\to\\batch0.bin\" sqlserver \"Server=localhost;...\" \"dbo.tablename\"");
                    return;
                }
                
                string filePath = args[1];
                string providerName = args[2];
                string connectionString = args[3];
                string fullTableName = args[4];
                
                Console.WriteLine($"File path: {filePath}");
                Console.WriteLine($"Provider: {providerName}");
                Console.WriteLine($"Connection string (truncated): {connectionString.Substring(0, Math.Min(20, connectionString.Length))}...");
                Console.WriteLine($"Table name: {fullTableName}");
                
                // Parse table name into schema.table
                string schemaName = "dbo";
                string tableName = fullTableName;
                if (fullTableName.Contains("."))
                {
                    var parts = fullTableName.Split('.');
                    schemaName = parts[0];
                    tableName = parts[1];
                }
                
                Console.WriteLine($"Running emergency import on file: {filePath}");
                Console.WriteLine($"Provider: {providerName}, Table: {schemaName}.{tableName}");
                
                try
                {
                    // Create provider
                    IDatabaseProvider provider;
                    switch (providerName.ToLowerInvariant())
                    {
                        case "sqlserver":
                            provider = new SqlServerProvider();
                            break;
                        case "mysql":
                            provider = new MySqlProvider();
                            break;
                        case "postgresql":
                            provider = new PostgreSqlProvider();
                            break;
                        case "firebird":
                            provider = new FirebirdProvider();
                            break;
                        default:
                            Console.WriteLine($"Unknown provider: {providerName}");
                            return;
                    }
                    
                    // Create connection
                    using var connection = provider.CreateConnection(connectionString);
                    connection.Open();
                    
                    // Run emergency import
                    var result = EmergencyImporter.ImportBatchFileDirectlyAsync(
                        filePath, provider, connection, schemaName, tableName).Result;
                        
                    if (result.Success)
                    {  
                        Console.WriteLine($"Emergency import successful! Imported {result.RowCount} rows.");
                    }
                    else
                    {  
                        Console.WriteLine($"Emergency import failed: {result.ErrorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during emergency import: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                    }
                }
                
                Console.WriteLine("===========================================");
                Console.WriteLine("EMERGENCY IMPORT TOOL FINISHED");
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey();
                return;
            }
            
            // Check for console mode flag or if command-line arguments are provided
            bool consoleMode = args.Contains("--console") || args.Contains("-c");
            
            if (args.Length == 0 || (!consoleMode && args.Length == 1 && args[0] == "--gui"))
            {
                // No arguments or --gui flag - launch GUI
                RunGuiMode();
            }
            else if (consoleMode && args.Length == 1)
            {
                // Console flag only - show interactive console menu
                RunInteractiveMode().Wait();
            }
            else
            {
                // Remove --console flag if present before processing other arguments
                if (consoleMode)
                {
                    args = args.Where(a => a != "--console" && a != "-c").ToArray();
                }
                
                // Command line arguments provided - process them as before
                ProcessCommandLineArgs(args).Wait();
            }
            }
            finally
            {
                // Cleanup dependency injection
                if (_serviceProvider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
        
        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();
            
            // Add core services
            services.AddDatabaseMigrationServices();
            services.AddErrorHandling();
            
            var serviceProvider = services.BuildServiceProvider();
            
            // Configure logging based on environment
            #if DEBUG
                LoggingService.ConfigureDevelopmentLogging();
            #endif
            
            return serviceProvider;
        }
        
        private static void RunGuiMode()
        {
            try
            {
                // Run WPF application on the main STA thread
                var application = new Application();
                
                // Create MainWindow using dependency injection
                var mainWindow = _serviceProvider!.GetRequiredService<MainWindow>();
                application.Run(mainWindow);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error starting GUI: {ex.Message}");
                Console.Error.WriteLine("Falling back to console mode...");
                RunInteractiveMode().Wait();
            }
        }

        private static async Task RunInteractiveMode()
        {
            bool exitRequested = false;

            while (!exitRequested)
            {
                Console.Clear();
                Console.WriteLine("=== Database Migration Tool ===");
                Console.WriteLine("1. List Available Database Providers");
                Console.WriteLine("2. Export Database");
                Console.WriteLine("3. Import Database");
                Console.WriteLine("4. View Database Schema");
                Console.WriteLine("0. Exit");
                Console.Write("\nEnter your choice: ");

                if (int.TryParse(Console.ReadLine(), out int choice))
                {
                    switch (choice)
                    {
                        case 0:
                            exitRequested = true;
                            break;
                        case 1:
                            await ListProviders(new ListProvidersOptions());
                            break;
                        case 2:
                            await RunInteractiveExport();
                            break;
                        case 3:
                            await RunInteractiveImport();
                            break;
                        case 4:
                            await RunInteractiveSchema();
                            break;
                        default:
                            Console.WriteLine("Invalid option. Press any key to continue...");
                            Console.ReadKey();
                            break;
                    }
                }
                else
                {
                    Console.WriteLine("Invalid input. Press any key to continue...");
                    Console.ReadKey();
                }
                
                if (!exitRequested)
                {
                    Console.WriteLine("\nPress any key to return to the main menu...");
                    Console.ReadKey();
                }
            }
        }

        private static async Task RunInteractiveExport()
        {
            var options = new ExportCommandOptions();
            
            Console.WriteLine("\n=== Export Database ===");
            
            options.Provider = GetUserInput("Enter database provider (sqlserver, mysql, postgresql, firebird): ");
            options.ConnectionString = GetUserInput("Enter connection string: ");
            options.OutputPath = GetUserInput("Enter output directory path: ");
            
            string tablesInput = GetUserInput("Enter tables to export (comma-separated, leave blank for all): ");
            if (!string.IsNullOrWhiteSpace(tablesInput))
            {
                options.Tables = tablesInput;
            }
            
            string batchSizeInput = GetUserInput("Enter batch size (default: 100000): ");
            if (!string.IsNullOrWhiteSpace(batchSizeInput) && int.TryParse(batchSizeInput, out int batchSize))
            {
                options.BatchSize = batchSize;
            }
            
            string schemaOnlyInput = GetUserInput("Export schema only? (y/n, default: n): ");
            options.SchemaOnly = schemaOnlyInput.Trim().ToLower() == "y";
            
            string tableCriteriaInput = GetUserInput("Enter path to table criteria JSON file (leave blank to skip): ");
            if (!string.IsNullOrWhiteSpace(tableCriteriaInput))
            {
                options.TableCriteriaFile = tableCriteriaInput;
            }
            
            Console.WriteLine("\nStarting export...");
            await RunExportCommand(options);
        }

        private static async Task RunInteractiveImport()
        {
            var options = new ImportCommandOptions();
            
            Console.WriteLine("\n=== Import Database ===");
            
            options.Provider = GetUserInput("Enter database provider (sqlserver, mysql, postgresql, firebird): ");
            options.ConnectionString = GetUserInput("Enter connection string: ");
            options.InputPath = GetUserInput("Enter input directory path: ");
            
            string tablesInput = GetUserInput("Enter tables to import (comma-separated, leave blank for all): ");
            if (!string.IsNullOrWhiteSpace(tablesInput))
            {
                options.Tables = tablesInput;
            }
            
            string batchSizeInput = GetUserInput("Enter batch size (default: 100000): ");
            if (!string.IsNullOrWhiteSpace(batchSizeInput) && int.TryParse(batchSizeInput, out int batchSize))
            {
                options.BatchSize = batchSize;
            }
            
            string noSchemaInput = GetUserInput("Skip schema creation? (y/n, default: n): ");
            options.NoCreateSchema = noSchemaInput.Trim().ToLower() == "y";
            
            string noFKInput = GetUserInput("Skip foreign key creation? (y/n, default: n): ");
            options.NoCreateForeignKeys = noFKInput.Trim().ToLower() == "y";
            
            string schemaOnlyInput = GetUserInput("Import schema only? (y/n, default: n): ");
            options.SchemaOnly = schemaOnlyInput.Trim().ToLower() == "y";
            
            string continueOnErrorInput = GetUserInput("Continue on error? (y/n, default: n): ");
            options.ContinueOnError = continueOnErrorInput.Trim().ToLower() == "y";
            
            Console.WriteLine("\nStarting import...");
            await RunImportCommand(options);
        }

        private static async Task RunInteractiveSchema()
        {
            var options = new SchemaCommandOptions();
            
            Console.WriteLine("\n=== View Database Schema ===");
            
            options.Provider = GetUserInput("Enter database provider (sqlserver, mysql, postgresql, firebird): ");
            options.ConnectionString = GetUserInput("Enter connection string: ");
            
            string tablesInput = GetUserInput("Enter tables to view (comma-separated, leave blank for all): ");
            if (!string.IsNullOrWhiteSpace(tablesInput))
            {
                options.Tables = tablesInput;
            }
            
            string verboseInput = GetUserInput("Show detailed schema information? (y/n, default: n): ");
            options.Verbose = verboseInput.Trim().ToLower() == "y";
            
            string scriptInput = GetUserInput("Generate SQL scripts? (y/n, default: n): ");
            options.ScriptOutput = scriptInput.Trim().ToLower() == "y";
            
            if (options.ScriptOutput)
            {
                options.ScriptPath = GetUserInput("Enter output path for SQL scripts: ");
            }
            
            Console.WriteLine("\nFetching schema...");
            await RunSchemaCommand(options);
        }

        private static string GetUserInput(string prompt)
        {
            Console.Write(prompt);
            return Console.ReadLine() ?? string.Empty;
        }

        private static async Task ProcessCommandLineArgs(string[] args)
        {
            await Parser.Default.ParseArguments<
                ListProvidersOptions,
                ExportCommandOptions,
                ImportCommandOptions,
                SchemaCommandOptions,
                ConfigCommandOptions,
                DiagnoseOptions,
                ValidateExportImportOptions
            >(args)
            .MapResult(
                (ListProvidersOptions opts) => ListProviders(opts),
                (ExportCommandOptions opts) => RunExportCommand(opts),
                (ImportCommandOptions opts) => RunImportCommand(opts),
                (SchemaCommandOptions opts) => RunSchemaCommand(opts),
                (ConfigCommandOptions opts) => RunConfigCommand(opts),
                (DiagnoseOptions opts) => DiagnoseCommand.Execute(opts),
                (ValidateExportImportOptions opts) => ValidateExportImportCommand.Execute(opts),
                errors => Task.FromResult(1)
            );
        }

        private static Task<int> ListProviders(ListProvidersOptions options)
        {
            Console.WriteLine("Available database providers:");
            foreach (var provider in DatabaseProviderFactory.GetSupportedProviders())
            {
                Console.WriteLine($"- {provider}");
            }
            
            return Task.FromResult(0);
        }

        private static async Task<int> RunExportCommand(ExportCommandOptions options)
        {
            try
            {
                // Load configuration file if specified and merge with command options
                options = await LoadAndMergeExportConfigAsync(options);
                
                // Validate required parameters
                if (string.IsNullOrEmpty(options.Provider))
                {
                    Console.Error.WriteLine("Error: Provider is required. Specify with --provider or in config file.");
                    return 1;
                }
                
                if (string.IsNullOrEmpty(options.ConnectionString))
                {
                    Console.Error.WriteLine("Error: Connection string is required. Specify with --connection or in config file.");
                    return 1;
                }
                
                if (string.IsNullOrEmpty(options.OutputPath))
                {
                    Console.Error.WriteLine("Error: Output path is required. Specify with --output or in config file.");
                    return 1;
                }
                var provider = DatabaseProviderFactory.Create(options.Provider);
                var connection = provider.CreateConnection(options.ConnectionString);
                
                Dictionary<string, string>? tableCriteria = null;
                if (!string.IsNullOrEmpty(options.TableCriteriaFile) && File.Exists(options.TableCriteriaFile))
                {
                    var json = File.ReadAllText(options.TableCriteriaFile);
                    tableCriteria = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                }
                
                var exportOptions = new ExportOptions
                {
                    Tables = options.Tables?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                    TableCriteria = tableCriteria,
                    BatchSize = options.BatchSize,
                    OutputDirectory = options.OutputPath,
                    IncludeSchemaOnly = options.SchemaOnly
                };
                
                // Check for existing export and get user confirmation
                var tablesList = string.IsNullOrEmpty(options.Tables) 
                    ? null 
                    : options.Tables.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToList();
                var overwriteResult = Utilities.ExportOverwriteChecker.CheckForTableSpecificOverwrite(options.OutputPath, tablesList);
                
                if (overwriteResult.HasExistingExport)
                {
                    Console.WriteLine();
                    Console.WriteLine("⚠️  Export Already Exists");
                    Console.WriteLine("=".PadRight(50, '='));
                    Console.WriteLine("An export already exists in the selected directory.");
                    Console.WriteLine(overwriteResult.GetSummaryText());
                    Console.WriteLine();
                    
                    if (overwriteResult.ExistingFiles.Count > 0)
                    {
                        Console.WriteLine("Files that will be overwritten:");
                        int displayCount = Math.Min(10, overwriteResult.ExistingFiles.Count);
                        for (int i = 0; i < displayCount; i++)
                        {
                            Console.WriteLine($"  • {overwriteResult.ExistingFiles[i]}");
                        }
                        
                        if (overwriteResult.ExistingFiles.Count > displayCount)
                        {
                            Console.WriteLine($"  ... and {overwriteResult.ExistingFiles.Count - displayCount} more files");
                        }
                        Console.WriteLine();
                    }
                    
                    Console.Write("Do you want to overwrite the existing export? (y/N): ");
                    string? response = Console.ReadLine();
                    
                    if (string.IsNullOrEmpty(response) || !response.Trim().ToLowerInvariant().StartsWith("y"))
                    {
                        Console.WriteLine("Export cancelled - existing files not overwritten.");
                        return 1; // Exit with error code
                    }
                    
                    // User confirmed overwrite - delete existing files
                    Console.WriteLine("Deleting existing export files...");
                    try
                    {
                        Utilities.ExportOverwriteChecker.DeleteExistingExport(options.OutputPath);
                        Console.WriteLine("Existing export files deleted successfully.");
                    }
                    catch (Exception deleteEx)
                    {
                        Console.Error.WriteLine($"Error deleting existing files: {deleteEx.Message}");
                        return 1; // Exit with error code
                    }
                }
                
                var exporter = new DatabaseExporter(provider, connection, exportOptions);
                await exporter.ExportAsync(options.OutputPath);
                
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error during export: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        private static async Task<int> RunImportCommand(ImportCommandOptions options)
        {
            try
            {
                // Load configuration file if specified and merge with command options
                options = await LoadAndMergeImportConfigAsync(options);
                
                // Validate required parameters
                if (string.IsNullOrEmpty(options.Provider))
                {
                    Console.Error.WriteLine("Error: Provider is required. Specify with --provider or in config file.");
                    return 1;
                }
                
                if (string.IsNullOrEmpty(options.ConnectionString))
                {
                    Console.Error.WriteLine("Error: Connection string is required. Specify with --connection or in config file.");
                    return 1;
                }
                
                if (string.IsNullOrEmpty(options.InputPath))
                {
                    Console.Error.WriteLine("Error: Input path is required. Specify with --input or in config file.");
                    return 1;
                }
                var provider = DatabaseProviderFactory.Create(options.Provider);
                var connection = provider.CreateConnection(options.ConnectionString);
                
                var importOptions = new ImportOptions
                {
                    Tables = options.Tables?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                    BatchSize = options.BatchSize,
                    CreateSchema = !options.NoCreateSchema,
                    CreateForeignKeys = !options.NoCreateForeignKeys,
                    SchemaOnly = options.SchemaOnly,
                    ContinueOnError = options.ContinueOnError
                };
                
                // Check for existing data and get user confirmation
                Console.WriteLine("Checking for existing data in target database...");
                var overwriteResult = await Utilities.ImportOverwriteChecker.CheckForExistingDataAsync(
                    provider, connection, options.InputPath, importOptions);
                
                if (overwriteResult.HasConflictingData)
                {
                    Console.WriteLine();
                    Console.WriteLine("⚠️  Import Will Affect Existing Data");
                    Console.WriteLine("=".PadRight(50, '='));
                    Console.WriteLine("The import operation will affect existing tables and data in the target database:");
                    Console.WriteLine();
                    Console.WriteLine(overwriteResult.GetSummaryText());
                    Console.WriteLine();
                    
                    if (overwriteResult.ConflictingTables.Any())
                    {
                        Console.WriteLine("Tables with conflicts:");
                        foreach (var conflict in overwriteResult.ConflictingTables.Take(10))
                        {
                            Console.WriteLine($"  • {conflict.TableName}: {conflict.GetDescription()}");
                        }
                        
                        if (overwriteResult.ConflictingTables.Count > 10)
                        {
                            Console.WriteLine($"  ... and {overwriteResult.ConflictingTables.Count - 10} more conflicting tables");
                        }
                        Console.WriteLine();
                    }
                    
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("⚠️ WARNING: This operation may overwrite existing data or fail if tables already exist.");
                    Console.WriteLine("Make sure you have backups of important data before proceeding.");
                    Console.ResetColor();
                    Console.WriteLine();
                    
                    Console.Write("Do you want to proceed with the import? (y/N): ");
                    string? response = Console.ReadLine();
                    
                    if (string.IsNullOrEmpty(response) || !response.Trim().ToLowerInvariant().StartsWith("y"))
                    {
                        Console.WriteLine("Import cancelled - existing data not overwritten.");
                        return 1; // Exit with error code
                    }
                    
                    Console.WriteLine("User confirmed import - proceeding with data overwrite...");
                }
                else if (!string.IsNullOrEmpty(overwriteResult.Message))
                {
                    Console.WriteLine($"Import analysis: {overwriteResult.Message}");
                }
                
                var importer = new DatabaseImporter(provider, connection, importOptions);
                await importer.ImportAsync(options.InputPath);
                
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error during import: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        private static async Task<int> RunSchemaCommand(SchemaCommandOptions options)
        {
            try
            {
                // Load configuration file if specified and merge with command options
                options = await LoadAndMergeSchemaConfigAsync(options);
                
                // Validate required parameters
                if (string.IsNullOrEmpty(options.Provider))
                {
                    Console.Error.WriteLine("Error: Provider is required. Specify with --provider or in config file.");
                    return 1;
                }
                
                if (string.IsNullOrEmpty(options.ConnectionString))
                {
                    Console.Error.WriteLine("Error: Connection string is required. Specify with --connection or in config file.");
                    return 1;
                }
                var provider = DatabaseProviderFactory.Create(options.Provider);
                var connection = provider.CreateConnection(options.ConnectionString);
                
                await connection.OpenAsync();
                
                var tables = await provider.GetTablesAsync(connection, 
                    options.Tables?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                
                Console.WriteLine($"Found {tables.Count} tables:");
                foreach (var table in tables)
                {
                    Console.WriteLine($"- {table.FullName} ({table.Columns.Count} columns)");
                    
                    if (options.Verbose)
                    {
                        Console.WriteLine("  Columns:");
                        foreach (var column in table.Columns.OrderBy(c => c.OrdinalPosition))
                        {
                            string nullable = column.IsNullable ? "NULL" : "NOT NULL";
                            string pk = column.IsPrimaryKey ? " PK" : "";
                            Console.WriteLine($"    {column.Name} ({column.DataType}) {nullable}{pk}");
                        }
                        
                        if (table.Indexes.Any())
                        {
                            Console.WriteLine("  Indexes:");
                            foreach (var index in table.Indexes)
                            {
                                string unique = index.IsUnique ? "UNIQUE " : "";
                                Console.WriteLine($"    {unique}{index.Name} ({string.Join(", ", index.Columns)})");
                            }
                        }
                        
                        if (table.ForeignKeys.Any())
                        {
                            Console.WriteLine("  Foreign Keys:");
                            foreach (var fk in table.ForeignKeys)
                            {
                                Console.WriteLine($"    {fk.Name} -> {fk.ReferencedTableSchema}.{fk.ReferencedTableName}");
                                Console.WriteLine($"      Columns: {string.Join(", ", fk.Columns)} -> {string.Join(", ", fk.ReferencedColumns)}");
                            }
                        }
                        
                        Console.WriteLine();
                    }
                }
                
                if (options.ScriptOutput && !string.IsNullOrEmpty(options.ScriptPath))
                {
                    await GenerateScripts(provider, tables, options.ScriptPath);
                }
                
                await connection.CloseAsync();
                
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error during schema command: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        private static async Task GenerateScripts(IDatabaseProvider provider, List<Models.TableSchema> tables, string outputPath)
        {
            Directory.CreateDirectory(outputPath);
            
            foreach (var table in tables)
            {
                string fileName = $"{table.Schema}_{table.Name}.sql";
                string filePath = Path.Combine(outputPath, fileName);
                
                string script = provider.GenerateTableCreationScript(table);
                await File.WriteAllTextAsync(filePath, script);
                
                Console.WriteLine($"Generated script: {filePath}");
            }
        }
        
        // Configuration file helper methods
        private static async Task<ExportCommandOptions> LoadAndMergeExportConfigAsync(ExportCommandOptions options)
        {
            if (string.IsNullOrEmpty(options.ConfigFile))
            {
                return options;
            }
            
            try
            {
                var config = await Utilities.ConfigurationManager.LoadConfigurationAsync(options.ConfigFile);
                
                if (config.Export == null)
                {
                    Console.WriteLine($"Warning: Configuration file '{options.ConfigFile}' does not contain export configuration.");
                    return options;
                }
                
                // Merge config file values with command line options (command line takes precedence)
                return new ExportCommandOptions
                {
                    ConfigFile = options.ConfigFile,
                    Provider = !string.IsNullOrEmpty(options.Provider) ? options.Provider : config.Export.Provider,
                    ConnectionString = !string.IsNullOrEmpty(options.ConnectionString) ? options.ConnectionString : config.Export.ConnectionString,
                    OutputPath = !string.IsNullOrEmpty(options.OutputPath) ? options.OutputPath : config.Export.OutputPath,
                    Tables = options.Tables ?? config.Export.Tables,
                    TableCriteriaFile = options.TableCriteriaFile ?? config.Export.TableCriteriaFile,
                    BatchSize = options.BatchSize != 100000 ? options.BatchSize : config.Export.BatchSize, // Check if user specified different batch size
                    SchemaOnly = options.SchemaOnly || config.Export.SchemaOnly
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error loading configuration file '{options.ConfigFile}': {ex.Message}");
                Environment.Exit(1);
                return options; // Never reached
            }
        }
        
        private static async Task<ImportCommandOptions> LoadAndMergeImportConfigAsync(ImportCommandOptions options)
        {
            if (string.IsNullOrEmpty(options.ConfigFile))
            {
                return options;
            }
            
            try
            {
                var config = await Utilities.ConfigurationManager.LoadConfigurationAsync(options.ConfigFile);
                
                if (config.Import == null)
                {
                    Console.WriteLine($"Warning: Configuration file '{options.ConfigFile}' does not contain import configuration.");
                    return options;
                }
                
                // Merge config file values with command line options (command line takes precedence)
                return new ImportCommandOptions
                {
                    ConfigFile = options.ConfigFile,
                    Provider = !string.IsNullOrEmpty(options.Provider) ? options.Provider : config.Import.Provider,
                    ConnectionString = !string.IsNullOrEmpty(options.ConnectionString) ? options.ConnectionString : config.Import.ConnectionString,
                    InputPath = !string.IsNullOrEmpty(options.InputPath) ? options.InputPath : config.Import.InputPath,
                    Tables = options.Tables ?? config.Import.Tables,
                    BatchSize = options.BatchSize != 100000 ? options.BatchSize : config.Import.BatchSize,
                    NoCreateSchema = options.NoCreateSchema || config.Import.NoCreateSchema,
                    NoCreateForeignKeys = options.NoCreateForeignKeys || config.Import.NoCreateForeignKeys,
                    SchemaOnly = options.SchemaOnly || config.Import.SchemaOnly,
                    ContinueOnError = options.ContinueOnError || config.Import.ContinueOnError
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error loading configuration file '{options.ConfigFile}': {ex.Message}");
                Environment.Exit(1);
                return options; // Never reached
            }
        }
        
        private static async Task<SchemaCommandOptions> LoadAndMergeSchemaConfigAsync(SchemaCommandOptions options)
        {
            if (string.IsNullOrEmpty(options.ConfigFile))
            {
                return options;
            }
            
            try
            {
                var config = await Utilities.ConfigurationManager.LoadConfigurationAsync(options.ConfigFile);
                
                if (config.Schema == null)
                {
                    Console.WriteLine($"Warning: Configuration file '{options.ConfigFile}' does not contain schema configuration.");
                    return options;
                }
                
                // Merge config file values with command line options (command line takes precedence)
                return new SchemaCommandOptions
                {
                    ConfigFile = options.ConfigFile,
                    Provider = !string.IsNullOrEmpty(options.Provider) ? options.Provider : config.Schema.Provider,
                    ConnectionString = !string.IsNullOrEmpty(options.ConnectionString) ? options.ConnectionString : config.Schema.ConnectionString,
                    Tables = options.Tables ?? config.Schema.Tables,
                    Verbose = options.Verbose || config.Schema.Verbose,
                    ScriptOutput = options.ScriptOutput || config.Schema.GenerateScripts,
                    ScriptPath = options.ScriptPath ?? config.Schema.ScriptPath
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error loading configuration file '{options.ConfigFile}': {ex.Message}");
                Environment.Exit(1);
                return options; // Never reached
            }
        }
        
        private static async Task<int> RunConfigCommand(ConfigCommandOptions options)
        {
            try
            {
                if (!string.IsNullOrEmpty(options.CreateSamplePath))
                {
                    await Utilities.ConfigurationManager.CreateSampleConfigurationAsync(options.CreateSamplePath);
                    Console.WriteLine($"Sample configuration file created: {options.CreateSamplePath}");
                    return 0;
                }
                
                if (!string.IsNullOrEmpty(options.ValidatePath))
                {
                    var isValid = await Utilities.ConfigurationManager.IsValidConfigurationFileAsync(options.ValidatePath);
                    if (isValid)
                    {
                        Console.WriteLine($"✓ Configuration file is valid: {options.ValidatePath}");
                        return 0;
                    }
                    else
                    {
                        Console.Error.WriteLine($"✗ Configuration file is invalid or corrupted: {options.ValidatePath}");
                        return 1;
                    }
                }
                
                if (!string.IsNullOrEmpty(options.ShowPath))
                {
                    var config = await Utilities.ConfigurationManager.LoadConfigurationAsync(options.ShowPath);
                    var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                    });
                    Console.WriteLine(json);
                    return 0;
                }
                
                Console.Error.WriteLine("Error: No action specified. Use --create-sample, --validate, or --show");
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }
    }

    [Verb("providers", HelpText = "List available database providers")]
    public class ListProvidersOptions
    {
    }

    [Verb("export", HelpText = "Export database schema and data")]
    public class ExportCommandOptions
    {
        [Option("config", HelpText = "Load parameters from configuration file (JSON). Other options will override config file values.")]
        public string? ConfigFile { get; set; }
        
        [Option('p', "provider", HelpText = "Database provider (sqlserver, mysql, postgresql, firebird)")]
        public string Provider { get; set; } = string.Empty;
        
        [Option('c', "connection", HelpText = "Connection string")]
        public string ConnectionString { get; set; } = string.Empty;
        
        [Option('o', "output", HelpText = "Output directory path")]
        public string OutputPath { get; set; } = string.Empty;
        
        [Option('t', "tables", HelpText = "Comma-separated list of tables to export (default: all tables)")]
        public string? Tables { get; set; }
        
        [Option("criteria", HelpText = "JSON file containing table criteria as key-value pairs")]
        public string? TableCriteriaFile { get; set; }
        
        [Option('b', "batch-size", Default = 100000, HelpText = "Number of rows to process in a single batch")]
        public int BatchSize { get; set; }
        
        [Option("schema-only", Default = false, HelpText = "Export schema only (no data)")]
        public bool SchemaOnly { get; set; }
    }

    [Verb("import", HelpText = "Import database schema and data")]
    public class ImportCommandOptions
    {
        [Option("config", HelpText = "Load parameters from configuration file (JSON). Other options will override config file values.")]
        public string? ConfigFile { get; set; }
        
        [Option('p', "provider", HelpText = "Database provider (sqlserver, mysql, postgresql, firebird)")]
        public string Provider { get; set; } = string.Empty;
        
        [Option('c', "connection", HelpText = "Connection string")]
        public string ConnectionString { get; set; } = string.Empty;
        
        [Option('i', "input", HelpText = "Input directory path")]
        public string InputPath { get; set; } = string.Empty;
        
        [Option('t', "tables", HelpText = "Comma-separated list of tables to import (default: all tables)")]
        public string? Tables { get; set; }
        
        [Option('b', "batch-size", Default = 100000, HelpText = "Number of rows to process in a single batch")]
        public int BatchSize { get; set; }
        
        [Option("no-create-schema", Default = false, HelpText = "Skip schema creation")]
        public bool NoCreateSchema { get; set; }
        
        [Option("no-foreign-keys", Default = false, HelpText = "Skip foreign key creation")]
        public bool NoCreateForeignKeys { get; set; }
        
        [Option("schema-only", Default = false, HelpText = "Import schema only (no data)")]
        public bool SchemaOnly { get; set; }
        
        [Option("continue-on-error", Default = false, HelpText = "Continue processing on error")]
        public bool ContinueOnError { get; set; }
    }

    [Verb("schema", HelpText = "View and export database schema")]
    public class SchemaCommandOptions
    {
        [Option("config", HelpText = "Load parameters from configuration file (JSON). Other options will override config file values.")]
        public string? ConfigFile { get; set; }
        
        [Option('p', "provider", HelpText = "Database provider (sqlserver, mysql, postgresql, firebird)")]
        public string Provider { get; set; } = string.Empty;
        
        [Option('c', "connection", HelpText = "Connection string")]
        public string ConnectionString { get; set; } = string.Empty;
        
        [Option('t', "tables", HelpText = "Comma-separated list of tables to view (default: all tables)")]
        public string? Tables { get; set; }
        
        [Option('v', "verbose", Default = false, HelpText = "Show detailed schema information")]
        public bool Verbose { get; set; }
        
        [Option("script", Default = false, HelpText = "Generate SQL scripts")]
        public bool ScriptOutput { get; set; }
        
        [Option("script-path", HelpText = "Output path for SQL scripts")]
        public string? ScriptPath { get; set; }
    }

    [Verb("config", HelpText = "Manage configuration files")]
    public class ConfigCommandOptions
    {
        [Option("create-sample", HelpText = "Create a sample configuration file with all options")]
        public string? CreateSamplePath { get; set; }
        
        [Option("validate", HelpText = "Validate a configuration file")]
        public string? ValidatePath { get; set; }
        
        [Option("show", HelpText = "Display the contents of a configuration file")]
        public string? ShowPath { get; set; }
    }
}
