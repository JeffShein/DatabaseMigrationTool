# Firebird Database Provider

## Overview

The FirebirdProvider supports both Firebird 2.5 and Firebird 5.0+ databases with automatic version detection and appropriate client library selection.

## Architecture

### Version Detection System
- **Automatic Detection**: Analyzes database file headers to detect ODS (On-Disk Structure) version
- **Manual Override**: Use `Version` parameter in connection string (e.g., "Version=2.5")
- **Fallback Strategy**: Defaults to Firebird 2.5 for maximum compatibility

### ODS Version Mapping
- **ODS 11-13**: Firebird 2.5 → Uses `fbembed.dll` (embedded mode)
- **ODS 14+**: Firebird 3.0+ → Uses `fbclient.dll` (client/server mode)

### DLL Management
The provider automatically switches between:
- **fbembed.dll**: Embedded mode for Firebird 2.5
- **fbclient.dll**: Client/server mode for Firebird 3.0+

## Configuration

### Connection String Examples
```csharp
// Automatic version detection
"Database=C:\\path\\to\\database.fdb;User=SYSDBA;Password=masterkey;"

// Force Firebird 2.5
"Database=C:\\path\\to\\database.fdb;User=SYSDBA;Password=masterkey;Version=2.5;"

// Force Firebird 5.0
"Database=C:\\path\\to\\database.fdb;User=SYSDBA;Password=masterkey;Version=5.0;"
```

### UI Configuration
- **Firebird Version Dropdown**: Select between "Firebird 5.0/4.0/3.0+" and "Firebird 2.5"
- **Default Selection**: Firebird 5.0+ for new connections
- **Automatic ServerType**: Set based on version (0 for client/server, 1 for embedded)

## SQL Syntax Compatibility

### Identifier Escaping
- **Table Names**: Uses quoted identifiers for case-sensitive tables (e.g., `"TableName"`)
- **Column Names**: Uses quoted identifiers to match CREATE TABLE syntax (e.g., `"ColumnName"`)
- **Cross-Database Compatibility**: Handles SQL Server to Firebird imports with proper identifier mapping
- **Case Sensitivity**: Supports both quoted (case-sensitive) and unquoted (uppercase) identifiers

### Key Differences from Other Providers
```sql
-- SQL Server style (NOT supported)
INSERT INTO [SCHEMA].[TABLE] ([COLUMN]) VALUES ('value')

-- Firebird style (SUPPORTED)
INSERT INTO TABLE (COLUMN) VALUES ('value')
```

## Deployment Requirements

### Required DLLs
- `fbembed.dll` - Firebird 2.5 embedded client
- `fbclient.dll` - Firebird 3.0+ client library
- `fbintl.dll` - Internationalization support
- `ib_util.dll` - Utility functions

### Unicode Support DLLs
- `icudt30.dll` - Unicode data
- `icuin30.dll` - Unicode internationalization
- `icuuc30.dll` - Unicode common functions

### Runtime Dependencies
- `msvcp80.dll` - Visual C++ runtime
- `msvcr80.dll` - Visual C++ runtime

### Configuration Files
- `firebird.conf` - Firebird configuration
- `fbintl.conf` - Internationalization configuration
- `firebird.msg` - Error messages

## Troubleshooting

### Common Issues

#### Connection Locking Errors
**Symptoms**:
- "lock conflict on no wait transaction"
- "unsuccessful metadata update"
- "object TABLE 'TABLENAME' is in use"

**Cause**: Connection pooling maintaining persistent connections that hold metadata locks
**Solution**: Provider automatically disables connection pooling for Firebird operations

#### "Token unknown" SQL Errors
**Cause**: SQL syntax incompatibility during cross-database imports
**Solution**: Provider automatically converts SQL Server syntax to Firebird-compatible syntax

#### Table Creation Conflicts
**Symptoms**: "Table already exists" errors during import
**Solution**: Provider now checks table existence before creation and prompts for overwrite confirmation

#### Case Sensitivity Issues
**Symptoms**: Table not found errors with correct table names
**Solution**: Provider handles both quoted (case-sensitive) and unquoted (uppercase) identifier matching

#### Cross-Database Import Errors
**Symptoms**: Type conversion errors when importing from SQL Server
**Solution**: Provider includes comprehensive type mapping system:
- `NVARCHAR` → `VARCHAR`
- `DATETIME2` → `TIMESTAMP`
- `BIT` → `SMALLINT`
- `UNIQUEIDENTIFIER` → `CHAR(36)`

#### DLL Loading Errors
**Cause**: Missing Firebird DLLs or Visual C++ runtime
**Solution**: Ensure all required DLLs are in application directory

#### Version Detection Issues
**Cause**: Corrupted database file or unsupported ODS version
**Solution**: Use manual version override in connection string

#### Connection Timeout
**Cause**: Firebird server not running (for client/server mode)
**Solution**: Start Firebird service or switch to embedded mode (2.5)

### Diagnostic Commands
```bash
# Test Firebird connection
dotnet run -- test-firebird --connection "Database=test.fdb;User=SYSDBA;Password=pass;"

# Test with specific version
dotnet run -- test-firebird --connection "Database=test.fdb;User=SYSDBA;Password=pass;Version=2.5;"

# Diagnose connection issues
dotnet run -- diagnose --provider firebird --connection "Database=test.fdb;User=SYSDBA;Password=pass;"
```

## Recent Improvements

### Connection Management Enhancements
- **Fixed Connection Locking**: Resolved "lock conflict on no wait transaction" errors by disabling connection pooling
- **Metadata Lock Prevention**: Isolated DDL operations to prevent table-in-use conflicts
- **Improved Transaction Isolation**: Uses ReadCommitted isolation level for better concurrency

### Cross-Database Compatibility
- **SQL Server to Firebird**: Comprehensive type mapping and syntax conversion
- **Schema Mapping**: Intelligent handling of schema differences (dbo vs SYSDB)
- **Identifier Handling**: Proper quoting for case-sensitive table and column names
- **Default Value Cleanup**: Converts SQL Server default syntax to Firebird format

### Import/Export Improvements
- **Table Filtering**: Fixed table filtering in both export and import operations
- **Overwrite Detection**: Schema-aware overwrite checking for UI operations
- **Existence Checking**: Tables are checked for existence before creation attempts
- **Case Sensitivity**: Handles both quoted and unquoted identifier matching

### Error Prevention
- **Pre-flight Validation**: Checks table existence and schema compatibility
- **Graceful Degradation**: Continues import when individual tables fail (with user consent)
- **Detailed Logging**: Timestamped logs with comprehensive error reporting
- **SQL Syntax Validation**: Prevents common cross-database syntax errors

## Implementation Notes

### Schema Handling
- Firebird schemas are user/owner names, not separate database objects like SQL Server
- Tables are owned by users (e.g., SYSDBA.TABLENAME)
- Provider maps all schemas to table owners for compatibility
- Cross-database imports automatically map SQL Server 'dbo' schema to appropriate Firebird owner

### Transaction Management
- Uses explicit transactions for data import operations
- Supports batch operations with configurable batch sizes
- Proper rollback on errors when continue-on-error is disabled
- Connection pooling disabled to prevent metadata lock conflicts

### Performance Optimizations
- Uses parameterized queries where possible
- Individual INSERT execution for Firebird (no batch statements)
- Implements proper connection disposal patterns
- Efficient table filtering to process only selected tables