# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Development Guidelines

- Act as an expert software developer
- Accept edits should be on by default when working with Claude Code
- Never hardcode data element names except in debugging
- Write code generically to solve problems
- Prefer simpler, less complicated solutions
- Use debugging to work together to solve problems
- Write messages to log files rather than the console
- Treat compiler warnings with the same importance as errors - fix all warnings
- No bullshit - be direct and straightforward

## Requirements

- .NET 9.0 SDK (project targets net9.0-windows, specified in global.json)
- Windows platform (WPF dependency)
- x86 architecture (configured for win-x86 runtime)
- Visual C++ runtime (for Firebird DLLs)

### Firebird-Specific Dependencies
Firebird provider requires additional DLL files deployed with the application:
- `fbembed.dll`, `fbclient.dll`, `fbintl.dll` (Firebird libraries)
- `icudt30.dll`, `icuin30.dll`, `icuuc30.dll` (Unicode support)  
- `msvcp80.dll`, `msvcr80.dll` (Visual C++ runtime)
- Configuration files: `firebird.conf`, `fbintl.conf`, `firebird.msg`

## Common Commands

### Building the Project

```bash
# Restore dependencies
dotnet restore

# Build the project in Debug mode
dotnet build

# Build the project in Release mode
dotnet build --configuration Release

# Build self-contained executable (matches project config)
dotnet publish -c Release --self-contained true -r win-x86
```

### Distribution and Packaging

The project includes comprehensive distribution tools for professional Windows deployment:

```bash
# Build both ZIP and MSI distributions (PowerShell)
.\build-distribution.ps1

# Build both distributions (Batch file for convenience)
.\build-both.bat

# Build only ZIP distribution
.\build-distribution.ps1 -ZipOnly

# Build only MSI installer
.\build-distribution.ps1 -MsiOnly

# Build MSI installer (dedicated script)
cd installer
.\build-msi.ps1

# Clean build with custom output
.\build-distribution.ps1 -Clean -OutputPath "./release"
```

**Distribution Options**:
- **ZIP Archive**: Portable, self-contained distribution with no installation required
- **MSI Installer**: Professional Windows installer with Start Menu integration, file associations, and uninstall support

See `DISTRIBUTION.md` for complete packaging and deployment documentation.

### Running the Application

```bash
# Run in GUI mode (default)
dotnet run

# Run in console interactive mode
dotnet run -- --console

# List available database providers
dotnet run -- providers

# Export a database
dotnet run -- export --provider sqlserver --connection "Server=localhost;Database=SourceDB;User Id=sa;Password=P@ssw0rd;TrustServerCertificate=True;" --output ./export_output

# Export specific tables only
dotnet run -- export --provider sqlserver --connection "Server=localhost;Database=SourceDB;User Id=sa;Password=P@ssw0rd;TrustServerCertificate=True;" --output ./export_output --tables "Customers,Orders,OrderDetails"

# Export with filtering criteria (create criteria.json first)
dotnet run -- export --provider sqlserver --connection "Server=localhost;Database=SourceDB;User Id=sa;Password=P@ssw0rd;TrustServerCertificate=True;" --output ./export_output --criteria criteria.json

# Export schema only (no data)
dotnet run -- export --provider sqlserver --connection "Server=localhost;Database=SourceDB;User Id=sa;Password=P@ssw0rd;TrustServerCertificate=True;" --output ./export_output --schema-only

# Import a database
dotnet run -- import --provider mysql --connection "Server=localhost;Database=TargetDB;User=root;Password=password;" --input ./export_output

# Import with options
dotnet run -- import --provider mysql --connection "Server=localhost;Database=TargetDB;User=root;Password=password;" --input ./export_output --no-create-schema --no-foreign-keys --continue-on-error

# Import schema only
dotnet run -- import --provider mysql --connection "Server=localhost;Database=TargetDB;User=root;Password=password;" --input ./export_output --schema-only

# View database schema
dotnet run -- schema --provider postgresql --connection "Host=localhost;Database=mydb;Username=postgres;Password=password;" --verbose

# Generate SQL scripts for schema
dotnet run -- schema --provider postgresql --connection "Host=localhost;Database=mydb;Username=postgres;Password=password;" --script --script-path ./schema_scripts
```

### Special Commands

