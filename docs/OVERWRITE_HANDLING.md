# Import/Export Overwrite Handling

## Overview

The Database Migration Tool includes comprehensive overwrite detection and handling for both export and import operations. This ensures data safety and provides user control over potentially destructive operations.

## Export Overwrite Detection

### How It Works
- Checks for existing export files in the target directory
- Analyzes `export_manifest.json` to determine export contents
- Presents detailed information about what will be overwritten
- Allows user to choose whether to proceed

### UI Behavior
```
Export Overwrite Detected
-------------------------
The directory 'C:\exports\mydata' contains an existing export.

Existing Export Details:
- Database: MyDatabase
- Exported: 2023-01-15 14:30:22
- Tables: 15 tables, 125,340 total rows
- Size: 45.2 MB

Current Export Will:
- Replace all existing files
- Export: 15 tables (same tables)
- Estimated size: ~47.8 MB

Continue with export? [Yes] [No]
```

### Files Checked
- `export_manifest.json` - Main metadata file
- `*.bin` - Table data files
- `*_batch*.bin` - Batched table data files
- `*.log` - Export log files

## Import Overwrite Detection

### Schema-Aware Detection
The import overwrite checker analyzes both the source export and target database to provide accurate conflict detection:

```csharp
// Handles different schema naming conventions
if (provider.ProviderName.Equals("SqlServer"))
{
    // SQL Server always uses 'dbo' schema for operations
    countSql = $"SELECT COUNT(*) FROM [dbo].[{table.Name}] WITH (NOLOCK)";
}
else if (provider.ProviderName.Equals("Firebird"))
{
    // Firebird uses owner names as schemas
    countSql = $"SELECT COUNT(*) FROM \"{table.Name}\"";
}
```

### Conflict Types

#### Schema Conflicts
- **Table Already Exists**: Attempting to create a table that already exists
- **Schema Mismatch**: Different column definitions between export and target

#### Data Conflicts
- **Data Append**: Adding data to tables that already contain data
- **Data Overwrite**: Replacing data in existing tables

### UI Behavior
```
Import Conflicts Detected
-------------------------
The target database contains conflicting data:

Schema Conflicts (2):
- CUSTOMERS: Table exists (will fail to create schema)
- ORDERS: Table exists (will fail to create schema)

Data Conflicts (3):
- PRODUCTS: Table has 1,250 rows (data will be appended)
- CATEGORIES: Table has 45 rows (data will be appended)
- SUPPLIERS: Table is empty (data will be imported)

New Tables (1):
- INVOICES: Will be created (0 rows)

Options:
□ Skip conflicting tables
□ Drop and recreate tables
□ Append data to existing tables

Continue with import? [Yes] [No]
```

## Technical Implementation

### Export Overwrite Checker
Located in: `ExportOverwriteChecker.cs`

Key features:
- Reads existing export metadata
- Calculates file sizes and table counts
- Schema-aware file detection
- Timestamped log handling

### Import Overwrite Checker
Located in: `ImportOverwriteChecker.cs`

Key features:
- Database provider abstraction
- Table existence checking
- Row count analysis
- Cross-database schema mapping

### Database Provider Integration

Each database provider implements schema-specific logic:

```csharp
// Firebird Provider - Case sensitivity handling
var tableConditions = new List<string>();
foreach (var tableName in tableNames.Select(t => t.Trim()))
{
    // Add original case (for quoted identifiers)
    tableConditions.Add($"TRIM(TRAILING FROM rdb$relation_name) = '{tableName}'");

    // Add uppercase (for unquoted identifiers)
    string upperName = tableName.ToUpperInvariant();
    if (upperName != tableName)
    {
        tableConditions.Add($"TRIM(TRAILING FROM rdb$relation_name) = '{upperName}'");
    }
}
```

## Configuration Options

### CLI Arguments
```bash
# Skip overwrite prompts (dangerous!)
dotnet run -- export --output ./data --force

# Import with specific conflict handling
dotnet run -- import --input ./data --drop-existing
dotnet run -- import --input ./data --append-data
dotnet run -- import --input ./data --skip-conflicts
```

### UI Settings
- **Always prompt**: Default behavior, always ask user
- **Auto-append**: Automatically append data to existing tables
- **Auto-skip**: Skip conflicting tables without prompting
- **Auto-replace**: Replace existing tables (destructive)

## Best Practices

### For Export Operations
1. **Use descriptive directory names** with timestamps
2. **Review existing exports** before overwriting
3. **Keep backups** of important export files
4. **Use table filtering** to export only needed tables

### For Import Operations
1. **Always review conflicts** before proceeding
2. **Test imports** on non-production databases first
3. **Use schema-only imports** to validate table structures
4. **Backup target database** before large imports

### Cross-Database Scenarios
When importing between different database systems:

1. **Schema Differences**: SQL Server 'dbo' → Firebird 'SYSDB'
2. **Type Mapping**: Automatic conversion of incompatible types
3. **Identifier Quoting**: Proper handling of case-sensitive names
4. **Constraint Handling**: Foreign keys created after all tables

## Error Recovery

### Failed Exports
- Partial files are cleaned up automatically
- Log files contain detailed error information
- Metadata files are only written on successful completion

### Failed Imports
- Transactions are rolled back on error (where supported)
- Tables created before failure are left in place
- Detailed error logging helps identify specific issues
- Option to continue with remaining tables after failures

## Logging and Diagnostics

### Export Logs
- Timestamped filenames prevent overwriting
- Include table processing details
- Show file sizes and compression ratios
- Record any skipped or failed tables

### Import Logs
- Separate log per import operation
- Include conflict resolution choices
- Show row counts and processing times
- Record any data conversion issues

Example log entry:
```
2023-01-15 14:30:45 [INFO] Import conflict analysis complete
2023-01-15 14:30:45 [WARN] Table CUSTOMERS exists with 1,250 rows - data will be appended
2023-01-15 14:30:45 [INFO] User chose to proceed with append operation
2023-01-15 14:30:47 [INFO] Successfully imported 2,350 new rows to CUSTOMERS
```