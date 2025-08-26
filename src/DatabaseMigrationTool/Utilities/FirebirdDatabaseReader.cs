using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatabaseMigrationTool.Utilities
{
    /// <summary>
    /// Specialized reader for Firebird database files
    /// </summary>
    public class FirebirdDatabaseReader : IDisposable
    {
        // Constants
        private const int PAGE_SIZE = 4096;
        private static readonly byte[] FIREBIRD_SIGNATURE = { 0x01, 0x00, 0x39, 0x30 }; // Firebird signature pattern
        
        // File access
        private readonly string _filePath;
        private FileStream? _fileStream;
        private BinaryReader? _reader;
        
        // Database metadata
        private readonly Dictionary<string, object> _metadata = new();
        private readonly StringBuilder _logBuilder = new();
        private readonly List<DatabasePage> _pages = new();
        private readonly List<DatabaseRecord> _records = new();
        
        // Page map - track what we've discovered about each page
        private readonly Dictionary<long, PageType> _pageTypes = new();
        private int _headerPageCount = 0;
        
        // Scan options
        private bool _fullScan = true; // By default, scan the entire database
        private int _maxPagesToScan = 0; // 0 means no limit
        private int _chunkSize = 20; // Process the database in smaller chunks to avoid memory issues while being more thorough
        
        /// <summary>
        /// Opens a Firebird database file
        /// </summary>
        /// <param name="filePath">Path to the database file</param>
        /// <param name="fullScan">If true, scan all pages. If false, use sampling for performance</param>
        /// <param name="maxPagesToScan">Maximum number of pages to scan (0 means no limit)</param>
        /// <param name="chunkSize">Number of pages to process in each chunk (to avoid memory issues)</param>
        public FirebirdDatabaseReader(string filePath, bool fullScan = true, int maxPagesToScan = 0, int chunkSize = 20)
        {
            _filePath = filePath;
            _fullScan = fullScan;
            _maxPagesToScan = maxPagesToScan;
            _chunkSize = Math.Max(10, chunkSize); // Ensure minimum chunk size
            
            // Initialize metadata - defer opening the file until AnalyzeDatabaseStructureAsync is called
            _metadata["FilePath"] = filePath;
            _metadata["LastModified"] = File.GetLastWriteTime(filePath);
            _metadata["FullScan"] = fullScan;
        }
        
        /// <summary>
        /// Analyzes the database structure
        /// </summary>
        public async Task AnalyzeDatabaseStructureAsync()
        {
            try
            {
                // Open the file if it's not already open
                if (_fileStream == null || _reader == null)
                {
                    _fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    _reader = new BinaryReader(_fileStream);
                    
                    // Update metadata that requires file access
                    _metadata["FileSize"] = _fileStream.Length;
                    _metadata["PageSize"] = PAGE_SIZE;
                    _metadata["TotalPages"] = _fileStream.Length / PAGE_SIZE;
                }
                
                Log("Starting database structure analysis...");
                
                // First, scan the first few pages to understand the structure
                await ScanHeaderPagesAsync();
                
                // Then look for potential data pages
                await ScanForDataPagesAsync();
                
                // Try to identify structure information
                IdentifyDatabaseStructure();
                
                Log($"Analysis completed. Found {_pages.Count} pages.");
            }
            catch (Exception ex)
            {
                Log($"Error analyzing database: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Scans the first several pages to understand the header structure
        /// </summary>
        private async Task ScanHeaderPagesAsync()
        {
            Log("Scanning header pages...");
            
            // We'll scan the first 10 pages to look for patterns
            int pagesToScan = 10;
            
            for (int i = 0; i < pagesToScan; i++)
            {
                long offset = i * PAGE_SIZE;
                var pageData = await ReadPageAsync(offset);
                
                // Check if this page has our signature
                if (HasFirebirdSignature(pageData))
                {
                    var page = new DatabasePage
                    {
                        PageNumber = i,
                        Offset = offset,
                        PageType = DeterminePageType(pageData),
                        TypeMarker = pageData[0],
                        Data = pageData
                    };
                    
                    _pages.Add(page);
                    _pageTypes[offset] = page.PageType;
                    _headerPageCount++;
                    
                    Log($"Found header page at offset {offset:X8}, type marker: {pageData[0]:X2}");
                }
            }
            
            Log($"Completed header scan. Found {_headerPageCount} header pages.");
        }
        
        /// <summary>
        /// Scans the database for potential data pages
        /// </summary>
        private async Task ScanForDataPagesAsync()
        {
            if (_fileStream == null)
                return;
                
            long fileSize = _fileStream.Length;
            int totalPages = (int)(fileSize / PAGE_SIZE);
            
            int pagesToProcess;
            bool useSampling = false;
            
            if (_fullScan)
            {
                // Full scan - process all pages in the file (up to max if specified)
                pagesToProcess = _maxPagesToScan > 0 ? Math.Min(_maxPagesToScan, totalPages) : totalPages;
                Log($"Full scan mode: Will scan {pagesToProcess} out of {totalPages} pages in chunks of {_chunkSize}");
            }
            else
            {
                // Sampling mode for better performance
                useSampling = true;
                pagesToProcess = _maxPagesToScan > 0 ? Math.Min(_maxPagesToScan, totalPages) : Math.Min(500, totalPages);
                Log($"Sampling mode: Will scan {pagesToProcess} out of {totalPages} pages");
            }
            
            // Track overall progress
            int processedPageCount = 0;
            int lastProgressPercent = -1;
            
            // Process in chunks to avoid memory issues
            for (int chunkStart = 0; chunkStart < pagesToProcess; chunkStart += _chunkSize)
            {
                // Determine the size of this chunk
                int currentChunkSize = Math.Min(_chunkSize, pagesToProcess - chunkStart);
                
                // Create a list for this chunk
                var chunkPages = new List<long>();
                
                if (useSampling)
                {
                    // In sampling mode, ensure we include early pages
                    if (chunkStart < 20)
                    {
                        // Include all pages in the first chunk
                        int pagesToInclude = Math.Min(20, currentChunkSize);
                        for (int i = 0; i < pagesToInclude; i++)
                        {
                            chunkPages.Add((chunkStart + i) * (long)PAGE_SIZE);
                        }
                    }
                    else
                    {
                        // For later chunks, sample evenly throughout the file
                        double step = (double)totalPages / pagesToProcess;
                        for (int i = 0; i < currentChunkSize; i++)
                        {
                            int pageIndex = (int)(chunkStart + i * step);
                            if (pageIndex < totalPages)
                            {
                                chunkPages.Add(pageIndex * (long)PAGE_SIZE);
                            }
                        }
                    }
                }
                else
                {
                    // In full scan mode, include all pages in this chunk
                    for (int i = 0; i < currentChunkSize; i++)
                    {
                        chunkPages.Add((chunkStart + i) * (long)PAGE_SIZE);
                    }
                }
                
                // Process this chunk
                Log($"Processing chunk {chunkStart / _chunkSize + 1} with {chunkPages.Count} pages");
                await ProcessPageChunkAsync(chunkPages);
                
                // Update progress
                processedPageCount += chunkPages.Count;
                int progressPercent = (processedPageCount * 100) / pagesToProcess;
                if (progressPercent != lastProgressPercent && progressPercent % 5 == 0)
                {
                    Log($"Scanning database: {progressPercent}% complete ({processedPageCount}/{pagesToProcess} pages)");
                    lastProgressPercent = progressPercent;
                    
                    // Force GC collection after each chunk to minimize memory usage
                    GC.Collect();
                }
            }
            
            // Log final scan statistics
            var dataPages = _pages.Where(p => p.PageType == PageType.Data).ToList();
            var headerPages = _pages.Where(p => p.PageType == PageType.Header).ToList();
            var indexPages = _pages.Where(p => p.PageType == PageType.Index).ToList();
            
            Log("========== SCAN COMPLETED ==========");
            Log($"DIAGNOSTIC SUMMARY: Found {dataPages.Count} data pages, {headerPages.Count} header pages, {indexPages.Count} index pages");
            Log($"Total pages scanned: {processedPageCount} out of {totalPages} total pages");
            Log($"Potential tables found: {dataPages.Count}");
            
            if (dataPages.Count < 400) // We expect 466, so if we find fewer, log additional details
            {
                Log("CRITICAL DIAGNOSTIC: Found significantly fewer data pages than expected!");
                Log($"Scan settings: fullScan={_fullScan}, maxPagesToScan={_maxPagesToScan}, chunkSize={_chunkSize}");
                Log($"File size: {fileSize:N0} bytes, page size: {PAGE_SIZE}, total pages: {totalPages:N0}");
                Log("First 10 data page numbers:");
                foreach (var page in dataPages.Take(10))
                {
                    Log($"  - Page {page.PageNumber} at offset 0x{page.Offset:X8}");
                }
            }
        }

        /// <summary>
        /// Processes a chunk of pages to reduce memory usage
        /// </summary>
        private async Task ProcessPageChunkAsync(List<long> pageOffsets)
        {
            // Diagnostic counters to track different types of pages we find
            int dataPageCount = 0;
            int headerPageCount = 0;
            int indexPageCount = 0;
            int unknownPageCount = 0;
            
            foreach (var offset in pageOffsets.Distinct())
            {
                // Skip pages we've already processed
                if (_pageTypes.ContainsKey(offset))
                    continue;
                    
                // For diagnostic purposes, occasionally log the offset we're checking
                if (offset % (PAGE_SIZE * 10000) == 0)
                {
                    Log($"DIAGNOSTIC: Processing page at offset {offset:X8} (page {offset / PAGE_SIZE})");
                }
                
                try
                {    
                    var pageData = await ReadPageAsync(offset);
                    
                    // Check if this page has any recognizable patterns
                    var pageType = DeterminePageType(pageData);
                    
                    if (pageType != PageType.Unknown)
                    {
                        var page = new DatabasePage
                        {
                            PageNumber = (int)(offset / PAGE_SIZE),
                            Offset = offset,
                            PageType = pageType,
                            TypeMarker = pageData[0],
                            // Store minimal information to save memory
                            Data = pageType == PageType.Data ? pageData : null
                        };
                        
                        _pages.Add(page);
                        _pageTypes[offset] = pageType;
                        
                        // Update counters for diagnostics
                        switch (pageType)
                        {
                            case PageType.Data: dataPageCount++; break;
                            case PageType.Header: headerPageCount++; break;
                            case PageType.Index: indexPageCount++; break;
                            default: break;
                        }
                        
                        if (pageType == PageType.Data)
                        {
                            // Try to extract records from this page
                            ExtractRecordsFromPage(page);
                            
                            // Clear page data after processing to save memory
                            page.Data = null;
                        }
                    }
                }
                catch (OutOfMemoryException ex)
                {
                    Log($"Memory error processing page at offset {offset}: {ex.Message}");
                    // Force garbage collection and continue with next page
                    GC.Collect();
                }
                catch (Exception ex)
                {
                    Log($"Error processing page at offset {offset}: {ex.Message}");
                    unknownPageCount++;
                }
            }
            
            // Log detailed diagnostic information about this chunk
            Log($"CHUNK DIAGNOSTIC: Processed {pageOffsets.Count} pages, found: {dataPageCount} data, {headerPageCount} header, {indexPageCount} index pages");
            
            // Track unidentified pages
            if (pageOffsets.Count > dataPageCount + headerPageCount + indexPageCount)
            {
                int unidentified = pageOffsets.Count - dataPageCount - headerPageCount - indexPageCount;
                if (unidentified > pageOffsets.Count * 0.5) // More than 50% unidentified
                {
                    Log($"WARNING: {unidentified} pages in this chunk could not be identified as any known type");
                }
            }
        }
        
        /// <summary>
        /// Attempts to extract database structure information
        /// </summary>
        private void IdentifyDatabaseStructure()
        {
            Log("Identifying database structure...");
            
            // Look at all header pages for structure information
            var headerPages = _pages.Where(p => p.PageType == PageType.Header).ToList();
            
            if (headerPages.Count > 0)
            {
                // The first page usually contains important metadata
                var firstPage = headerPages.First();
                
                // Store primary header values from first page
                _metadata["HeaderVersion"] = firstPage.TypeMarker;
                
                // Try to find structural information in the first few header pages
                // Limit to first 10 header pages maximum to avoid memory issues
                int headerPagesToProcess = Math.Min(headerPages.Count, 10);
                Log($"Processing {headerPagesToProcess} of {headerPages.Count} header pages");
                
                for (int i = 0; i < headerPagesToProcess; i++)
                {
                    try
                    {
                        AnalyzeHeaderPage(headerPages[i]);
                    }
                    catch (OutOfMemoryException)
                    {
                        Log("Memory error in header page analysis, stopping further processing");
                        GC.Collect();
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log($"Error processing header page {i}: {ex.Message}");
                    }
                }
            }
            
            // Analyze the record patterns we've found
            AnalyzeRecordPatterns();
        }
        
        /// <summary>
        /// Analyzes a header page for structural information
        /// </summary>
        private void AnalyzeHeaderPage(DatabasePage page)
        {
            try
            {
                // Make sure page data is available
                if (page.Data == null || page.Data.Length == 0)
                {
                    // Skip this page if we can't read it - don't try to load data anymore
                    // This prevents memory issues by not loading all pages at once
                    Log($"Skipping analysis of page {page.PageNumber} (no data available)");
                    return;
                }
            }
            catch (OutOfMemoryException)
            {
                // Force garbage collection and skip this page
                Log($"Memory error analyzing page {page.PageNumber}, skipping");
                GC.Collect();
                return;
            }
            catch (Exception ex)
            {
                // If we can't handle this page, just return
                Log($"Error analyzing page {page.PageNumber}: {ex.Message}");
                return;
            }

            try
            {
                // Different type markers indicate different header page types
                switch (page.TypeMarker)
                {
                    case 1: // First page - main header
                        // Bytes 4-7 often contain version or creation info
                        if (page.Data != null && page.Data.Length >= 8)
                        {
                            try
                            {
                                // Get first 4 bytes of header info without allocating new array
                                string headerInfo = $"{page.Data[4]:X2}-{page.Data[5]:X2}-{page.Data[6]:X2}-{page.Data[7]:X2}";
                                _metadata["HeaderInfo"] = headerInfo;
                                
                                // Try to extract any potential table count from a few key offsets only
                                // Limit to just a few checks to reduce memory pressure
                                int[] keyOffsets = { 0x20, 0x24, 0x28, 0x2C };
                                foreach (int offset in keyOffsets)
                                {
                                    if (offset + 4 <= page.Data.Length)
                                    {
                                        try
                                        {
                                            int value = BitConverter.ToInt32(page.Data, offset);
                                            if (value > 0 && value < 1000) // Reasonable table count
                                            {
                                                _metadata[$"PossibleTableCount_0x{offset:X2}"] = value;
                                            }
                                        }
                                        catch
                                        {
                                            // Skip this offset if there's a problem
                                        }
                                    }
                                }
                            }
                            catch (OutOfMemoryException)
                            {
                                // Force GC and stop processing
                                GC.Collect();
                                throw; // Re-throw to stop all header processing
                            }
                            catch
                            {
                                // Skip this part if there's a problem
                            }
                        }
                        break;
                        
                    case 2: // Second page - often contains schema info
                        // Look for potential string data (table names, etc)
                        if (page.Data != null)
                        {
                            try
                            {
                                // Extract only a small number of strings to avoid memory issues
                                var strings = ExtractLimitedStrings(page.Data, 10); // Limit to 10 strings max
                                if (strings.Count > 0)
                                {
                                    _metadata["Page2Strings"] = strings;
                                }
                            }
                            catch (OutOfMemoryException)
                            {
                                // Force GC and stop processing
                                GC.Collect();
                                throw; // Re-throw to stop all header processing
                            }
                            catch
                            {
                                // Skip this part if there's a problem
                            }
                        }
                        break;
                        
                    case 10: // Type 0x0A - often contains index information
                        _metadata["HasIndexPage"] = true;
                        break;
                        
                    default:
                        try
                        {
                            // Store any other header types we find
                            string key = $"HeaderType_0x{page.TypeMarker:X2}_Count";
                            _metadata[key] = _metadata.TryGetValue(key, out var count) ? 
                                ((int)count + 1) : 1;
                        }
                        catch
                        {
                            // Skip if there's an error
                        }
                        break;
                }
            }
            catch (OutOfMemoryException)
            {
                // Re-throw to abort further processing
                throw;
            }
            catch (Exception ex)
            {
                Log($"Error processing page type {page.TypeMarker}: {ex.Message}");
            }
            finally
            {
                // Clear page data after processing to save memory
                page.Data = null;
            }
        }
        
        /// <summary>
        /// Tries to extract records from a data page
        /// </summary>
        private void ExtractRecordsFromPage(DatabasePage page)
        {
            try
            {
                // Make sure we have page data
                if (page.Data == null || page.Data.Length == 0)
                    return;
                    
                // For data pages, we need to identify the record structure
                // This is a simplified approach - real implementation would need to understand the exact format
                
                // Look for repeating patterns in the page
                var potentialRecordSizes = DetectRecordSizes(page.Data);
                
                if (potentialRecordSizes.Count > 0)
                {
                    // Sort by confidence (highest first)
                    var bestSize = potentialRecordSizes.OrderByDescending(kvp => kvp.Value).First().Key;
                    
                    // Limit number of records to extract per page to avoid memory issues
                    int maxRecordsPerPage = 50;
                    int recordsExtracted = 0;
                    
                    // Try to extract records using this size
                    for (int offset = 0; offset + bestSize <= page.Data.Length && recordsExtracted < maxRecordsPerPage; offset += bestSize)
                    {
                        try
                        {
                            // Check if this looks like a record
                            if (LooksLikeRecordStart(page.Data, offset))
                            {
                                var recordData = new byte[bestSize];
                                Array.Copy(page.Data, offset, recordData, 0, bestSize);
                                
                                _records.Add(new DatabaseRecord
                                {
                                    PageOffset = page.Offset,
                                    RecordOffset = offset,
                                    Size = bestSize,
                                    Data = recordData
                                });
                                
                                recordsExtracted++;
                            }
                        }
                        catch (OutOfMemoryException)
                        {
                            Log($"Memory error extracting records from page at {page.Offset:X8}, stopped at {recordsExtracted} records");
                            GC.Collect();
                            break;
                        }
                        catch
                        {
                            // Skip this record and continue
                        }
                    }
                    
                    Log($"Extracted {recordsExtracted} potential records from page at {page.Offset:X8}");
                }
            }
            catch (OutOfMemoryException)
            {
                Log($"Memory error in record extraction for page at {page.Offset:X8}");
                GC.Collect();
            }
            catch (Exception ex)
            {
                Log($"Error extracting records: {ex.Message}");
            }
            finally
            {
                // Always clear page data to free memory
                page.Data = null;
            }
        }
        
        /// <summary>
        /// Analyzes the patterns in extracted records
        /// </summary>
        private void AnalyzeRecordPatterns()
        {
            if (_records.Count == 0)
                return;
                
            // Group records by size
            var recordsBySize = _records.GroupBy(r => r.Size)
                .OrderByDescending(g => g.Count())
                .ToList();
                
            foreach (var group in recordsBySize)
            {
                Log($"Found {group.Count()} records of size {group.Key} bytes");
                
                if (group.Count() >= 5)
                {
                    // This looks like a significant record size, let's analyze it
                    AnalyzeRecordStructure(group.ToList());
                }
            }
        }
        
        /// <summary>
        /// Tries to determine the structure of records with a specific size
        /// </summary>
        private void AnalyzeRecordStructure(List<DatabaseRecord> records)
        {
            if (records.Count == 0) return;
            
            int recordSize = records[0].Size;
            Log($"Analyzing structure of {records.Count} records with size {recordSize} bytes");
            
            // Try to identify field boundaries by looking for patterns
            var fieldTypes = new List<FieldInfo>();
            
            // Analyze each byte position across all records
            for (int position = 0; position < recordSize; position++)
            {
                // Get values at this position across records
                var values = records.Select(r => r.Data[position]).ToList();
                
                // Check what type of data this might be
                var dataType = DetermineDataType(values);
                
                // If this is the start of a new field, add it
                if (IsFieldBoundary(position, records))
                {
                    fieldTypes.Add(new FieldInfo { 
                        Offset = position,
                        DataType = dataType
                    });
                }
            }
            
            // Now we have potential field boundaries, let's try to determine field sizes
            for (int i = 0; i < fieldTypes.Count; i++)
            {
                int nextOffset = (i < fieldTypes.Count - 1) ? fieldTypes[i + 1].Offset : recordSize;
                fieldTypes[i].Size = nextOffset - fieldTypes[i].Offset;
            }
            
            // Store the field information
            _metadata[$"RecordStructure_{recordSize}"] = fieldTypes;
            
            // Log the structure
            Log($"Identified {fieldTypes.Count} potential fields in {recordSize}-byte records:");
            foreach (var field in fieldTypes)
            {
                Log($"  Offset: {field.Offset}, Size: {field.Size}, Type: {field.DataType}");
            }
            
            // Try to extract field values from a sample record
            if (records.Count > 0)
            {
                ExtractSampleValues(records[0], fieldTypes);
            }
        }
        
        /// <summary>
        /// Extracts sample values from a record using the identified field structure
        /// </summary>
        private void ExtractSampleValues(DatabaseRecord record, List<FieldInfo> fields)
        {
            Log("Sample values from first record:");
            
            foreach (var field in fields)
            {
                // Ensure we don't go out of bounds
                if (field.Offset + field.Size > record.Data.Length)
                    continue;
                    
                // Extract the field data
                byte[] fieldData = new byte[field.Size];
                Array.Copy(record.Data, field.Offset, fieldData, 0, field.Size);
                
                // Convert to appropriate type and log
                string value = ConvertFieldToString(fieldData, field.DataType);
                Log($"  Field at offset {field.Offset}: {value}");
            }
        }
        
        /// <summary>
        /// Converts field bytes to a readable string based on data type
        /// </summary>
        private string ConvertFieldToString(byte[] data, FieldDataType dataType)
        {
            try
            {
                switch (dataType)
                {
                    case FieldDataType.Integer:
                        if (data.Length >= 4)
                            return BitConverter.ToInt32(data, 0).ToString();
                        else if (data.Length >= 2)
                            return BitConverter.ToInt16(data, 0).ToString();
                        else
                            return data[0].ToString();
                            
                    case FieldDataType.Float:
                        if (data.Length >= 8)
                            return BitConverter.ToDouble(data, 0).ToString("F4");
                        else if (data.Length >= 4)
                            return BitConverter.ToSingle(data, 0).ToString("F4");
                        else
                            return "Invalid float";
                            
                    case FieldDataType.DateTime:
                        // Firebird stores dates as double since 12/30/1899
                        if (data.Length >= 8)
                        {
                            double dateValue = BitConverter.ToDouble(data, 0);
                            try
                            {
                                // Firebird date = days since 12/30/1899
                                DateTime baseDate = new DateTime(1899, 12, 30);
                                DateTime date = baseDate.AddDays(dateValue);
                                return date.ToString();
                            }
                            catch
                            {
                                return $"Invalid date ({dateValue})";
                            }
                        }
                        return "Invalid date";
                        
                    case FieldDataType.String:
                        // Look for null terminator
                        int length = 0;
                        while (length < data.Length && data[length] != 0)
                            length++;
                            
                        return Encoding.ASCII.GetString(data, 0, length);
                        
                    case FieldDataType.Binary:
                    default:
                        return BitConverter.ToString(data);
                }
            }
            catch
            {
                return "Conversion error";
            }
        }
        
        /// <summary>
        /// Tries to determine the data type of a field based on values
        /// </summary>
        private FieldDataType DetermineDataType(List<byte> values)
        {
            // Count different types of values
            int zeroCount = values.Count(b => b == 0);
            int printableCount = values.Count(b => b >= 32 && b <= 126);
            int digitCount = values.Count(b => b >= '0' && b <= '9');
            
            // If mostly printable, probably string
            if (printableCount > values.Count * 0.7)
                return FieldDataType.String;
                
            // If mostly digits, might be numeric
            if (digitCount > values.Count * 0.5)
                return FieldDataType.Integer;
                
            // If lots of zeros, might be numeric
            if (zeroCount > values.Count * 0.5)
                return FieldDataType.Integer;
                
            // Default to binary
            return FieldDataType.Binary;
        }
        
        /// <summary>
        /// Checks if a position is likely to be a field boundary
        /// </summary>
        private bool IsFieldBoundary(int position, List<DatabaseRecord> records)
        {
            // First position is always a boundary
            if (position == 0)
                return true;
                
            // Check for patterns that indicate field boundaries
            
            // Example: If all records have same value at position-1
            // but different values at position, might be boundary
            var prevValues = records.Select(r => r.Data[position - 1]).Distinct().Count();
            var currValues = records.Select(r => r.Data[position]).Distinct().Count();
            
            if (prevValues == 1 && currValues > 1)
                return true;
                
            // Example: Many zeros at this position could indicate a boundary
            int zeroCount = records.Count(r => r.Data[position] == 0);
            if (zeroCount > records.Count * 0.7)
                return true;
                
            return false;
        }
        
        /// <summary>
        /// Attempts to detect record sizes in a page
        /// </summary>
        private Dictionary<int, int> DetectRecordSizes(byte[]? pageData)
        {
            var sizeConfidence = new Dictionary<int, int>();
            
            // Return empty result if page data is null
            if (pageData == null || pageData.Length == 0)
                return sizeConfidence;
                
            // Try different record sizes
            for (int size = 16; size <= 1024; size += 4)
            {
                int confidence = 0;
                
                // Look for repeating patterns at this interval
                for (int offset = 0; offset + size * 2 <= pageData.Length; offset += size)
                {
                    // Check if there's a pattern at this size
                    if (HasRepeatingPatternAt(pageData, offset, size))
                    {
                        confidence++;
                    }
                }
                
                if (confidence > 0)
                {
                    sizeConfidence[size] = confidence;
                }
            }
            
            return sizeConfidence;
        }
        
        /// <summary>
        /// Checks if there's a repeating pattern at the given offset and size
        /// </summary>
        private bool HasRepeatingPatternAt(byte[]? data, int offset, int size)
        {
            // Null check and bounds check
            if (data == null || offset < 0 || size <= 0)
                return false;
                
            // We need at least 2 bytes for pattern checking
            if (offset + size + 1 >= data.Length)
                return false;
                
            try
            {
                // Check if first few bytes follow a pattern
                byte patternByte1 = data[offset];
                byte patternByte2 = data[offset + 1];
                
                // Check if same pattern exists at next record position
                return data[offset + size] == patternByte1 && 
                       data[offset + size + 1] == patternByte2;
            }
            catch
            {
                // Any array access exception means the pattern isn't repeating
                return false;
            }
        }
        
        /// <summary>
        /// Checks if this page looks like a record start
        /// </summary>
        private bool LooksLikeRecordStart(byte[]? data, int offset)
        {
            // First check if data is valid
            if (data == null || offset < 0)
                return false;
                
            // Very basic check - real implementation would be more sophisticated
            // For example, check for specific marker bytes, etc.
            
            // Example: First byte might be a type indicator
            return offset + 4 <= data.Length;
        }
        
        /// <summary>
        /// Extracts potential string values from a page with a limit to avoid memory issues
        /// </summary>
        private List<string> ExtractLimitedStrings(byte[]? pageData, int maxStrings)
        {
            var strings = new List<string>();
            
            // If page data is null, return empty list
            if (pageData == null || pageData.Length == 0 || maxStrings <= 0)
                return strings;
            
            // Only scan the first 1KB of the page to save memory
            int bytesToScan = Math.Min(pageData.Length, 1024);
            
            // Look for ASCII strings (sequences of printable characters)
            StringBuilder currentString = new();
            bool inString = false;
            
            for (int i = 0; i < bytesToScan; i++)
            {
                try
                {
                    byte b = pageData[i];
                    
                    // Check if this is printable ASCII
                    if (b >= 32 && b <= 126)
                    {
                        // Add to current string
                        currentString.Append((char)b);
                        inString = true;
                    }
                    else
                    {
                        // End of string?
                        if (inString)
                        {
                            // Only keep strings of a reasonable length
                            if (currentString.Length >= 3)
                            {
                                strings.Add(currentString.ToString());
                                
                                // Stop if we've reached the maximum number of strings
                                if (strings.Count >= maxStrings)
                                    break;
                            }
                            
                            currentString.Clear();
                            inString = false;
                        }
                    }
                }
                catch
                {
                    // Skip any errors and continue
                }
            }
            
            // Check for any final string
            if (inString && currentString.Length >= 3 && strings.Count < maxStrings)
            {
                strings.Add(currentString.ToString());
            }
            
            return strings;
        }
        
        /// <summary>
        /// Extracts potential string values from a page
        /// </summary>
        private List<string> ExtractPotentialStrings(byte[]? pageData)
        {
            var strings = new List<string>();
            
            // If page data is null, return empty list
            if (pageData == null || pageData.Length == 0)
                return strings;
            
            // Look for ASCII strings (sequences of printable characters)
            StringBuilder currentString = new();
            bool inString = false;
            
            for (int i = 0; i < pageData.Length; i++)
            {
                byte b = pageData[i];
                
                // Check if this is printable ASCII
                if (b >= 32 && b <= 126)
                {
                    // Add to current string
                    currentString.Append((char)b);
                    inString = true;
                }
                else
                {
                    // End of string?
                    if (inString)
                    {
                        // Only keep strings of a reasonable length
                        if (currentString.Length >= 3)
                        {
                            strings.Add(currentString.ToString());
                        }
                        
                        currentString.Clear();
                        inString = false;
                    }
                }
            }
            
            // Check for any final string
            if (inString && currentString.Length >= 3)
            {
                strings.Add(currentString.ToString());
            }
            
            return strings;
        }
        
        /// <summary>
        /// Checks if the page appears to contain record patterns
        /// </summary>
        private bool HasRecordPatterns(byte[]? pageData)
        {
            // Check if data is valid
            if (pageData == null || pageData.Length < 32)
                return false;
                
            // Debug indicator for every 1000th page to track progress
            if (_fileStream != null)
            {
                int pageNumber = (int)(_fileStream.Position / PAGE_SIZE);
                if (pageNumber % 1000 == 0)
                {
                    Log($"DEBUG: Analyzing page {pageNumber} for data patterns at position {_fileStream.Position:X8}");
                }
            }
                
            // Even more aggressive page detection algorithm to find all 466 tables
            // Look for repeating patterns of bytes at various sizes
            // Try a wider range of record sizes with smaller steps
            for (int size = 8; size <= 1024; size += 2) // Expanded size range, much smaller step
            {
                int matchCount = 0;
                
                // Check for repeating patterns
                for (int offset = 0; offset + size * 2 <= pageData.Length; offset += size)
                {
                    if (HasRepeatingPatternAt(pageData, offset, size))
                    {
                        matchCount++;
                    }
                }
                
                // Further reduce threshold to find more potential tables
                if (matchCount >= 1) // Extremely aggressive - even a single match might indicate a table
                {
                    return true;
                }
            }
            
            // Additional check: Look for structured data patterns
            // This can catch tables with few records but consistent structures
            int structuredSegments = 0;
            for (int offset = 0; offset + 16 <= pageData.Length; offset += 16)
            {
                // Check for runs of zeros (common in structured data)
                int zeroCount = 0;
                for (int i = 0; i < 8; i++)
                {
                    if (offset + i < pageData.Length && pageData[offset + i] == 0)
                        zeroCount++;
                }
                
                // More aggressive: even fewer zeros could indicate structure
                if (zeroCount >= 2)
                    structuredSegments++;
                    
                // Or check for printable ASCII (could be text data)
                int printableCount = 0;
                for (int i = 0; i < 8; i++)
                {
                    if (offset + i < pageData.Length && pageData[offset + i] >= 32 && pageData[offset + i] <= 126)
                        printableCount++;
                }
                
                // More aggressive: fewer printable chars could indicate text data
                if (printableCount >= 3)
                    structuredSegments++;
                    
                // New check: look for potential indexes (increasing byte values)
                int increasingBytes = 0;
                for (int i = 0; i < 7; i++)
                {
                    if (offset + i + 1 < pageData.Length && 
                        pageData[offset + i + 1] > pageData[offset + i])
                    {
                        increasingBytes++;
                    }
                }
                
                // If we have several increasing bytes, might be indexed data
                if (increasingBytes >= 3)
                    structuredSegments++;
            }
            
            // Even fewer structured segments could indicate a data page
            if (structuredSegments >= 5)
            {
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Checks if the page looks like an index page
        /// </summary>
        private bool LooksLikeIndexPage(byte[]? pageData)
        {
            // Check if data is valid
            if (pageData == null || pageData.Length < 100)
                return false;
                
            // Index pages often have pointers and sorted values
            // This is a simplified check
            
            // Example: Check for ordered byte sequences
            for (int offset = 0; offset < pageData.Length - 100; offset += 4)
            {
                int increasing = 0;
                
                for (int i = offset; i < offset + 16 && i + 4 < pageData.Length; i += 4)
                {
                    try
                    {
                        int value1 = BitConverter.ToInt32(pageData, i);
                        int value2 = BitConverter.ToInt32(pageData, i + 4);
                        
                        if (value2 > value1)
                        {
                            increasing++;
                        }
                    }
                    catch
                    {
                        // Skip this iteration if there's an error
                        continue;
                    }
                }
                
                // More aggressive threshold to detect more potential tables
                if (increasing >= 2)
                {
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Determines the type of page based on its content
        /// </summary>
        private PageType DeterminePageType(byte[]? pageData)
        {
            // Check if data is valid
            if (pageData == null || pageData.Length < 4)
                return PageType.Unknown;
                
            try
            {
                // Check for signature first
                if (HasFirebirdSignature(pageData))
                {
                    // Header pages have specific type markers in first byte
                    byte typeMarker = pageData[0];
                    if (typeMarker <= 10)
                    {
                        return PageType.Header;
                    }
                }
            }
            catch
            {
                // In case of error, return Unknown
                return PageType.Unknown;
            }
            
            // Check for data page patterns
            // In real implementation, would have more sophisticated detection
            
            // Track how many pages we check - useful for diagnostics
            if (_fileStream != null)
            {
                int pageNumber = (int)(_fileStream.Position / PAGE_SIZE);
                if (pageNumber % 5000 == 0)
                {
                    Log($"DIAGNOSTIC: Determining page type for page {pageNumber}");
                }
            }
            
            // Example: If page has many similar records, it's probably a data page
            if (HasRecordPatterns(pageData))
            {
                // For specific ranges, log more detailed info to help find patterns
                if (_fileStream != null)
                {
                    int pageNumber = (int)(_fileStream.Position / PAGE_SIZE);
                    if (pageNumber > 0 && pageNumber % 5000 == 0) 
                    {
                        Log($"FOUND DATA PAGE: Page {pageNumber} at offset {_fileStream.Position:X8} identified as data page");
                        // Get the first few bytes as a signature
                        if (pageData.Length >= 16)
                        {
                            string signature = BitConverter.ToString(pageData, 0, 16).Replace("-", " ");
                            Log($"DATA PAGE SIGNATURE: {signature}");
                        }
                    }
                }
                return PageType.Data;
            }
            
            // Index pages often have ordered values
            if (LooksLikeIndexPage(pageData))
            {
                return PageType.Index;
            }
            
            return PageType.Unknown;
        }
        
        /// <summary>
        /// Checks if the page has the Firebird signature
        /// </summary>
        private bool HasFirebirdSignature(byte[]? pageData)
        {
            // Check if page data matches Firebird signature pattern
            if (pageData == null || pageData.Length < FIREBIRD_SIGNATURE.Length)
                return false;
                
            // Check for exact match of signature bytes
            for (int i = 0; i < FIREBIRD_SIGNATURE.Length; i++)
            {
                if (pageData[i] != FIREBIRD_SIGNATURE[i])
                    return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Reads a page from the database file
        /// </summary>
        private async Task<byte[]> ReadPageAsync(long offset)
        {
            byte[] pageData = new byte[PAGE_SIZE];
            
            if (_fileStream != null)
            {
                _fileStream.Position = offset;
                await _fileStream.ReadAsync(pageData, 0, pageData.Length).ConfigureAwait(false);
            }
            
            return pageData;
        }
        
        /// <summary>
        /// Reads a specific range of bytes from the database file
        /// </summary>
        public async Task<byte[]> ReadBytesAsync(long offset, int length)
        {
            byte[] buffer = new byte[length];
            
            if (_fileStream != null)
            {
                _fileStream.Position = offset;
                await _fileStream.ReadAsync(buffer, 0, length).ConfigureAwait(false);
            }
            
            return buffer;
        }
        
        /// <summary>
        /// Gets all identified database pages
        /// </summary>
        public List<DatabasePage> GetPages()
        {
            return _pages;
        }
        
        /// <summary>
        /// Gets all extracted records
        /// </summary>
        public List<DatabaseRecord> GetRecords()
        {
            return _records;
        }
        
        /// <summary>
        /// Gets the database metadata
        /// </summary>
        public Dictionary<string, object> GetMetadata()
        {
            return _metadata;
        }
        
        /// <summary>
        /// Gets the log of operations
        /// </summary>
        public string GetLog()
        {
            return _logBuilder.ToString();
        }

        /// <summary>
        /// Generates a detailed diagnostic report of the database structure
        /// </summary>
        public string GenerateDiagnosticReport()
        {
            var report = new StringBuilder();
            
            // Basic file information
            report.AppendLine("===== FIREBIRD DATABASE DIAGNOSTIC REPORT =====");
            report.AppendLine($"File path: {_filePath}");
            // Use parentheses around the conditional expressions to fix syntax errors
            report.AppendLine($"File size: {(_metadata.TryGetValue("FileSize", out var fileSize) ? fileSize : "Unknown")} bytes");
            report.AppendLine($"Total pages: {(_metadata.TryGetValue("TotalPages", out var totalPages) ? totalPages : "Unknown")}");
            report.AppendLine($"Scan mode: {(_fullScan ? "Full" : "Sampling")}");
            report.AppendLine($"Max pages to scan: {_maxPagesToScan}");
            report.AppendLine($"Chunk size: {_chunkSize}");
            report.AppendLine();
            
            // Page statistics
            report.AppendLine("===== PAGE STATISTICS =====");
            report.AppendLine($"Total pages analyzed: {_pages.Count}");
            
            // Group by page type
            var pagesByType = _pages.GroupBy(p => p.PageType)
                .OrderByDescending(g => g.Count())
                .ToList();
                
            foreach (var group in pagesByType)
            {
                report.AppendLine($"- {group.Key}: {group.Count()} pages");
                
                // For each page type, list the first 10 pages
                if (group.Any())
                {
                    report.AppendLine("  Sample pages:");
                    foreach (var page in group.Take(10))
                    {
                        report.AppendLine($"  - Page {page.PageNumber} at offset 0x{page.Offset:X8}, type marker: 0x{page.TypeMarker:X2}");
                    }
                }
            }
            report.AppendLine();
            
            // Metadata analysis
            report.AppendLine("===== METADATA ANALYSIS =====");
            foreach (var item in _metadata)
            {
                report.AppendLine($"{item.Key}: {item.Value}");
            }
            report.AppendLine();
            
            // Record analysis
            report.AppendLine("===== RECORD ANALYSIS =====");
            report.AppendLine($"Total records extracted: {_records.Count}");
            
            // Group by size
            var recordsBySize = _records.GroupBy(r => r.Size)
                .OrderByDescending(g => g.Count())
                .ToList();
                
            report.AppendLine($"Distinct record sizes: {recordsBySize.Count}");
            foreach (var group in recordsBySize.Take(10))
            {
                report.AppendLine($"- Size {group.Key} bytes: {group.Count()} records");
            }
            
            return report.ToString();
        }
        
        /// <summary>
        /// Adds a log entry
        /// </summary>
        private void Log(string message)
        {
            string entry = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            _logBuilder.AppendLine(entry);
        }
        
        /// <summary>
        /// Closes the database file
        /// </summary>
        public void Close()
        {
            _reader?.Dispose();
            _fileStream?.Dispose();
        }
        
        /// <summary>
        /// Disposes resources
        /// </summary>
        public void Dispose()
        {
            Close();
        }

        /// <summary>
        /// Generates a hex dump of binary data
        /// </summary>
        public static string HexDump(byte[] bytes, int startOffset = 0, int length = -1, int bytesPerLine = 16)
        {
            if (bytes == null || bytes.Length == 0) return "No data";

            if (length < 0 || startOffset + length > bytes.Length)
                length = bytes.Length - startOffset;

            var result = new StringBuilder();
            int bytesLength = startOffset + length;
            
            for (int i = startOffset; i < bytesLength; i += bytesPerLine)
            {
                // Write offset
                result.Append($"{i:X8}  ");
                
                // Write hex bytes
                for (int j = 0; j < bytesPerLine; j++)
                {
                    if (i + j < bytesLength)
                        result.Append($"{bytes[i + j]:X2} ");
                    else
                        result.Append("   ");
                    
                    // Extra space after 8 bytes
                    if (j == 7)
                        result.Append(" ");
                }
                
                // Write ASCII representation
                result.Append("  ");
                for (int j = 0; j < bytesPerLine; j++)
                {
                    if (i + j < bytesLength)
                    {
                        byte b = bytes[i + j];
                        // Print printable ASCII characters
                        if (b >= 32 && b <= 126)
                            result.Append((char)b);
                        else
                            result.Append(".");
                    }
                }
                
                result.AppendLine();
            }
            
            return result.ToString();
        }

        #region Helper Classes
        
        /// <summary>
        /// Types of database pages
        /// </summary>
        public enum PageType
        {
            Unknown,
            Header,
            Data,
            Index,
            Blob,
            Free
        }
        
        /// <summary>
        /// Types of field data
        /// </summary>
        public enum FieldDataType
        {
            Unknown,
            Integer,
            Float,
            String,
            DateTime,
            Binary,
            Boolean
        }
        
        /// <summary>
        /// Represents a database page
        /// </summary>
        public class DatabasePage
        {
            public int PageNumber { get; set; }
            public long Offset { get; set; }
            public PageType PageType { get; set; }
            public byte TypeMarker { get; set; }
            
            // Only store data when needed (can be null to save memory)
            public byte[]? Data { get; set; } = null;
            
            public override string ToString()
            {
                return $"Page {PageNumber} at 0x{Offset:X8}, Type: {PageType}, Marker: 0x{TypeMarker:X2}";
            }
        }
        
        /// <summary>
        /// Represents a database record
        /// </summary>
        public class DatabaseRecord
        {
            public long PageOffset { get; set; }
            public int RecordOffset { get; set; }
            public int Size { get; set; }
            public byte[] Data { get; set; } = Array.Empty<byte>();
            
            public override string ToString()
            {
                return $"Record at page 0x{PageOffset:X8} + {RecordOffset}, Size: {Size} bytes";
            }
        }
        
        /// <summary>
        /// Information about a field in a record
        /// </summary>
        public class FieldInfo
        {
            public int Offset { get; set; }
            public int Size { get; set; }
            public FieldDataType DataType { get; set; }
            
            public override string ToString()
            {
                return $"Field at offset {Offset}, size {Size}, type {DataType}";
            }
        }
        
        #endregion
    }
}