```bash
# Emergency import (for recovery)
dotnet run -- emergency-import --input ./export_output --provider mysql --connection "Server=localhost;Database=RecoveryDB;User=root;Password=password;"

# Direct transfer (bypass batch processing)
dotnet run -- direct-transfer --source-provider sqlserver --source-connection "Server=source;Database=SourceDB;User Id=sa;Password=P@ssw0rd;TrustServerCertificate=True;" --target-provider mysql --target-connection "Server=target;Database=TargetDB;User=root;Password=password;" --tables "Customers,Orders"

# File inspection (individual table metadata)
dotnet run -- dump-file --file ./export_output/table_metadata/dbo_Customers.meta

# File inspection (data files)
dotnet run -- dump-file --file ./export_output/data/dbo_Orders_batch0.bin

# Diagnostic analysis
dotnet run -- debug-analyze-file --file ./export_output/table_data_Orders.bin

# Validate export/import on a single table (troubleshooting)
dotnet run -- validate-ei --provider sqlserver --connection "Server=localhost;Database=SourceDB;User Id=sa;Password=P@ssw0rd;TrustServerCertificate=True;" --table "dbo.Customers" --verbose

# Diagnose connection and provider issues
dotnet run -- diagnose --provider firebird --connection "DataSource=localhost;Database=C:\firebird\data\mydb.fdb;User=SYSDBA;Password=masterkey;"
```

### Configuration File Management

The tool supports comprehensive configuration files in JSON format for saving and reusing parameters across operations:

```bash
# Create a sample configuration file with all options
dotnet run -- config --create-sample my_config.json

# Validate a configuration file
dotnet run -- config --validate my_config.json

# Display configuration file contents
dotnet run -- config --show my_config.json

# Use configuration file with export (command-line options override config file)
dotnet run -- export --config my_config.json

# Use configuration with specific overrides
dotnet run -- export --config my_config.json --provider mysql --output ./different_output

# Use configuration file with import
dotnet run -- import --config my_config.json

# Use configuration file with schema operations
dotnet run -- schema --config my_config.json --verbose
```

**Configuration File Structure:**
Configuration files are JSON format containing export, import, schema, and global settings. They support:
- All command-line parameters for export, import, and schema operations
- Connection strings, provider settings, and paths
- Batch sizes, table filters, and operation flags
- Global settings like timeouts and logging preferences
- Version information for backward compatibility

**Benefits:**
- **Automation**: Save complex configurations once, reuse in scripts
- **Team Sharing**: Export configurations for team collaboration
- **Environment Management**: Different configs for dev/staging/production
- **Documentation**: Configuration files document migration procedures
- **Flexibility**: Override specific parameters without editing the file

### Debugging and Diagnostics

```bash
# Check build warnings (treat as errors per project guidelines)
dotnet build -v normal

# Clean build artifacts
dotnet clean

# Run with verbose console output (when debugging)
dotnet run -- --console --verbose
```

### Testing

The project now includes a test framework using xUnit with Moq for mocking:

```bash
# Run all tests
dotnet test

# Run tests with detailed output
dotnet test -v normal

# Run specific tests by filter
dotnet test --filter "TestMethodName"

# Run tests in specific test project
dotnet test tests/DatabaseMigrationTool.Tests/

# Run tests with code coverage
dotnet test --collect:"XPlat Code Coverage"
```

## Project Architecture

### Overall Structure

The Database Migration Tool is a .NET application built with .NET 9.0 that supports both GUI (WPF) and console modes. It is designed to export and import database schemas and data between different database systems with high performance.

The application follows a provider-based architecture where different database systems are supported through specific provider implementations that implement a common interface. Currently supported database systems include SQL Server, MySQL, PostgreSQL, and Firebird.

### Key Components

1. **Provider System**:
   - `IDatabaseProvider`: The core interface that all database providers implement
   - Concrete implementations: `SqlServerProvider`, `MySqlProvider`, `PostgreSqlProvider`, `FirebirdProvider`
   - `DatabaseProviderFactory`: Factory class that creates appropriate provider instances

2. **Data Models**:
   - `TableSchema`: Represents a database table structure
   - `ColumnDefinition`: Defines column properties (name, type, nullability, etc.)
   - `IndexDefinition`: Defines index structures
   - `ForeignKeyDefinition`: Defines foreign key relationships
   - `ConstraintDefinition`: Defines table constraints
   - `RowData`: Contains the actual data values as key-value pairs
   - `DatabaseExport`: The top-level container for exported database data

