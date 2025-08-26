using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Providers;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MessagePack;
using MessagePack.Resolvers;
using System.Diagnostics;

namespace DatabaseMigrationTool.Services
{
    /// <summary>
    /// Emergency importer for direct data loading from batch files
    /// </summary>
    public static class EmergencyImporter
    {
        public static async Task<EmergencyImportResult> ImportBatchFileDirectlyAsync(
            string batchFilePath,
            IDatabaseProvider provider,
            DbConnection connection,
            string tableSchema,
            string tableName)
        {
            var result = new EmergencyImportResult();
            result.FilePath = batchFilePath;
            
            Console.WriteLine("=== EMERGENCY IMPORT STARTED ===");
            Console.WriteLine($"File: {batchFilePath}");
            Console.WriteLine($"Table: {tableSchema}.{tableName}");
            
            if (!File.Exists(batchFilePath))
            {
                Console.WriteLine("ERROR: File does not exist!");
                result.ErrorMessage = "File not found";
                return result;
            }
            
            try
            {
                var fileInfo = new FileInfo(batchFilePath);
                Console.WriteLine($"File size: {fileInfo.Length} bytes");
                result.FileSize = fileInfo.Length;
                
                // Load file into memory first
                Console.WriteLine("Loading file into memory...");
                byte[] fileBytes = File.ReadAllBytes(batchFilePath);
                Console.WriteLine($"Read {fileBytes.Length} bytes into memory");
                
                // Check for GZip header
                bool isGZipped = fileBytes.Length > 2 && fileBytes[0] == 0x1F && fileBytes[1] == 0x8B;
                Console.WriteLine($"Is GZip compressed: {isGZipped}");
                
                // Try multiple deserialization approaches
                TableData? tableData = null;
                int deserializeMethod = 0;
                
                // Method 1: Direct MessagePack deserialization
                try
                {
                    Console.WriteLine("Method 1: Direct deserialization from file bytes...");
                    deserializeMethod = 1;
                    tableData = MessagePackSerializer.Deserialize<TableData>(fileBytes, MessagePackSerializerOptions.Standard);
                    Console.WriteLine("Method 1 SUCCESS: Direct deserialization successful!");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Method 1 FAILED: {ex.Message}");
                    
                    // Method 2: If it's GZipped, try that
                    if (isGZipped)
                    {
                        try
                        {
                            Console.WriteLine("Method 2: GZip decompression...");
                            deserializeMethod = 2;
                            using var memoryStream = new MemoryStream(fileBytes);
                            using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress);
                            using var decompressedStream = new MemoryStream();
                            
                            await gzipStream.CopyToAsync(decompressedStream);
                            decompressedStream.Position = 0;
                            var decompressedBytes = decompressedStream.ToArray();
                            Console.WriteLine($"Decompressed size: {decompressedBytes.Length} bytes");
                            
                            tableData = MessagePackSerializer.Deserialize<TableData>(decompressedBytes, MessagePackSerializerOptions.Standard);
                            Console.WriteLine("Method 2 SUCCESS: GZip decompression successful!");
                        }
                        catch (Exception gzEx)
                        {
                            Console.WriteLine($"Method 2 FAILED: {gzEx.Message}");
                            
                            // Method 3: Try with contractless resolver
                            try
                            {
                                Console.WriteLine("Method 3: Contractless resolver with GZip decompression...");
                                deserializeMethod = 3;
                                using var memoryStream = new MemoryStream(fileBytes);
                                using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress);
                                using var decompressedStream = new MemoryStream();
                                
                                await gzipStream.CopyToAsync(decompressedStream);
                                decompressedStream.Position = 0;
                                var decompressedBytes = decompressedStream.ToArray();
                                
                                var options = MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);
                                tableData = MessagePackSerializer.Deserialize<TableData>(decompressedBytes, options);
                                Console.WriteLine("Method 3 SUCCESS: Contractless resolver with GZip successful!");
                            }
                            catch (Exception ctEx)
                            {
                                Console.WriteLine($"Method 3 FAILED: {ctEx.Message}");
                                
                                // Method 4: Final attempt - try contractless on raw data
                                try
                                {
                                    Console.WriteLine("Method 4: Contractless resolver on raw data...");
                                    deserializeMethod = 4;
                                    var options = MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);
                                    tableData = MessagePackSerializer.Deserialize<TableData>(fileBytes, options);
                                    Console.WriteLine("Method 4 SUCCESS: Contractless resolver on raw data successful!");
                                }
                                catch (Exception rawEx)
                                {
                                    Console.WriteLine($"Method 4 FAILED: {rawEx.Message}");
                                    Console.WriteLine("All deserialization methods failed!");
                                    result.ErrorMessage = "All deserialization methods failed";
                                    return result;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Method 3: Try with contractless resolver on raw data
                        try
                        {
                            Console.WriteLine("Method 3: Contractless resolver on raw data...");
                            deserializeMethod = 3;
                            var options = MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);
                            tableData = MessagePackSerializer.Deserialize<TableData>(fileBytes, options);
                            Console.WriteLine("Method 3 SUCCESS: Contractless resolver successful!");
                        }
                        catch (Exception ctEx)
                        {
                            Console.WriteLine($"Method 3 FAILED: {ctEx.Message}");
                            Console.WriteLine("All applicable deserialization methods failed!");
                            result.ErrorMessage = "All deserialization methods failed";
                            return result;
                        }
                    }
                }
                
