# Database Migration Tool - User Guide

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [User Interface Guide](#user-interface-guide)
- [Database Operations](#database-operations)
- [Configuration Management](#configuration-management)
- [Command Line Usage](#command-line-usage)
- [Troubleshooting](#troubleshooting)
- [Best Practices](#best-practices)

## Overview

The Database Migration Tool is a powerful application designed to export, import, and analyze database schemas and data across different database systems. It supports SQL Server, MySQL, PostgreSQL, and Firebird databases with high performance and reliability.

### Key Features

- **Cross-Database Migration**: Export from one database type and import to another
- **Granular Control**: Select specific tables, apply filtering criteria, and customize operations
- **Professional UI**: Intuitive graphical interface with connection profiles and table browsers
- **Command Line Support**: Full automation capabilities for scripting and CI/CD
- **Data Integrity**: Comprehensive validation and recovery features
- **Performance Optimized**: Batch processing and compression for large datasets

### Supported Database Systems

- **Microsoft SQL Server** (2012 and later)
- **MySQL** (5.7 and later)
- **PostgreSQL** (10 and later)
- **Firebird** (2.5 and 3.0+)

## Getting Started

### System Requirements

- **Operating System**: Windows 10 or later
- **.NET Runtime**: Included with the application (self-contained)
- **Architecture**: x86 compatible
- **Memory**: 2GB RAM minimum, 4GB recommended for large datasets
- **Storage**: 100MB for application, additional space for export files

### Installation

1. **Download**: Extract the application files to your preferred directory
2. **First Run**: Launch `DatabaseMigrationTool.exe` to open the graphical interface
3. **Dependencies**: All required components are included (no additional installation needed)

### Quick Start Example

1. **Open the Application**: Double-click `DatabaseMigrationTool.exe`
2. **Configure Source Connection**: In the Export tab, select your database provider and enter connection details
3. **Select Output Directory**: Choose where to save the exported data
4. **Start Export**: Click "Start Export" to begin the process
5. **Configure Target Connection**: Switch to Import tab and configure your target database
6. **Select Input Directory**: Choose the export directory from step 3
7. **Start Import**: Click "Start Import" to complete the migration

## User Interface Guide

### Main Window Layout

The application features a tabbed interface with three main sections:

#### Export Tab
- **Connection Settings**: Configure source database connection
- **Table Selection**: Choose which tables to export
- **Output Settings**: Set export directory and options
- **Filtering Options**: Apply criteria files for selective data export
- **Progress Monitoring**: Real-time progress updates during export

#### Import Tab
- **Connection Settings**: Configure target database connection
- **Input Settings**: Select directory containing exported data
- **Table Selection**: Choose which tables to import
- **Schema Options**: Control schema creation and foreign key handling
- **Progress Monitoring**: Real-time progress updates during import

#### Schema Tab
- **Connection Settings**: Configure database connection for analysis
- **Table Selection**: Choose tables to analyze
- **View Options**: Control detail level and output format
- **Script Generation**: Create SQL scripts for table structures

### Connection Management

#### Database Providers

**SQL Server Configuration**:
- Server name (default: LocalHost)
- Authentication: Windows or SQL Server
- Database name
- Connection options (Trust Server Certificate enabled by default)

**MySQL Configuration**:
- Server and port (default: LocalHost:3306)
- Username and password
- Database name
- SSL/TLS options

**PostgreSQL Configuration**:
- Server and port (default: LocalHost:5432)
- Username and password
- Database name
- SSL connection (enabled by default)

**Firebird Configuration**:
- Database file path with browse button
- Version selection (2.5 or 3.0+)
- Username (default: SYSDBA)
- Password (default provided, can be overridden)
- Read-only mode option

#### Connection Profiles

**Profile Management Features**:
- **Save Profiles**: Store connection settings for reuse
- **Profile Manager**: Organize, edit, and delete saved profiles
- **Cross-Tab Sync**: Apply profiles across Export, Import, and Schema tabs
- **Encryption**: Passwords are securely encrypted
- **Import/Export**: Share profile configurations (without passwords)

**Using Profiles**:
1. Configure your connection settings
2. Click the Save Profile button (💾)
3. Enter a profile name and description
4. Select the profile from the dropdown in any tab
5. Profile automatically loads across all tabs

### Table Selection

#### Table Browser
- **Browse Button**: Opens table selection window
- **Search/Filter**: Quickly find specific tables
- **Multi-Select**: Choose multiple tables with checkboxes
- **Select All/None**: Bulk selection options
- **Schema Display**: Shows tables with schema prefixes
- **Performance Cache**: Tables are cached to improve browsing speed

#### Manual Entry
- **Comma-Separated**: Enter table names separated by commas
- **Schema Qualified**: Use "schema.table" format when needed
- **Clear Button**: Quickly remove all selections

### Progress Monitoring

#### Real-Time Updates
- **Progress Bar**: Visual indication of completion percentage
- **Status Messages**: Detailed information about current operations
- **Table Progress**: Individual table processing status
- **Error Reporting**: Immediate notification of issues

#### Success Handling
- **Completion Notification**: Clear indication when operations finish
- **Progress Cleanup**: Progress bars automatically clear after success
- **Success Delay**: 2-second display of completion status

## Database Operations

### Export Operations

#### Basic Export
1. **Configure Connection**: Select provider and enter connection details
2. **Test Connection**: Verify database connectivity
3. **Select Tables**: Use browser or manual entry
4. **Choose Output**: Select directory for export files
5. **Set Options**: Configure batch size and schema-only mode
6. **Start Export**: Begin the export process

#### Advanced Export Options

**Table Filtering**:
- **Criteria Files**: JSON files with WHERE clauses for selective data export
- **Criteria Helper**: Built-in tool for creating filter conditions
- **Table-Specific**: Different criteria for each table

**Performance Tuning**:
- **Batch Size**: Adjust based on available memory (default: 100,000 rows)
- **Schema Only**: Export structure without data for faster operations
- **Compression**: Automatic compression reduces file sizes

#### Export File Structure
```
export_directory/
├── export_manifest.json          # Export overview and table list
├── dependencies.json             # Foreign key relationships
├── table_metadata/               # Individual table schemas
│   ├── dbo_Customers.meta       # Compressed table structure
│   └── dbo_Orders.meta
├── data/                        # Table data files
│   ├── dbo_Customers.bin        # Compressed data
│   ├── dbo_Orders_batch0.bin    # Large table batches
│   └── dbo_Orders_batch1.bin
└── export_log.txt              # Operation log
```

### Import Operations

#### Basic Import
1. **Select Export Directory**: Choose directory containing exported data
2. **Configure Target Connection**: Select provider and enter connection details
3. **Test Connection**: Verify database connectivity
4. **Select Tables**: Choose which tables to import (from export data)
5. **Set Options**: Configure schema creation and error handling
6. **Start Import**: Begin the import process

#### Advanced Import Options

**Schema Handling**:
- **Create Schema**: Automatically create tables and indexes
- **Foreign Keys**: Control foreign key creation timing
- **Schema Only**: Import structure without data

**Error Management**:
- **Continue on Error**: Skip failed operations and continue
- **Validation**: Check for existing data conflicts
- **Recovery**: Resume interrupted imports

#### Import Validation
- **Conflict Detection**: Identifies existing tables that would be affected
- **Overwrite Confirmation**: User approval required for data overwrites
- **Dependency Analysis**: Ensures proper import order for foreign keys

### Schema Operations

#### Schema Analysis
1. **Configure Connection**: Select database to analyze
2. **Select Tables**: Choose tables for analysis (optional - defaults to all)
3. **Set Detail Level**: Choose verbose output for comprehensive information
4. **View Schema**: Display table structures in dedicated window
5. **Generate Scripts**: Optionally create SQL scripts for table creation

#### Schema Viewer Features
- **Table Structure**: Columns, data types, and constraints
- **Indexes**: Primary keys, unique constraints, and indexes
- **Foreign Keys**: Relationship information and dependencies
- **Statistics**: Row counts and table metadata
- **Export Options**: Save schema information to files

## Configuration Management

### Configuration Files

Configuration files enable automation and team collaboration by saving operation parameters in JSON format.

#### Creating Configurations
1. **GUI Method**: Use Save/Load Configuration buttons in the application
2. **Command Line**: Generate sample configurations with `--create-sample`
3. **Manual Creation**: Create JSON files following the documented structure

#### Configuration Structure
```json
{
  "name": "Production Migration",
  "description": "Daily sync from SQL Server to MySQL",
  "version": "2.0",
  "export": {
    "provider": "SqlServer",
    "connectionString": "Server=prod-sql;Database=MainDB;...",
    "outputPath": "./exports",
    "tables": "Customers,Orders,OrderDetails",
    "batchSize": 50000,
    "schemaOnly": false
  },
  "import": {
    "provider": "MySQL",
    "connectionString": "Server=target-mysql;Database=MainDB;...",
    "inputPath": "./exports",
    "createSchema": true,
    "createForeignKeys": true,
    "continueOnError": false
  }
}
```

#### Using Configurations
- **Command Line**: `DatabaseMigrationTool.exe export --config myconfig.json`
- **GUI**: Load Configuration button in the application
- **Override**: Command-line parameters override configuration file settings
- **Validation**: Built-in validation ensures configuration integrity

### User Settings

The application automatically saves user preferences including:
- **Window Size/Position**: Restore window layout on startup
- **Default Values**: Batch sizes, schema options, and provider preferences
- **Recent Items**: Recently used directories, files, and connections
- **Performance Settings**: Timeout values and retry policies
- **UI Preferences**: Last selected tabs and display options

Settings are stored in: `%APPDATA%/DatabaseMigrationTool/settings.json`

## Command Line Usage

The Database Migration Tool supports comprehensive command-line operations for automation and scripting.

### Basic Commands

#### List Providers
```bash
DatabaseMigrationTool.exe providers
```

#### Export Database
```bash
DatabaseMigrationTool.exe export ^
  --provider sqlserver ^
  --connection "Server=localhost;Database=SourceDB;Integrated Security=true;" ^
  --output "./export_output"
```

#### Import Database
```bash
DatabaseMigrationTool.exe import ^
  --provider mysql ^
  --connection "Server=localhost;Database=TargetDB;User=root;Password=pass;" ^
  --input "./export_output"
```

#### View Schema
```bash
DatabaseMigrationTool.exe schema ^
  --provider postgresql ^
  --connection "Host=localhost;Database=mydb;Username=postgres;Password=pass;" ^
  --verbose
```

### Advanced Commands

#### Selective Export
```bash
DatabaseMigrationTool.exe export ^
  --provider sqlserver ^
  --connection "Server=localhost;Database=SourceDB;Integrated Security=true;" ^
  --output "./export_output" ^
  --tables "Customers,Orders,OrderDetails" ^
  --batch-size 25000
```

#### Filtered Export
```bash
DatabaseMigrationTool.exe export ^
  --provider sqlserver ^
  --connection "Server=localhost;Database=SourceDB;Integrated Security=true;" ^
  --output "./export_output" ^
  --criteria "./filter_criteria.json"
```

#### Schema-Only Operations
```bash
# Export schema only
DatabaseMigrationTool.exe export ^
  --provider sqlserver ^
  --connection "..." ^
  --output "./schema_export" ^
  --schema-only

# Import schema only
DatabaseMigrationTool.exe import ^
  --provider mysql ^
  --connection "..." ^
  --input "./schema_export" ^
  --schema-only
```

#### Direct Transfer
```bash
DatabaseMigrationTool.exe direct-transfer ^
  --source-provider sqlserver ^
  --source-connection "Server=source;Database=SourceDB;..." ^
  --target-provider mysql ^
  --target-connection "Server=target;Database=TargetDB;..." ^
  --tables "Customers,Orders"
```

### Configuration-Based Commands

#### Use Configuration File
```bash
DatabaseMigrationTool.exe export --config "./configs/production.json"
```

#### Override Configuration
```bash
DatabaseMigrationTool.exe export ^
  --config "./configs/production.json" ^
  --output "./different_output" ^
  --batch-size 75000
```

#### Create Sample Configuration
```bash
DatabaseMigrationTool.exe config --create-sample "./my_config.json"
```

#### Validate Configuration
```bash
DatabaseMigrationTool.exe config --validate "./my_config.json"
```

### Utility Commands

#### Emergency Import
```bash
DatabaseMigrationTool.exe emergency-import ^
  --input "./export_output" ^
  --provider mysql ^
  --connection "Server=localhost;Database=RecoveryDB;..."
```

#### File Inspection
```bash
DatabaseMigrationTool.exe dump-file ^
  --file "./export_output/table_metadata/dbo_Customers.meta"
```

#### Diagnostic Analysis
```bash
DatabaseMigrationTool.exe diagnose ^
  --provider firebird ^
  --connection "DataSource=localhost;Database=C:\data\mydb.fdb;..."
```

## Troubleshooting

### Common Issues

#### Connection Problems

**"Cannot connect to database"**:
- Verify server name and port numbers
- Check username and password
- Ensure database service is running
- Test network connectivity
- Review firewall settings

**"Login failed for user"**:
- Verify credentials are correct
- Check user permissions on target database
- For SQL Server: Ensure mixed mode authentication is enabled
- For MySQL: Verify user has required privileges

#### Export Issues

**"Access denied" or "Permission denied"**:
- Ensure user has SELECT permissions on all tables
- For SQL Server: Consider using WITH (NOLOCK) hint
- Check file system permissions for output directory
- Run application as administrator if necessary

**"Out of memory" errors**:
- Reduce batch size in export settings
- Close other applications to free memory
- Consider exporting tables individually
- Use 64-bit system for large datasets

**"Table not found" errors**:
- Verify table names are correct (case-sensitive on some systems)
- Use schema-qualified names (e.g., "dbo.TableName")
- Check if tables exist in the selected database
- Ensure proper permissions to access tables

#### Import Issues

**"Table already exists"**:
- Use overwrite confirmation dialogs
- Consider dropping existing tables first
- Use schema-only import to update structure only
- Check import options for handling existing data

**"Foreign key constraint violations"**:
- Ensure data integrity in source database
- Consider importing without foreign keys initially
- Check dependency order in import process
- Verify referenced tables are imported first

**"Data type conversion errors"**:
- Review data type mappings between providers
- Check for incompatible data in source tables
- Consider data transformation before import
- Use continue-on-error option for non-critical issues

### Performance Optimization

#### Large Database Handling

**Optimize Batch Sizes**:
- Start with default 100,000 rows per batch
- Increase for systems with more memory
- Decrease if experiencing memory issues
- Monitor system resources during operations

**Memory Management**:
- Close unnecessary applications
- Use 64-bit operating system for large datasets
- Consider breaking large operations into smaller chunks
- Monitor disk space for export files

**Network Considerations**:
- Use local connections when possible
- Consider network bandwidth for remote databases
- Use compression to reduce data transfer
- Schedule operations during off-peak hours

#### Database-Specific Tips

**SQL Server**:
- Use Windows Authentication when possible
- Enable Trust Server Certificate for SSL
- Consider using NOLOCK hint for read operations
- Monitor transaction log growth during operations

**MySQL**:
- Increase max_allowed_packet for large data
- Configure appropriate timeout values
- Use SSL connections for security
- Consider MySQL-specific connection options

**PostgreSQL**:
- Enable SSL connections by default
- Configure appropriate work_mem settings
- Consider connection pooling for multiple operations
- Monitor PostgreSQL logs for performance issues

**Firebird**:
- Ensure correct Firebird version selection
- Use embedded mode for single-user scenarios
- Consider page size and cache settings
- Monitor Firebird server performance

### Error Recovery

#### Resume Failed Operations

**Export Recovery**:
- Use the Resume Operations feature in GUI
- Check export logs for specific error details
- Consider partial exports of remaining tables
- Verify disk space and permissions

**Import Recovery**:
- Use Emergency Import for critical recovery
- Check dependency order for failed imports
- Consider importing tables individually
- Verify target database state before retry

#### Data Validation

**Verify Migration Success**:
- Compare row counts between source and target
- Spot-check critical data values
- Verify foreign key relationships
- Test application functionality with migrated data

**Troubleshooting Tools**:
- Use Schema view to compare structures
- Check export logs for warnings or errors
- Use diagnostic commands for connection issues
- Monitor database performance during operations

## Best Practices

### Pre-Migration Planning

#### Assessment Phase
1. **Inventory Source Database**: Document all tables, views, procedures, and dependencies
2. **Analyze Data Volume**: Estimate export file sizes and transfer times
3. **Test Connectivity**: Verify connections to both source and target systems
4. **Plan Downtime**: Schedule migrations during maintenance windows
5. **Backup Strategy**: Create full backups before starting migration

#### Environment Preparation
- **Test Environment**: Perform full migration test in non-production environment
- **Network Resources**: Ensure adequate bandwidth for data transfer
- **Disk Space**: Allocate sufficient space for export files (2-3x data size)
- **System Resources**: Monitor CPU and memory during test runs
- **Security**: Review connection permissions and credential management

### Migration Execution

#### Export Best Practices
1. **Start Small**: Begin with smaller tables to verify process
2. **Use Filters**: Apply table criteria to reduce data volume when appropriate
3. **Monitor Progress**: Watch for errors or performance issues
4. **Validate Exports**: Check export manifests and file completeness
5. **Document Process**: Keep logs and notes for future reference

#### Import Best Practices
1. **Schema First**: Consider importing schema before data
2. **Dependency Order**: Respect foreign key relationships during import
3. **Batch Processing**: Use appropriate batch sizes for target system
4. **Error Handling**: Plan for handling data conversion issues
5. **Validation**: Verify data integrity after import completion

### Data Integrity

#### Validation Strategies
- **Row Count Verification**: Compare source and target table counts
- **Sample Data Checks**: Verify critical data values and calculations
- **Referential Integrity**: Test foreign key relationships
- **Application Testing**: Validate application functionality with migrated data
- **Performance Testing**: Ensure acceptable performance in target system

#### Quality Assurance
- **Automated Testing**: Use scripts to validate migration results
- **Manual Verification**: Review critical business data manually
- **Rollback Planning**: Maintain ability to revert to original state
- **Documentation**: Record all validation steps and results
- **Sign-off Process**: Obtain stakeholder approval before go-live

### Security Considerations

#### Connection Security
- **Encrypted Connections**: Use SSL/TLS when available
- **Credential Management**: Store passwords securely using profiles
- **Least Privilege**: Use database accounts with minimal required permissions
- **Network Security**: Consider VPN or private networks for remote connections
- **Audit Trail**: Maintain logs of all migration activities

#### Data Protection
- **Sensitive Data**: Consider masking or encryption for sensitive information
- **Compliance**: Ensure migration meets regulatory requirements
- **Access Control**: Limit access to migration tools and processes
- **Data Retention**: Plan for secure disposal of temporary export files
- **Monitoring**: Track and log all data access during migration

### Performance Optimization

#### System Tuning
- **Database Configuration**: Optimize source and target database settings
- **Hardware Resources**: Ensure adequate CPU, memory, and disk I/O
- **Network Optimization**: Use high-speed connections for large transfers
- **Parallel Processing**: Consider multiple simultaneous table migrations
- **Compression**: Enable compression to reduce transfer times

#### Monitoring and Maintenance
- **Resource Monitoring**: Track CPU, memory, and disk usage during operations
- **Progress Tracking**: Monitor migration progress and estimated completion
- **Error Detection**: Set up alerts for migration failures or issues
- **Performance Baselines**: Establish performance expectations for future migrations
- **Continuous Improvement**: Document lessons learned for future projects

### Automation and Scripting

#### Configuration Management
- **Standardized Configs**: Create reusable configuration templates
- **Environment-Specific**: Maintain separate configs for dev/test/production
- **Version Control**: Store configurations in source control systems
- **Validation**: Implement automated configuration validation
- **Documentation**: Document configuration parameters and their purposes

#### Operational Procedures
- **Automated Scheduling**: Use task schedulers for regular migrations
- **Error Notification**: Implement alerts for migration failures
- **Logging Strategy**: Maintain comprehensive logs for troubleshooting
- **Monitoring Integration**: Connect with existing monitoring systems
- **Recovery Procedures**: Document and test recovery processes

---

## Support and Resources

### Getting Help
- Review this user guide for common scenarios and solutions
- Check the troubleshooting section for specific error messages
- Use diagnostic commands to identify connection or configuration issues
- Consult database vendor documentation for provider-specific issues

### Additional Resources
- **Configuration Examples**: Sample configuration files for common scenarios
- **Command Reference**: Complete list of command-line options and parameters
- **API Documentation**: Technical details for developers and integrators
- **Release Notes**: Information about new features and bug fixes

### Best Practice Guidelines
- Test all migrations in non-production environments first
- Maintain backups of all source and target databases
- Document migration procedures for future reference
- Validate data integrity after each migration
- Plan for rollback procedures in case of issues

---

*Database Migration Tool - Professional database migration and analysis solution*