3. **Metadata Management System**:
   - `MetadataManager`: Central utility for reading/writing granular export metadata with table-specific files
   - `ExportManifest`: Lightweight JSON overview of exported tables, timestamps, and configuration
   - `TableManifestEntry`: Individual table entry tracking schema, row count, and file information
   - `TableMetadata`: Individual table schema stored as compressed MessagePack binary (.meta files)
   - `DependencyManifest`: Cross-table relationships and foreign key dependencies for import ordering
   - `ForeignKeyDependency`: Specific foreign key relationship information between tables

4. **Core Services**:
   - `DatabaseExporter`: Handles the export of database schema and data
   - `DatabaseImporter`: Handles the import of database schema and data
   - `PerformanceOptimizer`: Optimizes performance for large datasets
   - `DirectTransferUtility`: Handles direct database-to-database transfers
   - `EmergencyImporter`: Recovery utility for importing from batch files

5. **Command-Line Processing**:
   - Uses `CommandLineParser` package to process command arguments
   - Supports multiple verbs: `providers`, `export`, `import`, `schema`, etc.
   - Each command has its own options class with proper help texts

5. **Export/Import Overwrite Detection**:
   - `ExportOverwriteChecker`: Provides table-specific overwrite detection and surgical deletion
   - `ImportOverwriteChecker`: Analyzes existing target database tables for import conflicts
   - `ExportOverwriteDialog`: User interface for confirming export overwrites with detailed file lists
   - `ImportOverwriteDialog`: User interface for confirming import operations that affect existing data

6. **Serialization & Compression**:
   - Uses `MessagePack` for efficient binary serialization
   - Applies compression (`GZip` and `BZip2`) to reduce output file sizes

### Data Flow

1. **Export Process**:
   - Discover database schema (tables, columns, indexes, etc.)
   - Calculate dependency ordering to handle foreign key relationships
   - Export granular metadata: JSON manifest files and individual table .meta files
   - Export table data to individual compressed files, with optional filtering
   - Perform table-specific overwrite detection before writing any files
   - Support incremental exports that preserve existing non-conflicting tables

2. **Import Process**:
   - Read granular metadata from JSON manifests and individual table .meta files
   - Analyze target database for existing tables and data conflicts
   - Create schema in the target database (if enabled)
   - Import data in the correct dependency order based on foreign key relationships
   - Create foreign keys after data import (if enabled)

3. **Direct Transfer Process**:
   - Connect to both source and target databases simultaneously
   - Calculate dependency ordering
   - Transfer data directly between databases without intermediate files

### Export File Structure

The application uses a granular file structure for exports that enables table-specific operations and incremental exports:

#### Core Metadata Files
- **`export_manifest.json`**: Lightweight JSON manifest containing export overview, table list, and timestamps
- **`dependencies.json`**: Foreign key dependencies and import ordering information
- **`table_metadata/[schema]_[table].meta`**: Individual BZip2-compressed MessagePack files containing detailed table schema

#### Data Files  
- **`data/[schema]_[table].bin`**: Single compressed data file for tables exported in one batch
- **`data/[schema]_[table]_batch[N].bin`**: Multiple compressed batch files for large tables
- **`data/[schema]_[table].info`**: Table-specific export statistics and metadata
- **`data/[schema]_[table].error`**: Error logs specific to individual table exports

#### Log Files (Auto-Updated)
- **`export_log.txt`**: General export operation log (overwritten on each export)
- **`export_skipped_tables.txt`**: List of tables skipped during export (overwritten on each export)

#### Key Benefits of This Structure
- **Incremental Exports**: Add new tables without affecting existing exports
- **Selective Overwrites**: Only conflicting tables are removed when overwriting
- **Granular Recovery**: Individual table failures don't affect the entire export
- **Parallel Processing**: Table operations can be parallelized more effectively
- **Merge Capability**: Multiple partial exports can be combined into complete exports

#### Overwrite Detection Logic
The system performs intelligent overwrite detection:
- **Table-Specific Analysis**: Only warns when specific tables being exported would conflict
- **Surgical Deletion**: Removes only conflicting table files, preserves others
- **Manifest Updates**: Updates manifests and dependencies rather than overwriting
- **Log File Exclusion**: Doesn't warn about log files which are always overwritten

### Security Considerations

The application implements multiple layers of security to protect against common database vulnerabilities:

