using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DatabaseMigrationTool.Models;
using MessagePack;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;

namespace DatabaseMigrationTool.Utilities
{
    /// <summary>
    /// Manages export metadata with manifest, individual table metadata, and dependencies
    /// </summary>
    public static class MetadataManager
    {
        private const string MANIFEST_FILE = "export_manifest.json";
        private const string DEPENDENCIES_FILE = "dependencies.json";
        private const string METADATA_DIR = "table_metadata";

        /// <summary>
        /// Writes table metadata to export directory
        /// </summary>
        public static async Task WriteMetadataAsync(string exportPath, List<TableSchema> tables, string databaseName, bool schemaOnly = false)
        {
            Directory.CreateDirectory(exportPath);
            var metadataDir = Path.Combine(exportPath, METADATA_DIR);
            Directory.CreateDirectory(metadataDir);

            var exportDate = DateTime.UtcNow.ToString("o");
            var manifest = await LoadOrCreateManifestAsync(exportPath, databaseName);
            var dependencyManifest = await LoadOrCreateDependencyManifestAsync(exportPath);

            // Write individual table metadata files
            foreach (var table in tables)
            {
                WriteTableMetadata(metadataDir, table, databaseName, exportDate, schemaOnly);
                
                // Update or add table in manifest
                var existingEntry = manifest.Tables.FirstOrDefault(t => 
                    t.TableName.Equals(table.Name, StringComparison.OrdinalIgnoreCase) && 
                    t.Schema.Equals(table.Schema ?? "dbo", StringComparison.OrdinalIgnoreCase));
                
                if (existingEntry != null)
                {
                    // Update existing entry
                    existingEntry.ExportDate = exportDate;
                    existingEntry.SchemaOnly = schemaOnly;
                    existingEntry.MetadataFile = GetTableMetadataFileName(table);
                }
                else
                {
                    // Add new entry
                    manifest.Tables.Add(new TableManifestEntry
                    {
                        TableName = table.Name,
                        Schema = table.Schema ?? "dbo",
                        MetadataFile = GetTableMetadataFileName(table),
                        ExportDate = exportDate,
                        SchemaOnly = schemaOnly,
                        HasData = !schemaOnly
                    });
                }

                // Add foreign key dependencies
                AddTableDependencies(dependencyManifest, table);
            }

            // Calculate and update dependency order
            UpdateDependencyOrder(dependencyManifest, manifest.Tables.Select(t => $"{t.Schema}.{t.TableName}").ToList());

            // Write manifest and dependencies
            await WriteManifestAsync(exportPath, manifest);
            await WriteDependencyManifestAsync(exportPath, dependencyManifest);
        }

        /// <summary>
        /// Reads metadata from export directory
        /// </summary>
        public static async Task<DatabaseExport> ReadMetadataAsync(string exportPath)
        {
            var manifestPath = Path.Combine(exportPath, MANIFEST_FILE);
            
            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException($"Export manifest not found: {manifestPath}. This directory does not contain a valid export.");
            }

            return await ReadFromExportAsync(exportPath);
        }

        /// <summary>
        /// Checks if directory contains a valid export
        /// </summary>
        public static bool IsValidExport(string exportPath)
        {
            return File.Exists(Path.Combine(exportPath, MANIFEST_FILE));
        }

        /// <summary>
        /// Gets list of tables that would be affected by exporting specific tables
        /// </summary>
        public static async Task<List<string>> GetConflictingTablesAsync(string exportPath, List<string> tablesToExport)
        {
            var conflicts = new List<string>();
            
            if (!IsValidExport(exportPath))
            {
                return conflicts; // No conflicts if not a valid export
            }

            var manifest = await LoadManifestAsync(exportPath);
            if (manifest == null) return conflicts;

            var metadataDir = Path.Combine(exportPath, METADATA_DIR);
            if (!Directory.Exists(metadataDir)) return conflicts;

            foreach (var tableSpec in tablesToExport)
            {
                var (schema, tableName) = ParseTableSpec(tableSpec);
                
                var existingEntry = manifest.Tables.FirstOrDefault(t => 
                    t.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase) && 
                    t.Schema.Equals(schema, StringComparison.OrdinalIgnoreCase));
                
                if (existingEntry != null)
                {
                    var metadataFile = Path.Combine(metadataDir, existingEntry.MetadataFile);
                    if (File.Exists(metadataFile))
                    {
                        conflicts.Add(tableSpec);
                    }
                }
            }

