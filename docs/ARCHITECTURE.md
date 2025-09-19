# System Architecture

## Overview

The Database Migration Tool follows a clean architecture pattern with clear separation of concerns across providers, services, and UI layers.

## Core Components

### Provider System

#### IDatabaseProvider Interface
All database providers implement a common interface providing:
- Connection management and testing
- Schema discovery and creation
- Data export/import operations
- Provider-specific SQL generation

#### Supported Providers
- **FirebirdProvider**: Firebird 2.5 and 5.0+ with automatic version detection
- **SqlServerProvider**: Microsoft SQL Server with Windows/SQL authentication
- **MySqlProvider**: MySQL and MariaDB databases
- **PostgreSqlProvider**: PostgreSQL databases

#### Provider Factory
`DatabaseProviderFactory` creates provider instances based on provider name and handles provider discovery.

### Service Layer

#### Export Services
- **DatabaseExporter**: Main export orchestration
- **ExportService**: Schema and data export logic
- **StreamingDataReader**: Memory-efficient data reading for large tables

#### Import Services
- **DatabaseImporter**: Main import orchestration with overwrite detection
- **ImportService**: Schema and data import logic
- **TableImporter**: Individual table data import with provider-specific SQL generation
- **DirectImporter**: Direct database-to-database transfers

#### Utility Services
- **MetadataManager**: Granular export metadata management (JSON manifests + .meta files)
- **ConnectionManager**: Database connection management and pooling
- **ValidationService**: Data integrity and schema validation
- **OperationStateManager**: Import/export operation state tracking

### Data Models

#### Schema Models
- **TableSchema**: Complete table definition (columns, indexes, constraints, FKs)
- **ColumnDefinition**: Column properties (name, type, nullability, identity)
- **IndexDefinition**: Index structures and properties
- **ForeignKeyDefinition**: Foreign key relationships
- **ConstraintDefinition**: Check and unique constraints

#### Export/Import Models
- **DatabaseExport**: Top-level export container
- **TableData**: Serialized table data with compression
- **RowData**: Individual row data as key-value pairs
- **ExportManifest**: Lightweight export overview
- **DependencyManifest**: Foreign key dependencies for import ordering

#### Configuration Models
- **ConnectionProfile**: Saved connection configurations with encrypted passwords
- **UserSettings**: Application preferences and defaults
- **MigrationConfiguration**: Export/import operation settings

### UI Layer (WPF)

#### Main Components
- **MainWindow**: Primary application interface with tabbed layout
- **ConnectionStringControl**: Unified connection configuration across all tabs
- **TableSelectionWindow**: Multi-select table browser with search and caching
- **SchemaViewWindow**: Database schema visualization

#### Specialized Windows
- **ExportOverwriteDialog**: Table-specific overwrite confirmation
- **ImportOverwriteDialog**: Import conflict resolution
- **ImportTableSelectionWindow**: Select tables from export metadata
- **RecoveryWindow**: Operation recovery and state management

## File Structure and Export Format

### Granular Metadata System
- **export_manifest.json**: Lightweight export overview with table list and timestamps
- **dependencies.json**: Foreign key dependencies and import ordering
- **table_metadata/[schema]_[table].meta**: Individual BZip2-compressed MessagePack table schemas

### Data Files
- **data/[schema]_[table].bin**: Single compressed data file (MessagePack + GZip)
- **data/[schema]_[table]_batch[N].bin**: Multiple batch files for large tables
- **data/[schema]_[table].info**: Table export statistics and metadata

### Benefits of Granular Structure
- **Incremental Operations**: Add tables without affecting existing exports
- **Selective Overwrites**: Only remove conflicting table files
- **Parallel Processing**: Table operations can be parallelized
- **Granular Recovery**: Individual table failures don't affect entire export

## Security Architecture

### SQL Injection Prevention
- **Identifier Validation**: Strict character set validation for SQL identifiers
- **Length Limits**: 128-character maximum for identifiers
- **Parameterized Queries**: Use parameters for all user input
- **Provider-Specific Escaping**: Each provider handles identifier escaping appropriately

### Resource Management
- **Connection Disposal**: Proper try-finally patterns ensure connections are disposed
- **Memory Management**: IDisposable implementations throughout
- **Stream Cleanup**: All file operations use proper resource disposal
- **Exception Safety**: Resource cleanup guaranteed even on exceptions

### Credential Protection
- **Profile Encryption**: Connection passwords encrypted using AES with machine keys
- **No Plaintext Storage**: Credentials never stored in plaintext
- **Memory Clearing**: Sensitive data cleared from memory when possible

## Performance Architecture

### Batch Processing
- **Configurable Batch Sizes**: Optimize for different data volumes and systems
- **Memory-Efficient Streaming**: Process large datasets without loading into memory
- **Parallel Table Operations**: Multiple tables can be processed simultaneously
- **Progress Reporting**: Real-time progress updates during long operations

### Caching System
- **Table List Caching**: Cache database table lists per connection
- **Smart Invalidation**: Cache refreshes only when connection changes
- **Cross-Tab Caching**: Shared cache across Export/Import/Schema tabs
- **Connection Profile Caching**: Frequently used profiles cached in memory

### Async Patterns
- **ConfigureAwait(false)**: Used throughout to prevent deadlocks
- **Cancellation Support**: Operations can be cancelled gracefully
- **Non-blocking UI**: Long operations don't freeze the user interface
- **Background Processing**: Heavy operations run on background threads

## Error Handling and Recovery

### Operation State Management
- **State Persistence**: Operation progress saved to disk
- **Automatic Recovery**: Resume interrupted operations
- **Error Classification**: Distinguish between recoverable and fatal errors
- **Rollback Capability**: Undo partial operations when possible

### Logging and Diagnostics
- **Structured Logging**: Consistent logging format across all components
- **Error Categorization**: Errors classified by severity and recoverability
- **Diagnostic Commands**: Built-in commands for troubleshooting connections
- **Performance Metrics**: Track operation timing and resource usage

## Extension Points

### Adding New Database Providers
1. Implement `IDatabaseProvider` interface
2. Add provider-specific connection string handling
3. Implement schema discovery methods
4. Add SQL generation for CREATE/INSERT statements
5. Register provider with `DatabaseProviderFactory`

### Custom Export/Import Formats
- Extend `MetadataManager` for new metadata formats
- Implement custom serialization in `TableData` model
- Add format-specific options to export/import services

### UI Customization
- WPF controls are modular and can be extended
- Add new tabs by extending `MainWindow`
- Custom dialogs can be integrated into existing workflows