#### SQL Injection Prevention
- **Input Validation**: All SQL identifiers (table names, column names, aliases) are validated against strict character sets
- **Length Limits**: SQL identifiers are limited to 128 characters maximum
- **Character Restrictions**: Only alphanumeric, underscore, and single dot allowed in identifiers
- **Naming Rules**: Identifiers must start with letter or underscore
- **Parameter Escaping**: User input is properly escaped using single quote doubling
- **Schema Validation**: Schema.table format limited to single dot to prevent injection

#### Resource Management
- **Connection Disposal**: Proper try-finally patterns ensure database connections are always disposed
- **Memory Management**: IDisposable implementations with comprehensive disposal patterns
- **Stream Management**: All file streams and data readers implement proper resource cleanup
- **Exception Safety**: Resource disposal guaranteed even when exceptions occur

#### Connection Security
- **Connection String Validation**: Provider-specific validation of connection parameters
- **Parameter Sanitization**: Removal of unsupported or dangerous connection parameters
- **SSL/TLS Settings**: Default to secure connection settings where supported
- **Credential Protection**: Connection profile passwords encrypted using AES with per-machine keys

### Performance Considerations

- Uses batched operations for large datasets
- Implements parallel processing where appropriate
- Applies compression to reduce file sizes
- Uses `WITH (NOLOCK)` hint for SQL Server reads to avoid blocking
- Configurable batch sizes for both export and import
- Async/await patterns with ConfigureAwait(false) to prevent deadlocks

### Modes of Operation

1. **GUI Mode**: WPF-based user interface for interactive use
   - Main tabbed interface for Export, Import, and Schema operations
   - Table selection browser with search and multi-select capabilities
   - Connection string builder with provider-specific defaults
   - Loading spinners for long-running operations
   - Schema view window for examining database structures
   - Firebird database explorer window for .fdb file browsing

2. **Console Mode**: Command-line interface with interactive menu
   - Run with `--console` flag for menu-driven operation
   - Provides guided workflows for common operations

3. **Command Line Mode**: Direct command processing for scripting and automation
   - Supports all export, import, schema, and utility operations
   - Designed for batch processing and CI/CD integration

### Special Utility Functions

1. **Emergency Import**: `emergency-import` command for recovering data from batch files directly
2. **Direct Transfer**: `direct-transfer` command to bypass batch processing completely
3. **File Inspection**: `dump-file` command for examining binary files
4. **Diagnostic Analysis**: `debug-analyze-file` command for analyzing data files
5. **Export/Import Validation**: `validate-ei` command for troubleshooting single table export/import

## Project Structure

The solution consists of a single main project:

1. **DatabaseMigrationTool**: The main application
   - `Commands/`: Command-line verb handlers
   - `Controls/`: WPF UI controls
   - `Helpers/`: Utility and helper classes
   - `Logging/`: Logging system
   - `Models/`: Data models for database objects
   - `Providers/`: Database provider implementations
   - `Services/`: Core services for export/import operations
   - `Utilities/`: General utility functions

## Development Patterns and Conventions

1. **Provider Implementation**:
   - All database providers must implement the `IDatabaseProvider` interface
   - Each provider must register itself with the `DatabaseProviderFactory` for discovery
   - Use the `SetLogger` method to handle logging within provider implementations
   - When implementing new database providers, follow existing patterns in the codebase

2. **Application Entry Point**:
   - Program.cs handles both WPF GUI mode and console command-line mode
   - Uses `[STAThread]` attribute for WPF compatibility
   - Special handling for utility commands like `dump-file` and `debug-analyze-file`
   - CommandLineParser library processes command-line arguments with verb-based commands

3. **Firebird Provider**:
   - The Firebird provider supports both Firebird 2.5 and 3+ versions
   - It uses auto-detection to determine the version based on database file format
   - Implements specialized version handling to maximize compatibility
   - Connection strings for Firebird can include a Version parameter to explicitly set the version

4. **Security Patterns**:
   - Always validate SQL identifiers using `IsValidSqlIdentifier` and `IsValidTableNamePart` methods
   - Use `EscapeSqlIdentifier` and `GetSafeTableName` helper methods for safe SQL construction
   - Implement proper resource disposal with try-finally patterns for connection management
   - Validate all user inputs with length limits and character restrictions before database operations

5. **Async Patterns**:
   - Always use `ConfigureAwait(false)` on async calls in service layers to prevent deadlocks
   - Implement proper cancellation token support in long-running operations
   - Use async/await consistently throughout the application rather than blocking calls

