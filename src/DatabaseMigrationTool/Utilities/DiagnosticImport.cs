using DatabaseMigrationTool.Models;
using MessagePack;
using MessagePack.Resolvers;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatabaseMigrationTool.Utilities
{
    /// <summary>
    /// Special diagnostic tool for troubleshooting import issues
    /// </summary>
    public class DiagnosticImport
    {
        public static async Task<DiagnosticResult> ValidateImportFileAsync(string filePath)
        {
            var result = new DiagnosticResult
            {
                FilePath = filePath,
                FileExists = File.Exists(filePath),
                Logs = new List<string>()
            };
            
            if (!result.FileExists)
            {
                result.Logs.Add($"File not found: {filePath}");
                return result;
            }
            
            try
            {
                result.Logs.Add($"Validating file: {filePath}");
                var fileInfo = new FileInfo(filePath);
                result.FileSize = fileInfo.Length;
                result.Logs.Add($"File size: {fileInfo.Length} bytes");
                
                // Read file bytes
                byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
                result.Logs.Add($"Read {fileBytes.Length} bytes from file");
                
                // Check if it's too small
                if (fileBytes.Length < 10)
                {
                    result.Logs.Add("ERROR: File is too small to be valid");
                    return result;
                }
                
                // Check header to identify format
                string hexHeader = BitConverter.ToString(fileBytes.Take(16).ToArray());
                result.Logs.Add($"File header (hex): {hexHeader}");
                
                // Check if it's GZip compressed
                bool isGZip = fileBytes.Length > 2 && fileBytes[0] == 0x1F && fileBytes[1] == 0x8B;
                result.IsCompressed = isGZip;
                result.Logs.Add($"GZip compression: {isGZip}");
                
                // Decompress if needed
                byte[] dataBytes;
                if (isGZip)
                {
                    try
                    {
                        using (var ms = new MemoryStream(fileBytes))
                        using (var gzipStream = new GZipStream(ms, CompressionMode.Decompress))
                        using (var decompressedStream = new MemoryStream())
                        {
                            await gzipStream.CopyToAsync(decompressedStream);
                            dataBytes = decompressedStream.ToArray();
                        }
                        result.DecompressedSize = dataBytes.Length;
                        result.Logs.Add($"Successfully decompressed to {dataBytes.Length} bytes");
                        
                        // Show a sample of the decompressed data
                        string hexSample = BitConverter.ToString(dataBytes.Take(32).ToArray());
                        result.Logs.Add($"Decompressed data sample (hex): {hexSample}");
                        
                        // Try to show some text if there's any in the file
                        StringBuilder textSample = new StringBuilder();
                        foreach (byte b in dataBytes.Take(100))
                        {
                            if (b >= 32 && b <= 126) // Printable ASCII
                                textSample.Append((char)b);
                            else
                                textSample.Append('.');
                        }
                        result.Logs.Add($"Decompressed data sample (text): {textSample}");
                    }
                    catch (Exception ex)
                    {
                        result.Logs.Add($"ERROR: Failed to decompress GZip data: {ex.Message}");
                        return result;
                    }
                }
                else
                {
                    dataBytes = fileBytes;
                }
                
                // Try to deserialize with MessagePack
                try
                {
                    // Try with standard options first
                    var tableData = MessagePackSerializer.Deserialize<TableData>(
                        dataBytes,
                        MessagePackSerializerOptions.Standard);
                        
                    result.Deserialized = true;
                    result.TableName = tableData.Schema?.Name ?? "Unknown";
                    result.SchemaName = tableData.Schema?.Schema ?? "dbo";
                    result.RowCount = tableData.Rows?.Count ?? 0;
                    result.Logs.Add($"Successfully deserialized as TableData using standard options");
                    result.Logs.Add($"Table: {result.SchemaName}.{result.TableName}");
                    result.Logs.Add($"Row count: {result.RowCount}");
                    
                    // Analyze the data
                    if (tableData.Rows != null && tableData.Rows.Count > 0)
                    {
                        result.HasRows = true;
                        
                        // Sample the first row
                        var firstRow = tableData.Rows[0];
                        result.Logs.Add($"First row has {firstRow.Values.Count} columns");
                        
                        // Get column names
                        var columnNames = new List<string>();
                        foreach (var kvp in firstRow.Values)
                        {
                            columnNames.Add(kvp.Key);
                        }
                        
                        result.Columns = columnNames;
                        result.Logs.Add($"Columns: {string.Join(", ", columnNames)}");
                        
                        // Sample values from the first row
                        result.Logs.Add("First row values:");
                        foreach (var kvp in firstRow.Values.Take(5))
                        {
                            string valueType = kvp.Value?.GetType().Name ?? "null";
                            string valueStr = kvp.Value?.ToString() ?? "null";
                            result.Logs.Add($"  {kvp.Key} ({valueType}): {valueStr}");
                        }
                        
                        // Check for null values in other rows
                        int nullValueCount = 0;
                        foreach (var row in tableData.Rows)
                        {
                            foreach (var kvp in row.Values)
                            {
                                if (kvp.Value == null)
                                    nullValueCount++;
                            }
                        }
                        
                        result.Logs.Add($"Found {nullValueCount} null values in the data");
                    }
                    else
                    {
                        result.Logs.Add("No rows found in the deserialized data");
                    }
                }
                catch (Exception ex)
                {
                    // Try with contractless resolver
                    try
                    {
                        var options = MessagePackSerializerOptions.Standard.WithResolver(
                            ContractlessStandardResolver.Instance);
                        var tableData = MessagePackSerializer.Deserialize<TableData>(dataBytes, options);
                        
                        result.Deserialized = true;
                        result.TableName = tableData.Schema?.Name ?? "Unknown";
                        result.SchemaName = tableData.Schema?.Schema ?? "dbo";
                        result.RowCount = tableData.Rows?.Count ?? 0;
                        result.Logs.Add($"Successfully deserialized as TableData using contractless resolver");
                        result.Logs.Add($"Table: {result.SchemaName}.{result.TableName}");
                        result.Logs.Add($"Row count: {result.RowCount}");
                        
                        // Same analysis as above
                        if (tableData.Rows != null && tableData.Rows.Count > 0)
                        {
                            result.HasRows = true;
                            
                            // Sample the first row
                            var firstRow = tableData.Rows[0];
                            result.Logs.Add($"First row has {firstRow.Values.Count} columns");
                            
                            // Get column names
                            var columnNames = new List<string>();
                            foreach (var kvp in firstRow.Values)
                            {
                                columnNames.Add(kvp.Key);
                            }
                            
                            result.Columns = columnNames;
                            result.Logs.Add($"Columns: {string.Join(", ", columnNames)}");
                        }
                    }
                    catch (Exception fallbackEx)
                    {
                        result.Logs.Add($"ERROR: Failed to deserialize with standard options: {ex.Message}");
                        result.Logs.Add($"ERROR: Failed to deserialize with contractless resolver: {fallbackEx.Message}");
                        
                        // Try to analyze the binary format
                        result.Logs.Add("Attempting to analyze binary format...");
                        AnalyzeBinaryFormat(dataBytes, result);
                    }
                }
                
                return result;
            }
            catch (Exception ex)
            {
                result.Logs.Add($"ERROR: Validation failed: {ex.Message}");
                if (ex.InnerException != null)
                {
                    result.Logs.Add($"Inner error: {ex.InnerException.Message}");
                }
                
                return result;
            }
        }
        
        private static void AnalyzeBinaryFormat(byte[] data, DiagnosticResult result)
        {
            // Calculate some basic statistics
            int nullBytes = data.Count(b => b == 0);
            double nullPercentage = (double)nullBytes / data.Length * 100;
            result.Logs.Add($"Zero bytes: {nullBytes} ({nullPercentage:F1}% of file)");
            
            // Check for MessagePack format markers
            var formatMarkers = new Dictionary<byte, int>();
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                if ((b >= 0x80 && b <= 0x8F) || // FixMap markers
                    (b >= 0x90 && b <= 0x9F) || // FixArray markers
                    (b >= 0xA0 && b <= 0xBF) || // FixStr markers
                    (b == 0xC0) ||              // Nil
                    (b == 0xC2 || b == 0xC3) || // Bool (false/true)
                    (b >= 0xCC && b <= 0xD3) || // Integer markers
                    (b >= 0xDC && b <= 0xDF) || // Array/Map markers
                    (b >= 0xE0))                // Negative FixInt
                {
                    if (formatMarkers.ContainsKey(b))
                        formatMarkers[b]++;
                    else
                        formatMarkers[b] = 1;
                }
            }
            
            if (formatMarkers.Count > 0)
            {
                result.Logs.Add("Found MessagePack format markers:");
                foreach (var kvp in formatMarkers.OrderByDescending(kv => kv.Value).Take(5))
                {
                    result.Logs.Add($"  0x{kvp.Key:X2}: {kvp.Value} occurrences");
                }
                
                // Look for string headers
                var stringHeaders = new List<int>();
                for (int i = 0; i < data.Length - 10; i++)
                {
                    byte b = data[i];
                    
                    // Check for string headers (FixStr or Str8/16/32)
                    if ((b >= 0xA0 && b <= 0xBF) || b == 0xD9 || b == 0xDA || b == 0xDB)
                    {
                        int length = 0;
                        
                        if (b >= 0xA0 && b <= 0xBF)
                        {
                            // FixStr (0xA0 - 0xBF): 0-31 bytes
                            length = b & 0x1F;
                        }
                        else if (b == 0xD9 && i + 1 < data.Length)
                        {
                            // Str8: 8-bit length
                            length = data[i + 1];
                        }
                        else if (b == 0xDA && i + 2 < data.Length)
                        {
                            // Str16: 16-bit length
                            length = (data[i + 1] << 8) | data[i + 2];
                        }
                        
                        if (length > 0 && length < 100 && i + length < data.Length)
                        {
                            stringHeaders.Add(i);
                            
                            // Try to extract string content
                            int strOffset = (b >= 0xA0 && b <= 0xBF) ? 1 :
                                          (b == 0xD9) ? 2 : 
                                          (b == 0xDA) ? 3 : 4;
                                          
                            StringBuilder strContent = new StringBuilder();
                            for (int j = i + strOffset; j < i + strOffset + length && j < data.Length; j++)
                            {
                                byte ch = data[j];
                                if (ch >= 32 && ch <= 126) // printable ASCII
                                    strContent.Append((char)ch);
                                else
                                    strContent.Append('.');
                            }
                            
                            if (strContent.Length > 0)
                            {
                                result.Logs.Add($"  Possible string at offset {i}: '{strContent}'");
                            }
                            
                            if (result.Logs.Count(l => l.StartsWith("  Possible string at offset")) >= 10)
                                break; // Limit to 10 strings
                        }
                    }
                }
                
                result.Logs.Add($"Found {stringHeaders.Count} possible string headers");
            }
            else
            {
                result.Logs.Add("No MessagePack format markers found - may not be MessagePack data");
            }
        }
    }
    
    public class DiagnosticResult
    {
        public string FilePath { get; set; } = string.Empty;
        public bool FileExists { get; set; }
        public long FileSize { get; set; }
        public bool IsCompressed { get; set; }
        public long DecompressedSize { get; set; }
        public bool Deserialized { get; set; }
        public bool HasRows { get; set; }
        public int RowCount { get; set; }
        public string TableName { get; set; } = string.Empty;
        public string SchemaName { get; set; } = string.Empty;
        public List<string> Columns { get; set; } = new List<string>();
        public List<string> Logs { get; set; } = new List<string>();
    }
}