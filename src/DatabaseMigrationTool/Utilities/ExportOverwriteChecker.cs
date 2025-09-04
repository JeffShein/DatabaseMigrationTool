using System.IO;
using DatabaseMigrationTool.Models;

namespace DatabaseMigrationTool.Utilities
{
    public static class ExportOverwriteChecker
    {
        
        /// <summary>
        /// Check for existing export files that would be overwritten by the specified tables
        /// </summary>
        /// <param name="outputDirectory">Export output directory</param>
        /// <param name="tablesToExport">List of tables that will be exported (null means all tables)</param>
        /// <param name="existingTables">List of tables from existing metadata (if available)</param>
        /// <returns>Result indicating which specific files would be overwritten</returns>
        public static async Task<ExportOverwriteResult> CheckForTableSpecificOverwriteAsync(string outputDirectory, List<string>? tablesToExport, List<TableSchema>? existingTables = null)
        {
            if (string.IsNullOrEmpty(outputDirectory) || !Directory.Exists(outputDirectory))
            {
                return new ExportOverwriteResult { HasExistingExport = false };
            }

            var existingFiles = new List<string>();
            var conflictingTables = new List<string>();
            
            // Check if this is a valid export
            if (MetadataManager.IsValidExport(outputDirectory))
            {
                // Get conflicting tables
                var conflicts = await MetadataManager.GetConflictingTablesAsync(outputDirectory, tablesToExport ?? new List<string>());
                conflictingTables.AddRange(conflicts);
                
                // Note: We don't warn about export_manifest.json and dependencies.json 
                // because they are updated/merged, not overwritten
                
                // Add specific table metadata files that would be overwritten
                var metadataDir = Path.Combine(outputDirectory, "table_metadata");
                if (Directory.Exists(metadataDir) && tablesToExport != null)
                {
                    foreach (var tableSpec in tablesToExport)
                    {
                        var (schema, tableName) = ParseTableSpec(tableSpec);
                        var metadataFile = $"{schema}_{tableName}.meta";
                        var metadataFilePath = Path.Combine(metadataDir, metadataFile);
                        
                        if (File.Exists(metadataFilePath))
                        {
                            existingFiles.Add($"table_metadata/{metadataFile}");
                        }
                    }
                }
            }
            else
            {
                // Not a valid export - no conflicts to check
                return new ExportOverwriteResult { HasExistingExport = false };
            }
            
            // Check for data directory and specific table files
            string dataDir = Path.Combine(outputDirectory, "data");
            if (Directory.Exists(dataDir))
            {
                // If no specific tables specified, check all existing files
                if (tablesToExport == null || tablesToExport.Count == 0)
                {
                    var dataFiles = Directory.GetFiles(dataDir, "*.bin");
                    var infoFiles = Directory.GetFiles(dataDir, "*.info");
                    var errorFiles = Directory.GetFiles(dataDir, "*.error");
                    
                    existingFiles.AddRange(dataFiles.Select(f => Path.GetFileName(f)));
                    existingFiles.AddRange(infoFiles.Select(f => Path.GetFileName(f)));
                    existingFiles.AddRange(errorFiles.Select(f => Path.GetFileName(f)));
                }
                else
                {
                    // Check only for files that match the tables being exported
                    foreach (string tableSpec in tablesToExport)
                    {
                        // Parse table specification (could be "schema.table" or just "table")
                        string schema = "dbo"; // default schema
                        string tableName = tableSpec;
                        
                        if (tableSpec.Contains('.'))
                        {
                            var parts = tableSpec.Split('.');
                            if (parts.Length == 2)
                            {
                                schema = parts[0];
                                tableName = parts[1];
                            }
                        }
                        
                        // Check for various file patterns that could exist for this table
                        string tableFilePattern = $"{schema}_{tableName}";
                        
                        // Single file pattern
                        string singleFile = $"{tableFilePattern}.bin";
                        string singleFilePath = Path.Combine(dataDir, singleFile);
                        if (File.Exists(singleFilePath))
                        {
                            existingFiles.Add(singleFile);
                            conflictingTables.Add(tableSpec);
                        }
                        
                        // Batch file pattern
                        var batchFiles = Directory.GetFiles(dataDir, $"{tableFilePattern}_batch*.bin");
                        if (batchFiles.Length > 0)
                        {
                            existingFiles.AddRange(batchFiles.Select(f => Path.GetFileName(f)));
                            if (!conflictingTables.Contains(tableSpec))
                            {
                                conflictingTables.Add(tableSpec);
                            }
                        }
                        
                        // Info and error files
                        string infoFile = $"{tableFilePattern}.info";
                        string infoFilePath = Path.Combine(dataDir, infoFile);
                        if (File.Exists(infoFilePath))
                        {
                            existingFiles.Add(infoFile);
                        }
                        
                        string errorFile = $"{tableFilePattern}.error";
                        string errorFilePath = Path.Combine(dataDir, errorFile);
                        if (File.Exists(errorFilePath))
                        {
                            existingFiles.Add(errorFile);
                        }
                    }
                }
            }
            
            // Note: We don't check for log files (export_log.txt, export_skipped_tables.txt) 
            // as these are just logging files and it's acceptable to overwrite them
            
            return new ExportOverwriteResult
            {
                HasExistingExport = existingFiles.Count > 0,
                ExistingFiles = existingFiles,
                ConflictingTables = conflictingTables
            };
        }
        