6. **Error Handling**:
   - Log detailed errors to help diagnose issues using the centralized ErrorHandler utility
   - Include inner exception details where appropriate
   - Use the `ContinueOnError` flag to control error behavior during import operations
   - Implement retry patterns with exponential backoff for recoverable operations

7. **Testing**:
   - Test framework configured using xUnit with Moq for mocking and coverlet for code coverage
   - Tests are located in `tests/DatabaseMigrationTool.Tests/`
   - Use descriptive test names following the pattern `[Class]_Should[ExpectedBehavior]`
   - Use ITestOutputHelper for logging in test methods

8. **Command Handling**:
   - Each command implements a static `Execute` method
   - Command parameters are defined using the `CommandLineParser` attribute system
   - Return appropriate exit codes (0 for success, non-zero for failures)

9. **Serialization Strategy**:
   - Uses MessagePack for efficient binary serialization of database objects
   - Applies compression (GZip/BZip2) to reduce file sizes significantly
   - Separates metadata and data files for better organization and performance

## Working with the Code

- Database providers are implemented in `src/DatabaseMigrationTool/Providers/`
- Core export/import logic is in `src/DatabaseMigrationTool/Services/`
- Command-line argument handling is in `src/DatabaseMigrationTool/Commands/`
- Data models are in `src/DatabaseMigrationTool/Models/`
- WPF UI controls are in `src/DatabaseMigrationTool/Controls/`

## UI Configuration and Behavior

### Connection String Control

The `ConnectionStringControl` in the Controls folder provides a unified interface for configuring database connections across all supported providers, with advanced profile management capabilities. Key features:

#### Connection Profile Management

The control now includes comprehensive profile management:

- **Profile Dropdown**: Select from saved connection profiles with visual indicators (name, provider, server)
- **Save Profile Button (💾)**: Save current connection settings as a reusable profile
- **Manage Profiles Button (📋)**: Open the profile manager for advanced operations
- **Automatic Profile Loading**: Profiles populate connection fields and update last-used timestamps
- **Encrypted Storage**: Passwords are encrypted using AES encryption with per-machine keys
- **Import/Export**: Profiles can be exported for sharing (without passwords) and imported

#### Profile Manager Features

- **Profile Filtering**: Search profiles by name, provider, or server
- **Profile Details**: View comprehensive information about selected profiles
- **Profile Operations**: Edit, duplicate, delete profiles with confirmation dialogs
- **Recent Profiles**: Automatic tracking of last-used timestamps
- **Bulk Operations**: Import/export multiple profiles as JSON files

#### Shared Profile System

The application now features a unified profile system that works across all tabs:

- **Cross-Tab Synchronization**: When you select a profile in any tab (Export/Import/Schema), it automatically loads in all other tabs
- **Global Profile Indicator**: A prominent banner shows the currently active profile across all operations
- **One-Click Profile Management**: Save a profile once, use it everywhere
- **Automatic Cache Invalidation**: Table caches are cleared when switching profiles to ensure consistency
- **Profile Clear Function**: One button to clear the active profile from all tabs
- **Visual Feedback**: Clear indicators show which profile is active and when profiles change

#### Workflow Benefits

- **Consistent Operations**: Ensure Export, Import, and Schema operations use the same database
- **Reduced Configuration**: Configure connection once, use across all operations  
- **Error Prevention**: Eliminates mistakes from manually entering connections multiple times
- **Efficiency**: Quick switching between development, staging, and production environments

#### Connection Provider Behaviors

1. **SQL Server Provider**:
   - Defaults to "LocalHost" as server name
   - Windows Authentication is selected by default
   - Username/Password fields are disabled when Windows Authentication is selected
   - Username/Password fields become editable when SQL Server Authentication is selected
   - Trust Server Certificate is enabled by default to prevent certificate errors

2. **Firebird Provider**:
   - Username defaults to "SYSDB" with password "Hosis11223344"
   - Username and password fields are disabled by default with "Override" checkboxes
   - Check the override checkboxes to enable editing of username/password fields
   - Supports both Firebird 2.5 and 3.0+ formats via dropdown selection
   - Read-only mode is enabled by default
   - Browse button available for selecting database files

3. **MySQL Provider**:
   - Defaults to "LocalHost" server and port 3306
   - SSL/TLS connection option available
   - All fields are editable by default

4. **PostgreSQL Provider**:
   - Defaults to "LocalHost" server and port 5432
   - SSL connection is enabled by default
   - All fields are editable by default

### Table Selection Interface

The application includes a comprehensive table selection system for all operations:

