using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace DatabaseMigrationTool
{
    /// <summary>
    /// Debugging utilities for command-line diagnostics
    /// </summary>
    public static class DebugTools
    {
        /// <summary>
        /// Analyze a data file and display detailed diagnostic information
        /// </summary>
        public static async Task AnalyzeDataFileAsync(string filePath)
        {
            Console.WriteLine($"===== ANALYZING FILE: {filePath} =====");
            
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"ERROR: File does not exist: {filePath}");
                return;
            }
            
            try
            {
                var result = await DirectImporter.AnalyzeDataFileAsync(filePath);
                
                // Display diagnostic information
                Console.WriteLine("\nDIAGNOSTIC RESULTS:");
                Console.WriteLine($"File Path: {result.FilePath}");
                Console.WriteLine($"File Size: {result.FileSize} bytes");
                Console.WriteLine($"Compressed: {(result.IsGZip ? "GZip" : (result.IsBZip2 ? "BZip2" : "No"))}");
                
                if (result.IsGZip || result.IsBZip2)
                {
                    Console.WriteLine($"Decompressed Size: {result.DecompressedSize} bytes");
                }
                
                Console.WriteLine($"Deserialization Success: {result.Success}");
                Console.WriteLine($"Data Found: {result.DataFound}");
                Console.WriteLine($"Row Count: {result.RowCount}");
                
                if (result.TableData?.Schema != null)
                {
                    Console.WriteLine($"\nSCHEMA INFO:");
                    Console.WriteLine($"Table: {result.TableData.Schema.FullName}");
                    Console.WriteLine($"Columns: {result.TableData.Schema.Columns?.Count ?? 0}");
                    
                    // Display column info
                    if (result.TableData.Schema.Columns != null && result.TableData.Schema.Columns.Count > 0)
                    {
                        Console.WriteLine("\nCOLUMN INFO:");
                        foreach (var column in result.TableData.Schema.Columns)
                        {
                            string pkInfo = column.IsPrimaryKey ? " (PK)" : "";
                            string nullableInfo = column.IsNullable ? " NULL" : " NOT NULL";
                            Console.WriteLine($"- {column.Name} : {column.DataType}{pkInfo}{nullableInfo}");
                        }
                    }
                }
                
                // Display detailed diagnostics
                Console.WriteLine("\nDETAILED DIAGNOSTIC LOG:");
                foreach (var message in result.Messages)
                {
                    Console.WriteLine($"- {message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR during analysis: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}