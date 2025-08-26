using CommandLine;
using DatabaseMigrationTool.Utilities;
using System;
using System.IO;
using System.Threading.Tasks;

namespace DatabaseMigrationTool.Commands
{
    [Verb("diagnose", HelpText = "Diagnose import/export file issues")]
    public class DiagnoseOptions
    {
        [Option('f', "file", Required = true, HelpText = "The file to diagnose")]
        public string FilePath { get; set; } = string.Empty;
        
        [Option('o', "output", Required = false, HelpText = "Output file for diagnostic results")]
        public string OutputPath { get; set; } = string.Empty;
        
        [Option('v', "verbose", Default = false, HelpText = "Show verbose output")]
        public bool Verbose { get; set; }
    }
    
    public class DiagnoseCommand
    {
        public static async Task<int> Execute(DiagnoseOptions options)
        {
            try
            {
                Console.WriteLine($"Diagnosing file: {options.FilePath}");
                
                if (!File.Exists(options.FilePath))
                {
                    Console.WriteLine($"ERROR: File not found: {options.FilePath}");
                    return 1;
                }
                
                var result = await DiagnosticImport.ValidateImportFileAsync(options.FilePath);
                
                // Print summary
                Console.WriteLine("Diagnostic Results:");
                Console.WriteLine($"File: {Path.GetFileName(options.FilePath)}");
                Console.WriteLine($"Size: {result.FileSize} bytes");
                Console.WriteLine($"Compressed: {result.IsCompressed}");
                
                if (result.Deserialized)
                {
                    Console.WriteLine($"Successfully deserialized: YES");
                    Console.WriteLine($"Table: {result.SchemaName}.{result.TableName}");
                    Console.WriteLine($"Row count: {result.RowCount}");
                    Console.WriteLine($"Columns: {result.Columns.Count}");
                }
                else
                {
                    Console.WriteLine("Successfully deserialized: NO");
                }
                
                // Print detailed log
                if (options.Verbose)
                {
                    Console.WriteLine("\nDetailed Log:");
                    foreach (var log in result.Logs)
                    {
                        Console.WriteLine(log);
                    }
                }
                
                // Save to output file if requested
                if (!string.IsNullOrEmpty(options.OutputPath))
                {
                    using (var writer = new StreamWriter(options.OutputPath))
                    {
                        writer.WriteLine("# Diagnostic Results");
                        writer.WriteLine($"File: {Path.GetFileName(options.FilePath)}");
                        writer.WriteLine($"Size: {result.FileSize} bytes");
                        writer.WriteLine($"Compressed: {result.IsCompressed}");
                        
                        if (result.IsCompressed)
                        {
                            writer.WriteLine($"Decompressed size: {result.DecompressedSize} bytes");
                        }
                        
                        if (result.Deserialized)
                        {
                            writer.WriteLine($"Successfully deserialized: YES");
                            writer.WriteLine($"Table: {result.SchemaName}.{result.TableName}");
                            writer.WriteLine($"Row count: {result.RowCount}");
                            writer.WriteLine($"Columns: {result.Columns.Count}");
                            writer.WriteLine($"Column names: {string.Join(", ", result.Columns)}");
                        }
                        else
                        {
                            writer.WriteLine("Successfully deserialized: NO");
                        }
                        
                        writer.WriteLine("\n# Detailed Log");
                        foreach (var log in result.Logs)
                        {
                            writer.WriteLine(log);
                        }
                    }
                    
                    Console.WriteLine($"\nDetailed diagnostic information saved to: {options.OutputPath}");
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Diagnostic failed: {ex.Message}");
                return 1;
            }
        }
    }
}