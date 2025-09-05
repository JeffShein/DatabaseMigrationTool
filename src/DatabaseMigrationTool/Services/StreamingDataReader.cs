using DatabaseMigrationTool.Constants;
using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Utilities;
using MessagePack;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;

namespace DatabaseMigrationTool.Services
{
    public interface IStreamingDataReader : IDisposable
    {
        IAsyncEnumerable<RowData> ReadTableDataAsync(string filePath, CancellationToken cancellationToken = default);
        IAsyncEnumerable<TableData> ReadTableBatchesAsync(string dataDirectory, string tableFileName, CancellationToken cancellationToken = default);
        Task<OperationResult<TableData?>> ReadSingleTableDataAsync(string filePath, CancellationToken cancellationToken = default);
    }
    
    public class StreamingDataReader : IStreamingDataReader
    {
        private readonly Dictionary<string, Stream> _openStreams = new();
        private bool _disposed;
        
        public async IAsyncEnumerable<RowData> ReadTableDataAsync(string filePath, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
                yield break;
                
            var tableDataResult = await ReadSingleTableDataAsync(filePath, cancellationToken);
            if (!tableDataResult.Success || tableDataResult.Data?.Rows == null)
                yield break;
                
            foreach (var row in tableDataResult.Data.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return row;
            }
        }
        
        public async IAsyncEnumerable<TableData> ReadTableBatchesAsync(string dataDirectory, string tableFileName, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var files = FileUtilities.FindTableDataFiles(dataDirectory, tableFileName);
            
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var tableDataResult = await ReadSingleTableDataAsync(file, cancellationToken);
                if (tableDataResult.Success && tableDataResult.Data != null)
                {
                    yield return tableDataResult.Data;
                }
            }
        }
        
        public async Task<OperationResult<TableData?>> ReadSingleTableDataAsync(string filePath, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return OperationResult<TableData?>.Fail($"File not found: {filePath}");
                }
                
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length < DatabaseConstants.MinValidFileSize)
                {
                    return OperationResult<TableData?>.Ok(null);
                }
                
                // Read and decompress file data
                var decompressedData = await ReadAndDecompressFileAsync(filePath, cancellationToken);
                if (decompressedData == null)
                {
                    return OperationResult<TableData?>.Fail("Failed to read or decompress file");
                }
                
                // Deserialize using MessagePack
                var tableData = await DeserializeTableDataAsync(decompressedData, cancellationToken);
                return OperationResult<TableData?>.Ok(tableData);
            }
            catch (Exception ex)
            {
                return OperationResult<TableData?>.Fail(ex, $"ReadSingleTableData: {filePath}");
            }
        }
        
        private async Task<byte[]?> ReadAndDecompressFileAsync(string filePath, CancellationToken cancellationToken)
        {
            try
            {
                // Read file header to determine compression
                var header = await FileUtilities.ReadFileHeaderAsync(filePath, DatabaseConstants.HeaderSampleSize);
                
                byte[] fileData;
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 8192, useAsync: true))
                {
                    fileData = new byte[fileStream.Length];
                    await fileStream.ReadExactlyAsync(fileData, cancellationToken);
                }
                
                // Decompress based on file signature
                if (FileUtilities.IsGZipCompressed(header))
                {
                    return await DecompressGZipStreamingAsync(fileData, cancellationToken);
                }
                else if (FileUtilities.IsBZip2Compressed(header))
                {
                    return await FileUtilities.DecompressBZip2Async(fileData);
                }
                else
                {
                    return fileData;
                }
            }
            catch
            {
                return null;
            }
        }
        
        private async Task<byte[]> DecompressGZipStreamingAsync(byte[] compressedData, CancellationToken cancellationToken)
        {
            using var compressedStream = new MemoryStream(compressedData);
            using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var decompressedStream = new MemoryStream();
            
            var buffer = new byte[8192];
            int bytesRead;
            
            while ((bytesRead = await gzipStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await decompressedStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            
            return decompressedStream.ToArray();
        }
        
        private async Task<TableData?> DeserializeTableDataAsync(byte[] data, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    // Try standard options first
                    var options = MessagePackSerializerOptions.Standard;
                    return MessagePackSerializer.Deserialize<TableData>(data, options);
                }
                catch
                {
                    try
                    {
                        // Fallback to contractless resolver
                        var fallbackOptions = MessagePackSerializerOptions.Standard
                            .WithResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance);
                        return MessagePackSerializer.Deserialize<TableData>(data, fallbackOptions);
                    }
                    catch
                    {
                        return null;
                    }
                }
            }, cancellationToken);
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (var stream in _openStreams.Values)
                {
                    try
                    {
                        stream?.Dispose();
                    }
                    catch
                    {
                        // Ignore disposal errors
                    }
                }
                _openStreams.Clear();
                _disposed = true;
            }
        }
    }
    
    public class StreamingDataWriter : IDisposable
    {
        private readonly Dictionary<string, Stream> _openStreams = new();
        private bool _disposed;
        
        public async Task<OperationResult> WriteTableDataStreamAsync(
            IAsyncEnumerable<RowData> rows, 
            TableSchema schema,
            string outputPath,
            int batchSize = DatabaseConstants.DefaultBatchSize,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var fileName = FileUtilities.GetTableDataFileName(schema.Schema ?? "dbo", schema.Name);
                var filePath = Path.Combine(outputPath, DatabaseConstants.DirectoryNames.Data, fileName + DatabaseConstants.FileExtensions.Binary);
                
                FileUtilities.EnsureDirectoryExists(Path.GetDirectoryName(filePath)!);
                
                var batch = new List<RowData>();
                int batchNumber = 0;
                
                await foreach (var row in rows.WithCancellation(cancellationToken))
                {
                    batch.Add(row);
                    
                    if (batch.Count >= batchSize)
                    {
                        await WriteBatchAsync(batch, schema, filePath, batchNumber++, cancellationToken);
                        batch.Clear();
                    }
                }
                
                // Write final batch if any data remains
                if (batch.Count > 0)
                {
                    await WriteBatchAsync(batch, schema, filePath, batchNumber, cancellationToken);
                }
                
                return OperationResult.Ok();
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex, "WriteTableDataStream");
            }
        }
        
        private async Task WriteBatchAsync(List<RowData> rows, TableSchema schema, string baseFilePath, int batchNumber, CancellationToken cancellationToken)
        {
            var tableData = new TableData
            {
                Schema = schema,
                Rows = rows
            };
            
            var filePath = batchNumber == 0 ? baseFilePath : 
                Path.ChangeExtension(baseFilePath, null) + $"_batch{batchNumber:D4}" + DatabaseConstants.FileExtensions.Binary;
            
            // Serialize to MessagePack
            var serializedData = MessagePackSerializer.Serialize(tableData, MessagePackSerializerOptions.Standard);
            
            // Compress with GZip
            using var outputStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 8192, useAsync: true);
            using var gzipStream = new GZipStream(outputStream, CompressionLevel.Optimal);
            
            await gzipStream.WriteAsync(serializedData, 0, serializedData.Length, cancellationToken);
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (var stream in _openStreams.Values)
                {
                    try
                    {
                        stream?.Dispose();
                    }
                    catch
                    {
                        // Ignore disposal errors
                    }
                }
                _openStreams.Clear();
                _disposed = true;
            }
        }
    }
}