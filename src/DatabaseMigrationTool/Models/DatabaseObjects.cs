using System.Collections.Generic;
using MessagePack;

namespace DatabaseMigrationTool.Models
{
    [MessagePackObject]
    public class TableSchema
    {
        [Key(0)]
        public string Name { get; set; } = string.Empty;
        
        [Key(1)]
        public string? Schema { get; set; }
        
        [Key(2)]
        public List<ColumnDefinition> Columns { get; set; } = new();
        
        [Key(3)]
        public List<IndexDefinition> Indexes { get; set; } = new();
        
        [Key(4)]
        public List<ForeignKeyDefinition> ForeignKeys { get; set; } = new();
        
        [Key(5)]
        public List<ConstraintDefinition> Constraints { get; set; } = new();

        [IgnoreMember]
        public string FullName => !string.IsNullOrEmpty(Schema) ? $"{Schema}.{Name}" : Name ?? string.Empty;

        [Key(6)]
        public Dictionary<string, string> AdditionalProperties { get; set; } = new();
    }

    [MessagePackObject]
    public class ColumnDefinition
    {
        [Key(0)]
        public string Name { get; set; } = string.Empty;
        
        [Key(1)]
        public string DataType { get; set; } = string.Empty;
        
        [Key(2)]
        public bool IsNullable { get; set; }
        
        [Key(3)]
        public bool IsPrimaryKey { get; set; }
        
        [Key(4)]
        public bool IsIdentity { get; set; }
        
        [Key(5)]
        public string? DefaultValue { get; set; }
        
        [Key(6)]
        public int? MaxLength { get; set; }
        
        [Key(7)]
        public int? Precision { get; set; }
        
        [Key(8)]
        public int? Scale { get; set; }

        [Key(9)]
        public int OrdinalPosition { get; set; }

        [Key(10)]
        public Dictionary<string, string> AdditionalProperties { get; set; } = new();
    }

    [MessagePackObject]
    public class IndexDefinition
    {
        [Key(0)]
        public string Name { get; set; } = string.Empty;
        
        [Key(1)]
        public bool IsUnique { get; set; }
        
        [Key(2)]
        public List<string> Columns { get; set; } = new();
        
        [Key(3)]
        public bool IsClustered { get; set; }

        [Key(4)]
        public Dictionary<string, string> AdditionalProperties { get; set; } = new();
    }

    [MessagePackObject]
    public class ForeignKeyDefinition
    {
        [Key(0)]
        public string Name { get; set; } = string.Empty;
        
        [Key(1)]
        public string ReferencedTableSchema { get; set; } = string.Empty;
        
        [Key(2)]
        public string ReferencedTableName { get; set; } = string.Empty;
        
        [Key(3)]
        public List<string> Columns { get; set; } = new();
        
        [Key(4)]
        public List<string> ReferencedColumns { get; set; } = new();
        
        [Key(5)]
        public string UpdateRule { get; set; } = string.Empty;
        
        [Key(6)]
        public string DeleteRule { get; set; } = string.Empty;

        [Key(7)]
        public Dictionary<string, string> AdditionalProperties { get; set; } = new();
    }

    [MessagePackObject]
    public class ConstraintDefinition
    {
        [Key(0)]
        public string Name { get; set; } = string.Empty;
        
        [Key(1)]
        public string Type { get; set; } = string.Empty;
        
        [Key(2)]
        public string? Definition { get; set; }
        
        [Key(3)]
        public List<string> Columns { get; set; } = new();

        [Key(4)]
        public Dictionary<string, string> AdditionalProperties { get; set; } = new();
    }

    [MessagePackObject]
    public class TableData
    {
        [Key(0)]
        public TableSchema Schema { get; set; } = new();
        
        [Key(1)]
        public List<RowData> Rows { get; set; } = new();
        
        [Key(2)]
        public int TotalCount { get; set; }
        
        [Key(3)]
        public int BatchNumber { get; set; } = 0;
        
        [Key(4)]
        public int TotalBatches { get; set; } = 1;
        
        [Key(5)]
        public bool IsLastBatch { get; set; } = true;
    }

    [MessagePackObject]
    public class RowData
    {
        [Key(0)]
        public Dictionary<string, object?> Values { get; set; } = new();
    }

    [MessagePackObject]
    public class DatabaseExport
    {
        [Key(0)]
        public string DatabaseName { get; set; } = string.Empty;
        
        [Key(1)]
        public List<TableSchema> Schemas { get; set; } = new();
        
        [Key(2)]
        public Dictionary<string, List<string>> DependencyOrder { get; set; } = new();
        
        [Key(3)]
        public string ExportDate { get; set; } = string.Empty;
        
        [Key(4)]
        public string FormatVersion { get; set; } = "1.0";
        
        [Key(5)]
        public Dictionary<string, string> AdditionalProperties { get; set; } = new();
    }

    /// <summary>
    /// Export manifest - lightweight JSON file that tracks what's in the export
    /// </summary>
    public class ExportManifest
    {
        public string DatabaseName { get; set; } = string.Empty;
        public string ExportDate { get; set; } = string.Empty;
        public string FormatVersion { get; set; } = "2.0";
        public List<TableManifestEntry> Tables { get; set; } = new();
        public bool HasDependencies { get; set; } = true;
        public Dictionary<string, string> AdditionalProperties { get; set; } = new();
    }

    /// <summary>
    /// Individual table entry in the export manifest
    /// </summary>
    public class TableManifestEntry
    {
        public string TableName { get; set; } = string.Empty;
        public string Schema { get; set; } = "dbo";
        public string MetadataFile { get; set; } = string.Empty;
        public List<string> DataFiles { get; set; } = new();
        public string ExportDate { get; set; } = string.Empty;
        public long RowCount { get; set; } = 0;
        public bool HasData { get; set; } = true;
        public bool SchemaOnly { get; set; } = false;
    }

    /// <summary>
    /// Individual table metadata - stored as MessagePack binary file
    /// </summary>
    [MessagePackObject]
    public class TableMetadata
    {
        [Key(0)]
        public TableSchema Schema { get; set; } = new();
        
        [Key(1)]
        public string ExportDate { get; set; } = string.Empty;
        
        [Key(2)]
        public string FormatVersion { get; set; } = "2.0";
        
        [Key(3)]
        public long RowCount { get; set; } = 0;
        
        [Key(4)]
        public bool SchemaOnly { get; set; } = false;
        
        [Key(5)]
        public string SourceDatabase { get; set; } = string.Empty;
        
        [Key(6)]
        public Dictionary<string, string> AdditionalProperties { get; set; } = new();
    }

    /// <summary>
    /// Cross-table dependencies and import order
    /// </summary>
    public class DependencyManifest
    {
        public string FormatVersion { get; set; } = "2.0";
        public Dictionary<string, List<string>> DependencyOrder { get; set; } = new();
        public List<ForeignKeyDependency> CrossTableForeignKeys { get; set; } = new();
        public string CreatedDate { get; set; } = string.Empty;
    }

    /// <summary>
    /// Cross-table foreign key dependency information
    /// </summary>
    public class ForeignKeyDependency
    {
        public string ConstraintName { get; set; } = string.Empty;
        public string SourceTable { get; set; } = string.Empty;
        public string TargetTable { get; set; } = string.Empty;
        public List<string> SourceColumns { get; set; } = new();
        public List<string> TargetColumns { get; set; } = new();
        public string OnDelete { get; set; } = string.Empty;
        public string OnUpdate { get; set; } = string.Empty;
    }
}