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

## Quick Start Commands

### Building and Testing
```bash
# Build the main application
dotnet build src/DatabaseMigrationTool/DatabaseMigrationTool.csproj

# Build entire solution
dotnet build DatabaseMigrationTool.sln

# Run application in GUI mode (default)
dotnet run --project src/DatabaseMigrationTool

# Run with command line interface
dotnet run --project src/DatabaseMigrationTool -- --console

# Run specific command (e.g., list providers)
dotnet run --project src/DatabaseMigrationTool -- providers

# Build single executable for distribution
.\build-single-exe.ps1
```

### Common Development Tasks
```bash
# Export database
dotnet run --project src/DatabaseMigrationTool -- export --provider firebird --connection "Database=test.fdb;User=SYSDBA;Password=pass;" --output ./export

# Import database
dotnet run --project src/DatabaseMigrationTool -- import --provider firebird --connection "Database=test.fdb;User=SYSDBA;Password=pass;" --input ./export

# Test Firebird connection
dotnet run --project src/DatabaseMigrationTool -- test-firebird --connection "Database=test.fdb;User=SYSDBA;Password=pass;"

# View database schema
dotnet run --project src/DatabaseMigrationTool -- schema --provider firebird --connection "Database=test.fdb;User=SYSDBA;Password=pass;"

# Build distribution
.\build-single-exe.ps1
```

## Project Structure

- `src/DatabaseMigrationTool/` - Main WPF application with CLI support
  - `Commands/` - CLI command handlers
  - `Providers/` - Database provider implementations (Firebird, SQL Server, MySQL, PostgreSQL)
  - `Services/` - Core business logic services
  - `Views/` - WPF UI components and dialogs
  - `Models/` - Data models and schemas
  - `Utilities/` - Helper classes and utilities
  - `FirebirdDlls/` - Firebird client libraries for v2.5 and v5.0+
- `src/FirebirdTest/` - Standalone Firebird connection testing utility
- `installer/` - WiX installer configuration for MSI distribution
- `docs/` - Comprehensive documentation

## Key Architecture Patterns

### Provider System
All database providers implement `IDatabaseProvider` interface:
- `FirebirdProvider` - Firebird 2.5 and 5.0+ support with automatic version detection
- `SqlServerProvider` - Microsoft SQL Server
- `MySqlProvider` - MySQL/MariaDB
- `PostgreSqlProvider` - PostgreSQL

### Service Layer
Core services handle the main application logic:
- `DatabaseExporter` - Schema and data export
- `DatabaseImporter` - Schema and data import with overwrite detection
- `TableImporter` - Individual table data import with provider-specific SQL generation

### Security & Performance
- SQL injection prevention through identifier validation
- Resource leak prevention with proper disposal patterns
- Async/await with ConfigureAwait(false) throughout
- Connection pooling and timeout management

## Component Documentation

See additional documentation files:
- `docs/FIREBIRD.md` - Firebird provider specifics, version compatibility, and recent improvements
- `docs/OVERWRITE_HANDLING.md` - Import/export overwrite detection and conflict resolution
- `docs/ARCHITECTURE.md` - Detailed system architecture
- `docs/DISTRIBUTION.md` - Build and deployment processes
- `USER_GUIDE.md` - End user documentation

## Development Workflow

1. **Feature Development**: Create feature branches, implement functionality
2. **Testing**: Manual testing with real databases (no automated test suite currently)
3. **Building**: Ensure 0 warnings with `dotnet build` - treat warnings as errors
4. **Distribution**: Test with `.\build-single-exe.ps1` before release
5. **Debugging**: Use the FirebirdTest utility for isolated Firebird connection testing

## Recent Improvements

### Firebird Provider Enhancements
- **Fixed Connection Locking**: Resolved "lock conflict on no wait transaction" errors by optimizing connection management
- **Cross-Database Compatibility**: Enhanced SQL Server to Firebird import with comprehensive type mapping
- **Table Filtering**: Fixed table filtering bugs in both export and import operations
- **Schema Awareness**: Improved handling of different schema naming conventions (dbo vs SYSDB)

### UI and UX Improvements
- **Overwrite Detection**: Implemented schema-aware overwrite checking for both export and import
- **Progress Reporting**: Enhanced batch processing progress with detailed file-by-file reporting
- **Error Handling**: Improved error messages and recovery options for failed operations
- **Logging Consistency**: Standardized timestamped logging across all operations

### Reliability and Performance
- **Connection Pooling**: Disabled for Firebird to prevent metadata lock conflicts
- **Transaction Isolation**: Optimized isolation levels for better concurrency
- **Batch Processing**: Improved handling of large datasets with batched file processing
- **Error Recovery**: Enhanced rollback and cleanup procedures for failed operations

## Important Notes

- **Firebird Compatibility**: Tool supports both Firebird 2.5 (embedded) and 5.0+ (client/server)
- **Cross-Database Migration**: Can export from one database provider and import to another with automatic type conversion
- **Connection Management**: Firebird operations use optimized connection handling to prevent locking issues
- **Security**: Never commit connection strings or sensitive data
- **Performance**: Use table filtering and batched operations for large datasets