1. **Table Browser Window** (`TableSelectionWindow`):
   - Connects to the configured database and loads all available tables
   - Provides search/filter functionality to quickly find specific tables
   - Shows table names with schema prefixes when applicable
   - Supports multi-selection with checkboxes and Select All/None buttons
   - Displays selection count and status information
   - Remembers previously selected tables when reopened

2. **Table Field Enhancements**:
   - **Browse Button**: Opens the table selection window for easy table picking
   - **Clear Button (✖)**: Quickly clears the table list
   - **Bidirectional Sync**: Existing table selections are pre-checked in the browser
   - **Comma-Separated Output**: Selected tables populate as comma-separated list

3. **Intelligent Caching System**:
   - **Performance Optimization**: Table lists are cached to avoid repeated database queries
   - **Smart Invalidation**: Cache automatically refreshes when provider or connection changes
   - **Per-Tab Caching**: Each tab (Export, Import, Schema) maintains independent cache
   - **Instant Loading**: Subsequent table browsing operations load instantly from cache
   - **Visual Feedback**: Status indicates whether data is from cache or fresh database query

### Schema View Window

The Schema View functionality provides comprehensive database schema analysis:

- Defaults to SQL Server provider when accessed
- Passes the correct provider name to ensure compatibility
- Displays table structure, columns, indexes, foreign keys, and constraints
- Provides estimated statistics based on metadata when database access is limited
- Generates SQL scripts for table creation if requested

## Known Issues and Solutions

### Firebird DLL Deployment

**Automatic Deployment**: The project includes an MSBuild target (`CopyFirebirdDllsToRoot`) that automatically copies required Firebird DLLs to the application root directory after build.

**Required Files**:
- `fbembed.dll`, `fbclient.dll`, `fbintl.dll` (Firebird libraries)
- `icudt30.dll`, `icuin30.dll`, `icuuc30.dll` (Unicode/internationalization support)
- `msvcp80.dll`, `msvcr80.dll` (Visual C++ runtime)
- Configuration files: `firebird.conf`, `fbintl.conf`, `firebird.msg`

**Deployment Structure**:
- Source DLLs are stored in `firebird/` directory
- MSBuild target copies DLLs to application root at build time
- Configuration files remain in `firebird/` subdirectory
- Single source location prevents file duplication

### Connection String Compatibility

