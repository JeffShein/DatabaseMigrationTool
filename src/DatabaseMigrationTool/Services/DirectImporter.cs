using DatabaseMigrationTool.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MessagePack;

namespace DatabaseMigrationTool.Services
{
    /// <summary>
    /// A highly simplified importer for direct debugging of data files
    /// This is for diagnostic purposes to test exported data files
    /// </summary>
    public static class DirectImporter
    {
        public static async Task<ImportDiagnosticResult> AnalyzeDataFileAsync(string filePath)
        {
            var result = new ImportDiagnosticResult
            {
                FilePath = filePath,
                FileExists = File.Exists(filePath),
                Messages = new List<string>()
            };
            
            if (!result.FileExists)
            {
                result.Messages.Add($"File does not exist: {filePath}");
                return result;
            }
            
            try
            {
                var fileInfo = new FileInfo(filePath);
                result.FileSize = fileInfo.Length;
                result.Messages.Add($"File size: {fileInfo.Length} bytes");
                
                // Attempt to read header bytes to identify format
                byte[] header = new byte[8];
                using (var testStream = File.OpenRead(filePath))
                {
                    int bytesRead = testStream.Read(header, 0, Math.Min(8, (int)fileInfo.Length));
                    result.Messages.Add($"Read {bytesRead} bytes from header");
                    
                    // Output hex representation for debugging
                    var hexHeader = BitConverter.ToString(header, 0, bytesRead);
                    result.Messages.Add($"Header bytes: {hexHeader}");
                    
                    // Check GZip signature (1F 8B)
                    if (bytesRead >= 2 && header[0] == 0x1F && header[1] == 0x8B)
                    {
                        result.IsGZip = true;
                        result.Messages.Add("File appears to be GZip compressed");
                    }
                    // BZip2 signature (BZh)
                    else if (bytesRead >= 3 && header[0] == 'B' && header[1] == 'Z' && header[2] == 'h')
                    {
                        result.IsBZip2 = true;
                        result.Messages.Add("File appears to be BZip2 compressed");
                    }
                    else
                    {
                        result.Messages.Add("File does not appear to be compressed (or uses unknown compression)");
                    }
                }
                
                // Try to read the file content
                try
                {
                    result.Messages.Add("Attempting to read the file content...");
                    using var fileStream = File.OpenRead(filePath);
                    
                    // Try different decompression approaches
                    if (result.IsGZip)
                    {
                        result.Messages.Add("Attempting GZip decompression...");
                        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
                        using var memoryStream = new MemoryStream();
                        
                        await gzipStream.CopyToAsync(memoryStream);
                        result.DecompressedSize = memoryStream.Length;
                        result.Messages.Add($"Decompressed size: {memoryStream.Length} bytes");
                        
                        if (memoryStream.Length > 0)
                        {
                            memoryStream.Position = 0;
                            
                            // Try with different MessagePack options
                            await TryDeserializeAsync(memoryStream, result);
                        }
                    }
                    else
                    {
                        result.Messages.Add("Direct file access (no decompression)...");
                        await TryDeserializeAsync(fileStream, result);
                    }
                }
                catch (Exception ex)
                {
                    result.Messages.Add($"ERROR: {ex.GetType().Name}: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        result.Messages.Add($"INNER ERROR: {ex.InnerException.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Messages.Add($"ERROR: {ex.GetType().Name}: {ex.Message}");
            }
            
            return result;
        }
        
        private static async Task TryDeserializeAsync(Stream stream, ImportDiagnosticResult result)
        {
            // Try standard options first
            try
            {
                result.Messages.Add("Attempting deserialization with standard options...");
                var options = MessagePackSerializerOptions.Standard;
                
                // Copy to memory stream to ensure we can reset position
                using var memoryStream = new MemoryStream();
                stream.Position = 0;
                await stream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                
                var tableData = await Task.Run(() => MessagePackSerializer.Deserialize<TableData>(memoryStream, options));
                
                if (tableData != null)
                {
                    result.Messages.Add("Deserialization successful!");
                    result.Success = true;
                    result.DataFound = true;
                    result.TableData = tableData;
                    
                    // Report schema info
                    if (tableData.Schema != null)
                    {
                        result.Messages.Add($"Schema: {tableData.Schema?.FullName ?? "NULL"}");
                        result.Messages.Add($"Columns: {tableData.Schema?.Columns?.Count ?? 0}");
                    }
                    else
                    {
                        result.Messages.Add("WARNING: Schema is NULL");
                    }
                    
                    // Report row count
                    if (tableData.Rows != null)
                    {
                        result.Messages.Add($"Rows in file: {tableData.Rows.Count}");
                        result.RowCount = tableData.Rows.Count;
                        
                        // Check first row if available
                        if (tableData.Rows.Count > 0)
                        {
                            var firstRow = tableData.Rows.First();
                            result.Messages.Add($"First row has {firstRow.Values.Count} columns");
                            
                            // Sample some values - check more carefully for empty values
                            var sampleValues = firstRow.Values.Take(5)
                                .Select(kv => $"{kv.Key}={kv.Value ?? "NULL"}")
                                .ToList();
                                
                            result.Messages.Add($"Sample values: {string.Join(", ", sampleValues)}");
                            
                            // Count empty values to see if this is an issue
                            int emptyCount = firstRow.Values.Count(kv => kv.Value == null || 
                                (kv.Value is string s && string.IsNullOrEmpty(s)));
                                
                            if (emptyCount > 0)
                            {
                                result.Messages.Add($"WARNING: Found {emptyCount} empty values in first row (out of {firstRow.Values.Count} columns)");
                            }
                            
                            // Log more details about values
                            result.Messages.Add("All column values in first row:");
                            foreach (var kv in firstRow.Values)
                            {
                                string valueType = kv.Value?.GetType().Name ?? "NULL";
                                string valueStr = kv.Value?.ToString() ?? "NULL";
                                result.Messages.Add($"  {kv.Key} ({valueType}): {valueStr}");
                            }
                        }
                    }
                    else
                    {
                        result.Messages.Add("WARNING: Rows collection is NULL");
                    }
                }
                else
                {
                    result.Messages.Add("Deserialization returned NULL object");
                }
            }
            catch (Exception ex)
            {
                result.Messages.Add($"Standard deserialization failed: {ex.Message}");
                
                // Try with contractless resolver
                try
                {
                    result.Messages.Add("Attempting deserialization with ContractlessStandardResolver...");
                    var fallbackOptions = MessagePackSerializerOptions.Standard.WithResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance);
                    
                    stream.Position = 0;
                    var tableData = await Task.Run(() => MessagePackSerializer.Deserialize<TableData>(stream, fallbackOptions));
                    
                    if (tableData != null)
                    {
                        result.Messages.Add("Fallback deserialization successful!");
                        result.Success = true;
                        result.DataFound = true;
                        result.TableData = tableData;
                        
                        // Report schema info
                        if (tableData.Schema != null)
                        {
                            result.Messages.Add($"Schema: {tableData.Schema?.FullName ?? "NULL"}");
                            result.Messages.Add($"Columns: {tableData.Schema?.Columns?.Count ?? 0}");
                        }
                        else
                        {
                            result.Messages.Add("WARNING: Schema is NULL");
                        }
                        
                        // Report row count
                        if (tableData.Rows != null)
                        {
                            result.Messages.Add($"Rows in file: {tableData.Rows.Count}");
                            result.RowCount = tableData.Rows.Count;
                        }
                        else
                        {
                            result.Messages.Add("WARNING: Rows collection is NULL");
                        }
                    }
                    else
                    {
                        result.Messages.Add("Fallback deserialization returned NULL object");
                    }
                }
                catch (Exception fallbackEx)
                {
                    result.Messages.Add($"Fallback deserialization failed: {fallbackEx.Message}");
                }
            }
        }
    }
    
    public class ImportDiagnosticResult
    {
        public string FilePath { get; set; } = string.Empty;
        public bool FileExists { get; set; }
        public long FileSize { get; set; }
        public bool IsGZip { get; set; }
        public bool IsBZip2 { get; set; }
        public long DecompressedSize { get; set; }
        public bool Success { get; set; }
        public bool DataFound { get; set; }
        public TableData? TableData { get; set; }
        public int RowCount { get; set; }
        public List<string> Messages { get; set; } = new List<string>();
    }
}