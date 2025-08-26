# Database Migration Tool

A high-performance utility for exporting and importing database schemas and data between different database systems.

## Features

- **Schema Discovery**: Automatically discovers database schema including tables, columns, indexes, foreign keys, and constraints
- **Data Export with Filtering**: Export data with optional filtering criteria by table
- **Schema Migration**: Export schema structures for recreation in target databases
- **High Performance**: Optimized for large datasets with parallel processing and batching
- **Cross-Database Support**: Works with SQL Server, MySQL, PostgreSQL, and Firebird
- **Data Compression**: Uses efficient binary serialization and compression for transport

## Usage

### List Available Database Providers

```bash
dotnet run -- providers
```

### Export a Database

```bash
dotnet run -- export \
  --provider sqlserver \
  --connection "Server=localhost;Database=SourceDB;User Id=sa;Password=P@ssw0rd;TrustServerCertificate=True;" \
  --output ./export_output
```

To export only specific tables:

```bash
dotnet run -- export \
  --provider sqlserver \
  --connection "Server=localhost;Database=SourceDB;User Id=sa;Password=P@ssw0rd;TrustServerCertificate=True;" \
  --output ./export_output \
  --tables "Customers,Orders,OrderDetails"
```

To use filtering criteria:

```bash
# Create a criteria JSON file
echo '{
  "Customers": "Country = 'USA'",
  "Orders": "OrderDate > '2022-01-01'"
}' > criteria.json

# Export with criteria
dotnet run -- export \
  --provider sqlserver \
  --connection "Server=localhost;Database=SourceDB;User Id=sa;Password=P@ssw0rd;TrustServerCertificate=True;" \
  --output ./export_output \
  --criteria criteria.json
```

### Import a Database

```bash
dotnet run -- import \
  --provider mysql \
  --connection "Server=localhost;Database=TargetDB;User=root;Password=password;" \
  --input ./export_output
```

Options:

- `--no-create-schema`: Skip schema creation
- `--no-foreign-keys`: Skip foreign key creation  
- `--schema-only`: Import schema only, not data
- `--continue-on-error`: Continue processing on errors

### View Database Schema

```bash
dotnet run -- schema \
  --provider postgresql \
  --connection "Host=localhost;Database=mydb;Username=postgres;Password=password;" \
  --verbose
```

To generate SQL scripts:

```bash
dotnet run -- schema \
  --provider postgresql \
  --connection "Host=localhost;Database=mydb;Username=postgres;Password=password;" \
  --script \
  --script-path ./schema_scripts
```

### Example using Firebird

```bash
dotnet run -- export \
  --provider firebird \
  --connection "DataSource=localhost;Database=C:\firebird\data\mydb.fdb;User=SYSDBA;Password=masterkey;" \
  --output ./export_output
```

## Parameters

### Global Parameters

- `--provider`: Database provider (sqlserver, mysql, postgresql, firebird)
- `--connection`: Connection string for the database

### Export Parameters

- `--output`: Output directory path
- `--tables`: Comma-separated list of tables to export (default: all tables)
- `--criteria`: JSON file containing table criteria as key-value pairs
- `--batch-size`: Number of rows to process in a single batch (default: 10000)
- `--schema-only`: Export schema only, no data

### Import Parameters

- `--input`: Input directory path
- `--tables`: Comma-separated list of tables to import (default: all tables)
- `--batch-size`: Number of rows to process in a single batch (default: 1000)
- `--no-create-schema`: Skip schema creation
- `--no-foreign-keys`: Skip foreign key creation
- `--schema-only`: Import schema only, no data
- `--continue-on-error`: Continue processing on error

### Schema Parameters

- `--tables`: Comma-separated list of tables to view (default: all tables)
- `--verbose`: Show detailed schema information
- `--script`: Generate SQL scripts
- `--script-path`: Output path for SQL scripts

## Requirements

- .NET 7.0 or later
- Appropriate database drivers for the source and target databases