When switching providers in the UI, connection strings may contain parameters specific to other providers:
- SQL Server provider automatically filters out incompatible parameters (like Firebird's "version" parameter)
- This prevents `ArgumentException` errors when testing connections

### Provider Selection Consistency

The application ensures that the selected provider in the UI matches the provider used internally:
- Schema view operations use the provider selected in the dropdown
- Connection string generation matches the selected provider type
- Provider switching updates all dependent UI elements

## Troubleshooting Common Issues

1. **"System.DllNotFoundException for fbembed"**:
   - Ensure Firebird DLLs are in the application root directory
   - Check that all required Visual C++ runtime DLLs are present

2. **"Keyword 'version' not supported" in SQL Server**:
   - This occurs when Firebird connection parameters leak into SQL Server connections
   - The SQL Server provider automatically cleans these parameters

3. **Empty default server names**:
   - Ensure proper initialization order in ConnectionStringControl
   - Default values are set during UI loading and provider switching

4. **Username/Password fields not properly disabled**:
   - Verify that Windows Authentication is properly initialized as default
   - Check that event handlers for authentication type changes are properly wired

5. **Table selection performance**:
   - Table lists are cached automatically per connection
   - Cache invalidates when provider or connection string changes
   - Use "Browse..." buttons for efficient table selection instead of manual typing

## Recent Enhancements

### Metadata Architecture Overhaul (2025-09)
- **Granular Metadata System**: Replaced monolithic metadata.bin with per-table .meta files and JSON manifests
- **Table-Specific Overwrite Detection**: Precise conflict analysis that only warns for actual table conflicts
- **Surgical Deletion**: Selective file removal that preserves non-conflicting tables during overwrites
- **Incremental Export Support**: Enable building exports table-by-table without conflicts
- **Clean Terminology**: Removed legacy "hybrid" references throughout codebase

### Import System Enhancements (2025-09)
- **Import Table Browser**: Fixed import table selection to show tables from export data, not target database
- **ImportTableSelectionWindow**: New dedicated window for selecting tables to import from export metadata
- **Flexible Table Name Matching**: Enhanced importer to handle both "TableName" and "schema.TableName" formats
- **Import Overwrite Detection**: Fixed to use new MetadataManager instead of legacy metadata.bin
- **Export Data Validation**: Import directory validation ensures valid export before table browsing
- **Rich Table Information**: Import browser shows column count, primary keys, and foreign keys from export

### User Interface Improvements
- **Table Selection System**: Added comprehensive table browsing with search, multi-select, and caching
- **Connection Profile Management**: Advanced profile system with encryption, import/export, and cross-tab synchronization
- **Connection Defaults**: Improved initialization of default values for all database providers  
- **Authentication Handling**: Fixed SQL Server Windows Authentication defaults and field states
- **Overwrite Dialogs**: Added comprehensive export/import overwrite detection and user confirmation dialogs
- **Progress Bar Fixes**: Properly clear progress UI when operations are cancelled or fail

### Performance Optimizations
- **Intelligent Caching**: Table lists are cached per connection to avoid repeated database queries
- **Smart Cache Invalidation**: Cache automatically refreshes only when connection details change
- **Instant Table Loading**: Subsequent table browsing operations load instantly from cache
- **Configuration Management**: Comprehensive JSON configuration system for automation and team sharing

### Developer Experience
- **Cleaner Project Structure**: Simplified Firebird component deployment using MSBuild targets
- **Migration Configuration System**: Complete configuration file support with validation and sample generation
- **Better Documentation**: Enhanced CLAUDE.md with comprehensive UI behavior documentation
- **Consistent Patterns**: Standardized connection handling across all database providers

### Critical Security & Performance Improvements (January 2025)
- **Connection Leak Prevention**: Fixed critical resource leaks in FirebirdProvider.CreateAndTestConnection with proper try-finally disposal patterns
- **SQL Injection Protection**: Enhanced BuildTableFilter method with comprehensive input validation, identifier sanitization, and length limits
- **Async Best Practices**: Added ConfigureAwait(false) to 25+ async calls across MainWindow, DatabaseImporter, and DatabaseExporter to prevent deadlocks
- **Enhanced Input Validation**: Strengthened SQL identifier validation requiring proper naming conventions and limiting special characters
- **Security Hardening**: Implemented multi-layer defense against SQL injection with character validation, length limits, and proper escaping

### Major Refactoring Initiative (January 2025)
- **Dependency Injection Architecture**: Refactored MainWindow to use proper DI container with Microsoft.Extensions.DependencyInjection
- **Method Decomposition**: Broke down large StartExport/StartImport methods (~280 lines each) into focused, single-responsibility methods
- **Centralized Error Handling**: Implemented comprehensive ErrorHandler utility with automatic structured logging and categorized error responses
- **Configuration Management**: Added comprehensive UserSettings system with JSON persistence, validation, and user preference tracking
- **Connection Management Consolidation**: Enhanced ConnectionManager with UserSettings timeout integration and resource pooling
- **UI Thread Safety**: Fixed cross-thread operations with proper Dispatcher.Invoke() patterns for all UI updates
- **Progress Bar UX**: Implemented proper progress bar clearing with 2-second success visibility delay
- **Compiler Warning Elimination**: Achieved 0 warnings through systematic fixing of CS8604 and CS1998 warnings

### Current Project State (2025-01)
- **.NET 9.0**: Project updated to latest .NET version with improved performance
- **Clean Build**: Project compiles with 0 warnings, 0 errors after comprehensive refactoring and security improvements
- **Modern Architecture**: Full dependency injection with service container, proper separation of concerns, and testable design
- **Granular Metadata Architecture**: Complete migration from monolithic metadata.bin to per-table system
- **Table-Specific Operations**: Precise overwrite detection and surgical deletion capabilities
- **Security Posture**: Critical vulnerabilities addressed with comprehensive input validation and resource management
- **Enhanced File Structure**: JSON manifests, individual .meta files, and improved organization
- **Logical Import Flow**: Import table browser now correctly shows export data, not target database tables
- **Flexible Table Matching**: Handles both simple and schema-qualified table names seamlessly
- **Configuration Management**: Comprehensive JSON configuration system with validation and sample generation
- **No Legacy Code**: All legacy compatibility code removed for cleaner architecture
- **No Hardcoded Data**: Application follows generic programming principles with dynamic table discovery
- **Professional Architecture**: MainWindow refactored from 2,000+ line monolith to focused, maintainable methods with clear separation of concerns

## Development History

### Data Validation Feature (Attempted & Removed)

**Note**: A comprehensive data validation and quality system was attempted but subsequently removed due to persistent technical issues with SQL Server table access and identifier quoting problems.

#### What Was Attempted:
- **DataValidationService**: Comprehensive validation service for checking data quality, integrity, row counts, null values, data type consistency, and foreign key relationships
- **DataValidationWindow**: Rich UI for displaying validation results with filtering, statistics, and export capabilities
- **IntegrityVerificationService**: Cross-database comparison system for verifying successful migrations
- **ValidationResult Models**: Complete data model system for validation issues, severities, and metrics
- **Integration**: UI buttons in MainWindow for "Validate Data Quality" and "Verify Data Integrity"

#### Technical Issues Encountered:
- **SQL Server Object Access**: Persistent "Invalid object name" errors despite table existence
- **Identifier Quoting**: Issues with SQL identifiers containing spaces and reserved words
- **Database Context**: Connection context mismatches between schema discovery and validation
- **Permission Issues**: Validation queries failing where schema queries succeeded

#### Resolution:
**Complete Removal (December 2024)**: After multiple troubleshooting attempts including SQL identifier quoting fixes, database context debugging, and error handling improvements, the entire data validation system was removed to maintain project stability.

#### Files Removed:
- `Services/DataValidationService.cs`
- `Services/DataIntegrityVerificationService.cs`
- `DataValidationWindow.xaml/.xaml.cs`
- `IntegrityVerificationWindow.xaml/.xaml.cs`
- `IntegrityVerificationSetupDialog.xaml/.xaml.cs`
- `Models/ValidationResult.cs`
- Associated event handlers and UI buttons

#### Current Status:
- ✅ **Clean Build**: Project compiles with 0 warnings, 0 errors
- ✅ **Core Functionality**: All original export/import/schema features intact
- ✅ **Stable UI**: No broken references or orphaned validation components
- 📋 **Future Consideration**: Data validation remains on the improvement list for potential future implementation with different approach

#### Lessons Learned:
- Cross-platform database validation requires careful handling of provider-specific SQL syntax
- Table existence checks must account for different database contexts and permission models
- Complex validation features should be implemented incrementally with thorough testing per provider

### Critical Security & Performance Improvements (January 2025)

A comprehensive security audit identified and resolved several critical vulnerabilities and performance issues:

#### Issues Addressed:
- **Connection Leaks**: FirebirdProvider.CreateAndTestConnection() could leak database connections on exceptions
- **SQL Injection Risks**: BuildTableFilter method allowed potentially dangerous SQL identifier injection
- **Async Deadlock Potential**: Missing ConfigureAwait(false) patterns could cause deadlocks in library scenarios
- **Input Validation Gaps**: Insufficient validation of SQL identifiers allowed edge case security issues

#### Solutions Implemented:
- **Resource Management**: Added proper try-finally disposal patterns with null safety guards
- **Input Sanitization**: Implemented comprehensive SQL identifier validation with character restrictions and length limits
- **Async Best Practices**: Added ConfigureAwait(false) to 25+ async calls across core services
- **Enhanced Validation**: Strengthened identifier validation requiring proper naming conventions

#### Files Modified:
- `Providers/BaseDatabaseProvider.cs`: Enhanced BuildTableFilter with comprehensive validation
- `Providers/FirebirdProvider.cs`: Fixed connection disposal in CreateAndTestConnection  
- `MainWindow.xaml.cs`: Added ConfigureAwait(false) to 12 async calls
- `Services/DatabaseImporter.cs`: Added ConfigureAwait(false) to 8 async calls
- `Services/DatabaseExporter.cs`: Added ConfigureAwait(false) to 5 async calls

#### Security Features Added:
- **SQL Identifier Validation**: Strict character set validation (alphanumeric, underscore, single dot)
- **Length Limits**: 128-character maximum for all SQL identifiers
- **Naming Rules**: Identifiers must start with letter or underscore
- **Injection Prevention**: Multi-layer defense against SQL injection attacks
- **Resource Safety**: Guaranteed resource disposal even on exceptions

#### Current Status:
- ✅ **Security Posture**: Critical vulnerabilities resolved with comprehensive defense mechanisms
- ✅ **Performance**: Async patterns optimized to prevent deadlocks and improve responsiveness  
- ✅ **Build Quality**: 0 warnings, 0 errors after all security improvements
- ✅ **Code Quality**: Enhanced input validation and error handling throughout codebase