            return conflicts;
        }

        #region Private Helper Methods

        private static async Task<ExportManifest> LoadOrCreateManifestAsync(string exportPath, string databaseName)
        {
            var manifest = await LoadManifestAsync(exportPath);
            if (manifest != null) return manifest;

            return new ExportManifest
            {
                DatabaseName = databaseName,
                ExportDate = DateTime.UtcNow.ToString("o"),
                FormatVersion = "2.0"
            };
        }

        private static async Task<ExportManifest?> LoadManifestAsync(string exportPath)
        {
            var manifestPath = Path.Combine(exportPath, MANIFEST_FILE);
            if (!File.Exists(manifestPath)) return null;

            try
            {
                var json = await File.ReadAllTextAsync(manifestPath);
                return JsonSerializer.Deserialize<ExportManifest>(json);
            }
            catch
            {
                return null;
            }
        }

        private static async Task WriteManifestAsync(string exportPath, ExportManifest manifest)
        {
            var manifestPath = Path.Combine(exportPath, MANIFEST_FILE);
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(manifest, options);
            await File.WriteAllTextAsync(manifestPath, json);
        }

        private static async Task<DependencyManifest> LoadOrCreateDependencyManifestAsync(string exportPath)
        {
            var dependenciesPath = Path.Combine(exportPath, DEPENDENCIES_FILE);
            if (File.Exists(dependenciesPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(dependenciesPath);
                    var existing = JsonSerializer.Deserialize<DependencyManifest>(json);
                    if (existing != null) return existing;
                }
                catch
                {
                    // Fall through to create new
                }
            }

            return new DependencyManifest
            {
                CreatedDate = DateTime.UtcNow.ToString("o")
            };
        }

        private static async Task WriteDependencyManifestAsync(string exportPath, DependencyManifest dependencies)
        {
            var dependenciesPath = Path.Combine(exportPath, DEPENDENCIES_FILE);
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(dependencies, options);
            await File.WriteAllTextAsync(dependenciesPath, json);
        }

        private static void WriteTableMetadata(string metadataDir, TableSchema table, string databaseName, string exportDate, bool schemaOnly)
        {
            var tableMetadata = new TableMetadata
            {
                Schema = table,
                ExportDate = exportDate,
                SourceDatabase = databaseName,
                SchemaOnly = schemaOnly
            };

            var fileName = GetTableMetadataFileName(table);
            var filePath = Path.Combine(metadataDir, fileName);

            using (var fs = File.Create(filePath))
            using (var compressor = new BZip2Stream(fs, CompressionMode.Compress, true))
            {
                MessagePackSerializer.Serialize(compressor, tableMetadata);
            }
        }

        private static string GetTableMetadataFileName(TableSchema table)
        {
            var schema = table.Schema ?? "dbo";
            return $"{schema}_{table.Name}.meta";
        }

        private static void AddTableDependencies(DependencyManifest dependencies, TableSchema table)
        {
            foreach (var fk in table.ForeignKeys)
            {
                var referencedTable = string.IsNullOrEmpty(fk.ReferencedTableSchema) 
                    ? fk.ReferencedTableName 
                    : $"{fk.ReferencedTableSchema}.{fk.ReferencedTableName}";
                    
                var dependency = new ForeignKeyDependency
                {
                    ConstraintName = fk.Name,
                    SourceTable = table.FullName,
                    TargetTable = referencedTable,
                    SourceColumns = fk.Columns,
                    TargetColumns = fk.ReferencedColumns,
                    OnDelete = fk.DeleteRule,
                    OnUpdate = fk.UpdateRule
                };

                // Update or add dependency
                var existing = dependencies.CrossTableForeignKeys.FirstOrDefault(d => 
                    d.ConstraintName.Equals(fk.Name, StringComparison.OrdinalIgnoreCase));
                
                if (existing != null)
                {
                    dependencies.CrossTableForeignKeys.Remove(existing);
                }
                
                dependencies.CrossTableForeignKeys.Add(dependency);
            }
        }

        private static void UpdateDependencyOrder(DependencyManifest dependencies, List<string> allTables)
        {
            // Simple topological sort based on foreign key dependencies
            var dependsOn = new Dictionary<string, List<string>>();
            
            foreach (var table in allTables)
            {
                dependsOn[table] = new List<string>();
            }

            foreach (var fk in dependencies.CrossTableForeignKeys)
            {
                if (dependsOn.ContainsKey(fk.SourceTable) && allTables.Contains(fk.TargetTable))
                {
                    dependsOn[fk.SourceTable].Add(fk.TargetTable);
                }
            }

            // Calculate import order (reverse of dependency order)
            var order = TopologicalSort(dependsOn);
            dependencies.DependencyOrder.Clear();
            
            for (int i = 0; i < order.Count; i++)
            {
                var level = (i + 1).ToString();
                if (!dependencies.DependencyOrder.ContainsKey(level))
                {
                    dependencies.DependencyOrder[level] = new List<string>();
                }
                dependencies.DependencyOrder[level].Add(order[i]);
            }
        }

        private static List<string> TopologicalSort(Dictionary<string, List<string>> dependsOn)
        {
            var result = new List<string>();
            var visited = new HashSet<string>();
            var visiting = new HashSet<string>();

            foreach (var table in dependsOn.Keys)
            {
                Visit(table, dependsOn, visited, visiting, result);
            }

            return result;
        }

        private static void Visit(string table, Dictionary<string, List<string>> dependsOn, HashSet<string> visited, HashSet<string> visiting, List<string> result)
        {
            if (visited.Contains(table)) return;
            if (visiting.Contains(table)) return; // Circular dependency - skip

            visiting.Add(table);

            foreach (var dependency in dependsOn[table])
            {
                Visit(dependency, dependsOn, visited, visiting, result);
            }

            visiting.Remove(table);
            visited.Add(table);
            result.Add(table);
        }


        private static async Task<DatabaseExport> ReadFromExportAsync(string exportPath)
        {
            var manifest = await LoadManifestAsync(exportPath);
            if (manifest == null) throw new InvalidOperationException("Invalid or missing export manifest");

            var dependencies = await LoadOrCreateDependencyManifestAsync(exportPath);
            var metadataDir = Path.Combine(exportPath, METADATA_DIR);
            var schemas = new List<TableSchema>();

            foreach (var entry in manifest.Tables)
            {
                var metadataFile = Path.Combine(metadataDir, entry.MetadataFile);
                if (File.Exists(metadataFile))
                {
                    try
                    {
                        using (var fs = File.OpenRead(metadataFile))
                        using (var decompressor = new BZip2Stream(fs, CompressionMode.Decompress, true))
                        {
                            var tableMetadata = MessagePackSerializer.Deserialize<TableMetadata>(decompressor);
                            schemas.Add(tableMetadata.Schema);
                        }
                    }
                    catch
                    {
                        // Skip corrupted metadata files
                    }
                }
            }

            return new DatabaseExport
            {
                DatabaseName = manifest.DatabaseName,
                Schemas = schemas,
                ExportDate = manifest.ExportDate,
                DependencyOrder = dependencies.DependencyOrder,
                FormatVersion = manifest.FormatVersion
            };
        }


        private static (string schema, string tableName) ParseTableSpec(string tableSpec)
        {
            if (tableSpec.Contains('.'))
            {
                var parts = tableSpec.Split('.');
                return parts.Length == 2 ? (parts[0], parts[1]) : ("dbo", tableSpec);
            }
            return ("dbo", tableSpec);
        }

        #endregion
    }
}