        /// <summary>
        /// Synchronous wrapper for table-specific overwrite check (for backward compatibility)
        /// </summary>
        public static ExportOverwriteResult CheckForTableSpecificOverwrite(string outputDirectory, List<string>? tablesToExport, List<TableSchema>? existingTables = null)
        {
            return CheckForTableSpecificOverwriteAsync(outputDirectory, tablesToExport, existingTables).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Parse table specification into schema and table name
        /// </summary>
        private static (string schema, string tableName) ParseTableSpec(string tableSpec)
        {
            if (tableSpec.Contains('.'))
            {
                var parts = tableSpec.Split('.');
                return parts.Length == 2 ? (parts[0], parts[1]) : ("dbo", tableSpec);
            }
            return ("dbo", tableSpec);
        }

        /// <summary>
        /// Deletes only the files for specific tables being overwritten
        /// </summary>
        public static void DeleteConflictingTables(string outputDirectory, List<string> tablesToOverwrite)
        {
            if (string.IsNullOrEmpty(outputDirectory) || !Directory.Exists(outputDirectory) || tablesToOverwrite == null || tablesToOverwrite.Count == 0)
            {
                return;
            }
            
            try
            {
                var dataDir = Path.Combine(outputDirectory, "data");
                var metadataDir = Path.Combine(outputDirectory, "table_metadata");
                
                foreach (var tableSpec in tablesToOverwrite)
                {
                    var (schema, tableName) = ParseTableSpec(tableSpec);
                    
                    // Delete table metadata file
                    if (Directory.Exists(metadataDir))
                    {
                        var metadataFile = Path.Combine(metadataDir, $"{schema}_{tableName}.meta");
                        if (File.Exists(metadataFile))
                        {
                            File.Delete(metadataFile);
                        }
                    }
                    
                    // Delete table data files
                    if (Directory.Exists(dataDir))
                    {
                        var tableFilePattern = $"{schema}_{tableName}";
                        
                        // Single data file
                        var singleFile = Path.Combine(dataDir, $"{tableFilePattern}.bin");
                        if (File.Exists(singleFile))
                        {
                            File.Delete(singleFile);
                        }
                        
                        // Batch data files
                        var batchFiles = Directory.GetFiles(dataDir, $"{tableFilePattern}_batch*.bin");
                        foreach (var batchFile in batchFiles)
                        {
                            File.Delete(batchFile);
                        }
                        
                        // Info and error files
                        var infoFile = Path.Combine(dataDir, $"{tableFilePattern}.info");
                        if (File.Exists(infoFile))
                        {
                            File.Delete(infoFile);
                        }
                        
                        var errorFile = Path.Combine(dataDir, $"{tableFilePattern}.error");
                        if (File.Exists(errorFile))
                        {
                            File.Delete(errorFile);
                        }
                    }
                }
                
                // Note: We don't delete manifest.json or dependencies.json as they get updated, not overwritten
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to delete conflicting table files: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Deletes entire export directory (for full export overwrites)
        /// </summary>
        public static void DeleteExistingExport(string outputDirectory)
        {
            if (string.IsNullOrEmpty(outputDirectory) || !Directory.Exists(outputDirectory))
            {
                return;
            }
            
            try
            {
                // Delete metadata files
                string manifestPath = Path.Combine(outputDirectory, "export_manifest.json");
                string dependenciesPath = Path.Combine(outputDirectory, "dependencies.json");
                string tableMetadataDir = Path.Combine(outputDirectory, "table_metadata");
                
                if (File.Exists(manifestPath))
                {
                    File.Delete(manifestPath);
                }
                
                if (File.Exists(dependenciesPath))
                {
                    File.Delete(dependenciesPath);
                }
                
                if (Directory.Exists(tableMetadataDir))
                {
                    Directory.Delete(tableMetadataDir, recursive: true);
                }
                
                // Delete data directory and all its contents
                string dataDir = Path.Combine(outputDirectory, "data");
                if (Directory.Exists(dataDir))
                {
                    Directory.Delete(dataDir, recursive: true);
                }
                
                // Delete log files
                string logFile = Path.Combine(outputDirectory, "export_log.txt");
                string skippedTablesFile = Path.Combine(outputDirectory, "export_skipped_tables.txt");
                
                if (File.Exists(logFile))
                {
                    File.Delete(logFile);
                }
                
                if (File.Exists(skippedTablesFile))
                {
                    File.Delete(skippedTablesFile);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to delete existing export files: {ex.Message}", ex);
            }
        }
    }
    
    public class ExportOverwriteResult
    {
        public bool HasExistingExport { get; set; }
        public List<string> ExistingFiles { get; set; } = new List<string>();
        public List<string> ConflictingTables { get; set; } = new List<string>();
        
        public string GetSummaryText()
        {
            if (!HasExistingExport)
            {
                return "No existing export found.";
            }
            
            // If we have specific conflicting tables, show that information
            if (ConflictingTables.Count > 0)
            {
                int tableCount = ConflictingTables.Count;
                int fileCount = ExistingFiles.Count;
                
                if (tableCount == 1)
                {
                    return $"The table '{ConflictingTables[0]}' already exists in this export ({fileCount} file{(fileCount > 1 ? "s" : "")} would be overwritten).";
                }
                else if (tableCount <= 3)
                {
                    return $"{tableCount} tables already exist in this export: {string.Join(", ", ConflictingTables)} ({fileCount} file{(fileCount > 1 ? "s" : "")} would be overwritten).";
                }
                else
                {
                    var sampleTables = ConflictingTables.Take(3).ToList();
                    return $"{tableCount} tables already exist in this export including: {string.Join(", ", sampleTables)} and {tableCount - 3} more ({fileCount} file{(fileCount > 1 ? "s" : "")} would be overwritten).";
                }
            }
            
            // Fallback to original file-based message
            int totalFiles = ExistingFiles.Count;
            if (totalFiles == 0)
            {
                return "Export directory exists but appears empty.";
            }
            
            if (totalFiles == 1)
            {
                return $"Found 1 existing export file: {ExistingFiles[0]}";
            }
            
            var sampleFiles = ExistingFiles.Take(3).ToList();
            if (totalFiles <= 3)
            {
                return $"Found {totalFiles} existing export files: {string.Join(", ", sampleFiles)}";
            }
            
            return $"Found {totalFiles} existing export files including: {string.Join(", ", sampleFiles)} and {totalFiles - 3} more...";
        }
    }
}