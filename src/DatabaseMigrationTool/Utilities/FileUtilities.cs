using DatabaseMigrationTool.Constants;
using System.IO;
using System.IO.Compression;

namespace DatabaseMigrationTool.Utilities
{
    public static class FileUtilities
    {
        public static bool IsGZipCompressed(byte[] data)
        {
            return data.Length >= 2 && 
                   data[0] == DatabaseConstants.CompressionSignatures.GZip[0] && 
                   data[1] == DatabaseConstants.CompressionSignatures.GZip[1];
        }
        
        public static bool IsBZip2Compressed(byte[] data)
        {
            return data.Length >= 3 && 
                   data[0] == DatabaseConstants.CompressionSignatures.BZip2[0] && 
                   data[1] == DatabaseConstants.CompressionSignatures.BZip2[1] && 
                   data[2] == DatabaseConstants.CompressionSignatures.BZip2[2];
        }
        
        public static async Task<byte[]> ReadFileHeaderAsync(string filePath, int headerSize = DatabaseConstants.HeaderSampleSize)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");
                
            var fileInfo = new FileInfo(filePath);
            var bytesToRead = Math.Min(headerSize, (int)fileInfo.Length);
            var header = new byte[bytesToRead];
            
            using var stream = File.OpenRead(filePath);
            int totalBytesRead = 0;
            
            while (totalBytesRead < bytesToRead)
            {
                int bytesRead = await stream.ReadAsync(header, totalBytesRead, bytesToRead - totalBytesRead);
                if (bytesRead == 0)
                    break;
                totalBytesRead += bytesRead;
            }
            
            return header.Take(totalBytesRead).ToArray();
        }
        
        public static async Task<byte[]> DecompressGZipAsync(byte[] compressedData)
        {
            using var compressedStream = new MemoryStream(compressedData);
            using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var decompressedStream = new MemoryStream();
            
            await gzipStream.CopyToAsync(decompressedStream);
            return decompressedStream.ToArray();
        }
        
        public static async Task<byte[]> DecompressBZip2Async(byte[] compressedData)
        {
            using var compressedStream = new MemoryStream(compressedData);
            using var bzipStream = new SharpCompress.Compressors.BZip2.BZip2Stream(
                compressedStream, SharpCompress.Compressors.CompressionMode.Decompress, true);
            using var decompressedStream = new MemoryStream();
            
            await bzipStream.CopyToAsync(decompressedStream);
            return decompressedStream.ToArray();
        }
        
        public static string GetTableDataFileName(string schema, string tableName)
        {
            return string.Format(DatabaseConstants.TableNamePatterns.TableDataPattern, schema, tableName);
        }
        
        public static List<string> FindTableDataFiles(string dataDirectory, string tableFileName)
        {
            if (!Directory.Exists(dataDirectory))
                return new List<string>();
                
            var files = new List<string>();
            
            // Look for main data file
            var mainFile = Path.Combine(dataDirectory, tableFileName + DatabaseConstants.FileExtensions.Binary);
            if (File.Exists(mainFile))
            {
                files.Add(mainFile);
            }
            
            // Look for batch files
            var batchPattern = tableFileName + DatabaseConstants.TableNamePatterns.BatchFilePattern;
            var batchFiles = Directory.GetFiles(dataDirectory, batchPattern);
            files.AddRange(batchFiles);
            
            return files.OrderBy(f => f).ToList();
        }
        
        public static async Task<string> CreateLogFileAsync(string directory, string prefix)
        {
            Directory.CreateDirectory(directory);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var logFileName = $"{prefix}_log_{timestamp}{DatabaseConstants.FileExtensions.Text}";
            var logFilePath = Path.Combine(directory, logFileName);
            
            await File.WriteAllTextAsync(logFilePath, $"Log file created at: {DateTime.Now}\n");
            return logFilePath;
        }
        
        public static void EnsureDirectoryExists(string path)
        {
            if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
        
        public static long GetDirectorySize(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                return 0;
                
            return Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
                          .Sum(file => new FileInfo(file).Length);
        }
        
        public static void CleanupTempFiles(string directory, TimeSpan maxAge)
        {
            if (!Directory.Exists(directory))
                return;
                
            var cutoffTime = DateTime.Now - maxAge;
            var tempFiles = Directory.GetFiles(directory, "*.tmp")
                                  .Where(file => File.GetCreationTime(file) < cutoffTime);
                                  
            foreach (var file in tempFiles)
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
        
        public static string GetSafeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return string.Concat(fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        }
        
        public static bool IsValidPath(string path)
        {
            try
            {
                Path.GetFullPath(path);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}