                // At this point, we should have a valid TableData object
                if (tableData != null)
                {
                    Console.WriteLine("Successfully deserialized table data!");
                    Console.WriteLine($"Deserialization method used: {deserializeMethod}");
                    
                    // Check rows
                    if (tableData.Rows != null)
                    {
                        Console.WriteLine($"Found {tableData.Rows.Count} rows in the batch");
                        result.RowCount = tableData.Rows.Count;
                        
                        if (tableData.Rows.Count > 0)
                        {
                            // Sample first row
                            var firstRow = tableData.Rows[0];
                            Console.WriteLine($"First row has {firstRow.Values.Count} columns");
                            
                            // Sample some values
                            var sampleValues = firstRow.Values.Take(3)
                                .Select(kv => $"{kv.Key}={kv.Value}")
                                .ToList();
                                
                            Console.WriteLine($"Sample values: {string.Join(", ", sampleValues)}");
                            
                            // Import the data
                            Console.WriteLine("Starting direct import...");
                            var stopwatch = Stopwatch.StartNew();
                            
                            await provider.ImportDataAsync(connection, tableName, tableSchema, 
                                ConvertToAsyncEnumerable(tableData.Rows));
                                
                            stopwatch.Stop();
                            Console.WriteLine($"Import completed in {stopwatch.ElapsedMilliseconds}ms");
                            
                            result.Success = true;
                            result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                        }
                        else
                        {
                            Console.WriteLine("WARNING: File contains 0 rows");
                            result.Success = true; // Consider this success, just with 0 rows
                        }
                    }
                    else
                    {
                        Console.WriteLine("ERROR: Rows collection is null");
                        result.ErrorMessage = "Rows collection is null";
                    }
                }
                else
                {
                    Console.WriteLine("ERROR: Deserialized TableData is null");
                    result.ErrorMessage = "Deserialized TableData is null";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR during import: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                result.ErrorMessage = ex.Message;
            }
            
            return result;
        }
        
        // Helper method to convert a List to IAsyncEnumerable
        private static async IAsyncEnumerable<RowData> ConvertToAsyncEnumerable(IEnumerable<RowData> rows)
        {
            foreach (var row in rows)
            {
                yield return row;
            }
            await Task.CompletedTask; // Add an await operation to make the compiler happy
        }
    }
    
    public class EmergencyImportResult
    {
        public string FilePath { get; set; } = string.Empty;
        public bool Success { get; set; }
        public int RowCount { get; set; }
        public long FileSize { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }
}