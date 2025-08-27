# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Development Guidelines

- Act as an expert software developer
- Never hardcode data element names except in debugging
- Write code generically to solve problems
- Prefer simpler, less complicated solutions
- Use debugging to work together to solve problems
- Write messages to log files rather than the console
- Treat compiler warnings with the same importance as errors - fix all warnings
- No bullshit - be direct and straightforward

## Requirements

- .NET 9.0 (project targets net9.0-windows)
- Windows platform (WPF dependency)
- x86 architecture (configured for win-x86 runtime)

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

# File inspection
dotnet run -- dump-file --file ./export_output/metadata.bin

# Diagnostic analysis
dotnet run -- debug-analyze-file --file ./export_output/table_data_Orders.bin

# Validate export/import on a single table (troubleshooting)
dotnet run -- validate-ei --provider sqlserver --connection "Server=localhost;Database=SourceDB;User Id=sa;Password=P@ssw0rd;TrustServerCertificate=True;" --table "dbo.Customers" --verbose

# Diagnose connection and provider issues
dotnet run -- diagnose --provider firebird --connection "DataSource=localhost;Database=C:\firebird\data\mydb.fdb;User=SYSDBA;Password=masterkey;"
```

### Testing

Currently, this project does not have automated tests configured. When adding tests:

```bash
# Run all tests (when test projects exist)
dotnet test

# Run tests with detailed output
dotnet test -v normal

# Run specific tests by filter
dotnet test --filter "TestMethodName"
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

3. **Core Services**:
   - `DatabaseExporter`: Handles the export of database schema and data
   - `DatabaseImporter`: Handles the import of database schema and data
   - `PerformanceOptimizer`: Optimizes performance for large datasets
   - `DirectTransferUtility`: Handles direct database-to-database transfers
   - `EmergencyImporter`: Recovery utility for importing from batch files

4. **Command-Line Processing**:
   - Uses `CommandLineParser` package to process command arguments
   - Supports multiple verbs: `providers`, `export`, `import`, `schema`, etc.
   - Each command has its own options class with proper help texts

5. **Serialization & Compression**:
   - Uses `MessagePack` for efficient binary serialization
   - Applies compression (`GZip` and `BZip2`) to reduce output file sizes

### Data Flow

1. **Export Process**:
   - Discover database schema (tables, columns, indexes, etc.)
   - Calculate dependency ordering to handle foreign key relationships
   - Export metadata to a compressed binary file
   - Export table data to individual compressed files, with optional filtering

2. **Import Process**:
   - Read metadata to understand the database structure
   - Create schema in the target database (if enabled)
   - Import data in the correct dependency order
   - Create foreign keys after data import (if enabled)

3. **Direct Transfer Process**:
   - Connect to both source and target databases simultaneously
   - Calculate dependency ordering
   - Transfer data directly between databases without intermediate files

### Performance Considerations

- Uses batched operations for large datasets
- Implements parallel processing where appropriate
- Applies compression to reduce file sizes
- Uses `WITH (NOLOCK)` hint for SQL Server reads to avoid blocking
- Configurable batch sizes for both export and import

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

4. **Error Handling**:
   - Log detailed errors to help diagnose issues
   - Include inner exception details where appropriate
   - Use the `ContinueOnError` flag to control error behavior during import operations

5. **Testing**:
   - Currently no test framework is configured
   - When adding tests, consider using xUnit with ITestOutputHelper for logging
   - Use descriptive test names following the pattern `[Class]_Should[ExpectedBehavior]`

6. **Command Handling**:
   - Each command implements a static `Execute` method
   - Command parameters are defined using the `CommandLineParser` attribute system
   - Return appropriate exit codes (0 for success, non-zero for failures)

7. **Serialization Strategy**:
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

Firebird requires specific DLL files to be deployed with the application:
- `fbembed.dll`, `fbclient.dll`, `fbintl.dll` (Firebird libraries)
- `icudt30.dll`, `icuin30.dll`, `icuuc30.dll` (Unicode/internationalization support)
- `msvcp80.dll`, `msvcr80.dll` (Visual C++ runtime)
- Configuration files: `firebird.conf`, `fbintl.conf`, `firebird.msg`

**Deployment Structure**:
- All DLL files are copied to the application root directory for runtime access
- Configuration files are maintained in the `firebird\` subdirectory
- The project uses an MSBuild target to automatically copy DLLs from `firebird\` source folder to root output
- Single source location in `firebird\` directory prevents file duplication in project

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

### User Interface Improvements
- **Table Selection System**: Added comprehensive table browsing with search, multi-select, and caching
- **Connection Defaults**: Improved initialization of default values for all database providers  
- **Authentication Handling**: Fixed SQL Server Windows Authentication defaults and field states
- **Project Structure**: Cleaned up Firebird DLL deployment to use single source location

### Performance Optimizations
- **Intelligent Caching**: Table lists are cached per connection to avoid repeated database queries
- **Smart Cache Invalidation**: Cache automatically refreshes only when connection details change
- **Instant Table Loading**: Subsequent table browsing operations load instantly from cache

### Developer Experience
- **Cleaner Project File**: Simplified Firebird component deployment using MSBuild targets
- **Better Documentation**: Enhanced CLAUDE.md with comprehensive UI behavior documentation
- **Consistent Patterns**: Standardized connection handling across all database providers