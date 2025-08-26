using DatabaseMigrationTool.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text;

namespace DatabaseMigrationTool.Providers
{
    public class SqlServerProvider : IDatabaseProvider
    {
        public string ProviderName => "SqlServer";
        
        // Logging delegate to redirect all output to the log file
        private Action<string>? _logger;

        public DbConnection CreateConnection(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));
            }
            
            try
            {
                // Clean the connection string to remove unsupported parameters
                string cleanConnectionString = CleanConnectionStringForSqlServer(connectionString);
                
                // Validate connection string by attempting to parse it
                var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(cleanConnectionString);
                return new SqlConnection(cleanConnectionString);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"Invalid SQL Server connection string: {ex.Message}", nameof(connectionString), ex);
            }
        }
        
        private string CleanConnectionStringForSqlServer(string connectionString)
        {
            // Remove parameters that are not supported by SQL Server but might be present from other providers
            var unsupportedKeywords = new[] { "version", "servertype", "usesingleconnection", "clientencoding", "fbclientlibrary", "dialect", "isolationlevel" };
            
            var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
            var cleanParts = new List<string>();
            bool hasEncryptSetting = false;
            bool hasTrustServerCertificate = false;
            
            foreach (var part in parts)
            {
                var keyValue = part.Split('=', 2);
                if (keyValue.Length == 2)
                {
                    string key = keyValue[0].Trim().ToLowerInvariant();
                    if (!unsupportedKeywords.Contains(key))
                    {
                        cleanParts.Add(part);
                        
                        // Check for existing SSL/encryption settings
                        if (key == "encrypt")
                            hasEncryptSetting = true;
                        if (key == "trustservercertificate")
                            hasTrustServerCertificate = true;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(part))
                {
                    cleanParts.Add(part);
                }
            }
            
            // Add SSL/encryption settings if not present to avoid certificate errors
            if (!hasEncryptSetting)
            {
                cleanParts.Add("Encrypt=False");
            }
            if (!hasTrustServerCertificate)
            {
                cleanParts.Add("TrustServerCertificate=True");
            }
            
            return string.Join(";", cleanParts);
        }
        
        public void SetLogger(Action<string> logger)
        {
            _logger = logger;
        }
        
        private void Log(string message)
        {
            if (_logger != null)
            {
                _logger(message);
            }
            else
            {
                Console.WriteLine(message);
            }
        }

        public async Task<List<TableSchema>> GetTablesAsync(DbConnection connection, IEnumerable<string>? tableNames = null)
        {
            string tableFilter = "";
            if (tableNames != null && tableNames.Any())
            {
                var quotedNames = tableNames.Select(t => $"'{t.Replace("'", "''")}'");
                tableFilter = $" AND t.name IN ({string.Join(",", quotedNames)})";
            }

            var sql = $@"
                SELECT 
                    t.name AS [Name],
                    s.name AS [Schema]
                FROM 
                    sys.tables t
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE 
                    t.is_ms_shipped = 0{tableFilter}
                ORDER BY 
                    s.name, t.name";

            var tables = await connection.QueryAsync<TableSchema>(sql);
            var result = tables.ToList();

            foreach (var table in result)
            {
                table.Columns = await GetColumnsAsync(connection, table.Name, table.Schema);
                table.Indexes = await GetIndexesAsync(connection, table.Name, table.Schema);
                table.ForeignKeys = await GetForeignKeysAsync(connection, table.Name, table.Schema);
                table.Constraints = await GetConstraintsAsync(connection, table.Name, table.Schema);
            }

            return result;
        }

        public async Task<TableSchema> GetTableSchemaAsync(DbConnection connection, string tableName, string? schema = null)
        {
            schema ??= "dbo";

            var sql = @"
                SELECT 
                    t.name AS [Name],
                    s.name AS [Schema]
                FROM 
                    sys.tables t
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE 
                    t.name = @TableName
                    AND s.name = @Schema";

            var table = await connection.QuerySingleOrDefaultAsync<TableSchema>(
                sql,
                new { TableName = tableName, Schema = schema });

            if (table == null)
            {
                throw new Exception($"Table {schema}.{tableName} not found");
            }

            table.Columns = await GetColumnsAsync(connection, tableName, schema);
            table.Indexes = await GetIndexesAsync(connection, tableName, schema);
            table.ForeignKeys = await GetForeignKeysAsync(connection, tableName, schema);
            table.Constraints = await GetConstraintsAsync(connection, tableName, schema);

            return table;
        }

        public async Task<List<ColumnDefinition>> GetColumnsAsync(DbConnection connection, string tableName, string? schema = null)
        {
            schema ??= "dbo";

            var sql = @"
                SELECT 
                    c.name AS [Name],
                    t.name AS DataType,
                    c.is_nullable AS IsNullable,
                    CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey,
                    c.is_identity AS IsIdentity,
                    dc.definition AS DefaultValue,
                    CASE WHEN t.name IN ('nvarchar', 'nchar', 'varchar', 'char', 'binary', 'varbinary') 
                         THEN c.max_length
                         ELSE NULL 
                    END AS MaxLength,
                    c.precision AS Precision,
                    c.scale AS Scale,
                    c.column_id AS OrdinalPosition
                FROM 
                    sys.columns c
                    INNER JOIN sys.tables tab ON c.object_id = tab.object_id
                    INNER JOIN sys.schemas s ON tab.schema_id = s.schema_id
                    INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
                    LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
                    LEFT JOIN (
                        SELECT 
                            ic.column_id, 
                            ic.object_id
                        FROM 
                            sys.index_columns ic
                            INNER JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                        WHERE 
                            i.is_primary_key = 1
                    ) pk ON c.column_id = pk.column_id AND c.object_id = pk.object_id
                WHERE 
                    tab.name = @TableName
                    AND s.name = @Schema
                ORDER BY 
                    c.column_id";

            var columns = await connection.QueryAsync<ColumnDefinition>(
                sql,
                new { TableName = tableName, Schema = schema });

            return columns.ToList();
        }

        public async Task<List<IndexDefinition>> GetIndexesAsync(DbConnection connection, string tableName, string? schema = null)
        {
            schema ??= "dbo";

            var sql = @"
                SELECT 
                    i.name AS [Name],
                    i.is_unique AS IsUnique,
                    CASE WHEN i.type_desc = 'CLUSTERED' THEN 1 ELSE 0 END AS IsClustered
                FROM 
                    sys.indexes i
                    INNER JOIN sys.tables t ON i.object_id = t.object_id
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE 
                    t.name = @TableName
                    AND s.name = @Schema
                    AND i.is_primary_key = 0
                    AND i.is_unique_constraint = 0";

            var indexes = await connection.QueryAsync<IndexDefinition>(
                sql, 
                new { TableName = tableName, Schema = schema });

            var result = indexes.ToList();

            foreach (var index in result)
            {
                var columnSql = @"
                    SELECT 
                        c.name
                    FROM 
                        sys.index_columns ic
                        INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                        INNER JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                        INNER JOIN sys.tables t ON i.object_id = t.object_id
                        INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                    WHERE 
                        i.name = @IndexName
                        AND t.name = @TableName
                        AND s.name = @Schema
                    ORDER BY 
                        ic.key_ordinal";

                var columns = await connection.QueryAsync<string>(
                    columnSql, 
                    new { IndexName = index.Name, TableName = tableName, Schema = schema });
                
                index.Columns = columns.ToList();
            }

            return result;
        }

        public async Task<List<ForeignKeyDefinition>> GetForeignKeysAsync(DbConnection connection, string tableName, string? schema = null)
        {
            schema ??= "dbo";

            var sql = @"
                SELECT 
                    fk.name AS [Name],
                    OBJECT_SCHEMA_NAME(fk.referenced_object_id) AS ReferencedTableSchema,
                    OBJECT_NAME(fk.referenced_object_id) AS ReferencedTableName,
                    fk.update_referential_action_desc AS UpdateRule,
                    fk.delete_referential_action_desc AS DeleteRule
                FROM 
                    sys.foreign_keys fk
                    INNER JOIN sys.tables t ON fk.parent_object_id = t.object_id
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE 
                    t.name = @TableName
                    AND s.name = @Schema";

            var foreignKeys = await connection.QueryAsync<ForeignKeyDefinition>(
                sql, 
                new { TableName = tableName, Schema = schema });

            var result = foreignKeys.ToList();

            foreach (var fk in result)
            {
                var columnSql = @"
                    SELECT 
                        c.name
                    FROM 
                        sys.foreign_key_columns fkc
                        INNER JOIN sys.columns c ON fkc.parent_object_id = c.object_id AND fkc.parent_column_id = c.column_id
                        INNER JOIN sys.foreign_keys fk ON fkc.constraint_object_id = fk.object_id
                    WHERE 
                        fk.name = @ForeignKeyName
                    ORDER BY 
                        fkc.constraint_column_id";

                var columns = await connection.QueryAsync<string>(
                    columnSql, 
                    new { ForeignKeyName = fk.Name });
                
                fk.Columns = columns.ToList();

                var refColumnSql = @"
                    SELECT 
                        c.name
                    FROM 
                        sys.foreign_key_columns fkc
                        INNER JOIN sys.columns c ON fkc.referenced_object_id = c.object_id AND fkc.referenced_column_id = c.column_id
                        INNER JOIN sys.foreign_keys fk ON fkc.constraint_object_id = fk.object_id
                    WHERE 
                        fk.name = @ForeignKeyName
                    ORDER BY 
                        fkc.constraint_column_id";

                var refColumns = await connection.QueryAsync<string>(
                    refColumnSql, 
                    new { ForeignKeyName = fk.Name });
                
                fk.ReferencedColumns = refColumns.ToList();
            }

            return result;
        }

        public async Task<List<ConstraintDefinition>> GetConstraintsAsync(DbConnection connection, string tableName, string? schema = null)
        {
            schema ??= "dbo";

            var sql = @"
                SELECT 
                    c.name AS [Name],
                    CASE 
                        WHEN c.type = 'PK' THEN 'PRIMARY KEY'
                        WHEN c.type = 'UQ' THEN 'UNIQUE'
                        WHEN c.type = 'C' THEN 'CHECK'
                    END AS Type,
                    cc.definition AS Definition
                FROM 
                    sys.objects c
                    INNER JOIN sys.tables t ON c.parent_object_id = t.object_id
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                    LEFT JOIN sys.check_constraints cc ON c.object_id = cc.object_id
                WHERE 
                    t.name = @TableName
                    AND s.name = @Schema
                    AND c.type IN ('PK', 'UQ', 'C')";

            var constraints = await connection.QueryAsync<ConstraintDefinition>(
                sql, 
                new { TableName = tableName, Schema = schema });

            var result = constraints.ToList();

            foreach (var constraint in result)
            {
                if (constraint.Type == "PRIMARY KEY" || constraint.Type == "UNIQUE")
                {
                    var columnSql = @"
                        SELECT 
                            c.name
                        FROM 
                            sys.index_columns ic
                            INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                            INNER JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                            INNER JOIN sys.key_constraints kc ON i.object_id = kc.parent_object_id AND i.index_id = kc.unique_index_id
                        WHERE 
                            kc.name = @ConstraintName
                        ORDER BY 
                            ic.key_ordinal";

                    var columns = await connection.QueryAsync<string>(
                        columnSql, 
                        new { ConstraintName = constraint.Name });
                    
                    constraint.Columns = columns.ToList();
                }
            }

            return result;
        }

        public async Task<IAsyncEnumerable<RowData>> GetTableDataAsync(DbConnection connection, string tableName, string? schema = null, string? whereClause = null, int batchSize = 1000)
        {
            schema ??= "dbo";
            var fullTableName = $"[{schema}].[{tableName}]";
            
            var sql = $"SELECT * FROM {fullTableName} WITH (NOLOCK)";
            if (!string.IsNullOrEmpty(whereClause))
            {
                sql += $" WHERE {whereClause}";
            }
            
            // Log the SQL for debugging
            Console.WriteLine($"[DEBUG] SQL Query: {sql}");

            // Always use a fresh connection to avoid issues with previously used connections
            using (var newConnection = CreateConnection(connection.ConnectionString))
            {
                await newConnection.OpenAsync();
                
                var command = newConnection.CreateCommand();
                command.CommandText = sql;
                command.CommandType = CommandType.Text;
                
                try
                {
                    var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);
                    // Return empty result set if no rows
                    if (!reader.HasRows)
                    {
                        reader.Close();
                        return new EmptyRowDataAsyncEnumerable();
                    }
                    
                    return new DatabaseReaderAsyncEnumerable(reader);
                }
                catch (Exception ex)
                {
                    if (newConnection.State == ConnectionState.Open)
                    {
                        newConnection.Close();
                    }
                    throw new Exception($"Error reading data from {fullTableName}: {ex.Message}", ex);
                }
            }
        }

        public async Task CreateTableAsync(DbConnection connection, TableSchema tableSchema)
        {
            try
            {
                // Basic validation
                if (string.IsNullOrEmpty(tableSchema.Name))
                {
                    throw new ArgumentException("Cannot create table with empty name");
                }
                
                if (tableSchema.Schema == null)
                {
                    tableSchema.Schema = "dbo";
                }
                
                if (tableSchema.Columns == null || tableSchema.Columns.Count == 0)
                {
                    throw new ArgumentException($"Table {tableSchema.Schema}.{tableSchema.Name} has no columns");
                }

                Log($"Creating table {tableSchema.Schema}.{tableSchema.Name} with {tableSchema.Columns.Count} columns");

                // Simplest approach: Create table without any defaults
                using (var sqlConn = new SqlConnection(connection.ConnectionString))
                {
                    await sqlConn.OpenAsync();
                    
                    // Drop table if it exists
                    string dropTableSql = $@"
                        IF OBJECT_ID(N'[{tableSchema.Schema}].[{tableSchema.Name}]', N'U') IS NOT NULL
                            DROP TABLE [{tableSchema.Schema}].[{tableSchema.Name}]";
                    
                    using (var cmd = new SqlCommand(dropTableSql, sqlConn))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                    
                    // Generate column definitions
                    List<string> columnDefs = new List<string>();
                    foreach (var column in tableSchema.Columns)
                    {
                        string colDef = $"[{column.Name}] {column.DataType}";
                        
                        // Add size/precision specifications
                        if (column.MaxLength.HasValue)
                        {
                            if (column.MaxLength == -1)
                            {
                                colDef += "(MAX)";
                            }
                            else if (column.DataType.Contains("char"))
                            {
                                int charLength = 0;
                                if (column.DataType.StartsWith("n") && column.MaxLength.Value > 0)
                                    charLength = column.MaxLength.Value / 2;
                                else
                                    charLength = column.MaxLength ?? 0;
                                    
                                colDef += $"({charLength})";
                            }
                            else
                            {
                                colDef += $"({column.MaxLength})";
                            }
                        }
                        else if (column.Precision.HasValue && column.Scale.HasValue)
                        {
                            // Handle precision/scale based on data type
                            var typeName = column.DataType.ToLowerInvariant();
                            
                            // decimal/numeric: Use both precision and scale
                            if (typeName.Contains("decimal") || typeName.Contains("numeric"))
                            {
                                colDef += $"({column.Precision}, {column.Scale})";
                            }
                            // float/real: Use only precision (no scale)
                            else if (typeName.Contains("float") || typeName.Contains("real"))
                            {
                                colDef += $"({column.Precision})";
                            }
                        }
                        
                        // Add nullability
                        colDef += column.IsNullable ? " NULL" : " NOT NULL";
                        
                        // Add identity if needed
                        if (column.IsIdentity)
                        {
                            colDef += " IDENTITY(1,1)";
                        }
                        
                        // Skip default values for now
                        columnDefs.Add(colDef);
                    }
                    
                    // Create table with all columns
                    string createTableSql = $"CREATE TABLE [{tableSchema.Schema}].[{tableSchema.Name}] (\n";
                    createTableSql += string.Join(",\n", columnDefs);
                    createTableSql += "\n)";
                    
                    using (var cmd = new SqlCommand(createTableSql, sqlConn))
                    {
                        await cmd.ExecuteNonQueryAsync();
                        Log($"Created table {tableSchema.Schema}.{tableSchema.Name}");
                    }
                    
                    // Add default constraints after the table is created
                    foreach (var column in tableSchema.Columns)
                    {
                        if (!string.IsNullOrEmpty(column.DefaultValue))
                        {
                            try
                            {
                                // Use a literal default value assignment to avoid parameter detection issues
                                string defaultValueLiteral = LiteralizeDefaultValue(column.DefaultValue, column.DataType);
                                string addDefaultSql = $"ALTER TABLE [{tableSchema.Schema}].[{tableSchema.Name}] ADD CONSTRAINT [DF_{tableSchema.Name}_{column.Name}] DEFAULT {defaultValueLiteral} FOR [{column.Name}]";
                                
                                using (var defaultCmd = new SqlCommand(addDefaultSql, sqlConn))
                                {
                                    await defaultCmd.ExecuteNonQueryAsync();
                                }
                            }
                            catch (Exception ex)
                            {
                                Log($"Warning: Failed to add default constraint for column {column.Name}: {ex.Message}");
                                
                                // Try an alternative approach with EXEC
                                try
                                {
                                    // Use EXEC to break the direct connection between default value and SQL parsing
                                    string execSql = $"EXEC('ALTER TABLE [{tableSchema.Schema}].[{tableSchema.Name}] ADD CONSTRAINT [DF_{tableSchema.Name}_{column.Name}] DEFAULT {column.DefaultValue.Replace("'", "''")} FOR [{column.Name}]')";
                                    
                                    using (var execCmd = new SqlCommand(execSql, sqlConn))
                                    {
                                        await execCmd.ExecuteNonQueryAsync();
                                    }
                                }
                                catch (Exception)
                                {
                                    // Continue since we at least created the table
                                }
                            }
                        }
                    }
                }
                
                // Verify table was created
                using (var verifyConn = new SqlConnection(connection.ConnectionString))
                {
                    await verifyConn.OpenAsync();
                    string verifySql = $"SELECT OBJECT_ID(N'[{tableSchema.Schema}].[{tableSchema.Name}]', N'U') AS TableExists";
                    using (var cmd = new SqlCommand(verifySql, verifyConn))
                    {
                        var result = await cmd.ExecuteScalarAsync();
                        if (result == null || result == DBNull.Value)
                        {
                            throw new Exception($"Failed to create table {tableSchema.Schema}.{tableSchema.Name}");
                        }
                    }
                }
                
                Log($"Table {tableSchema.Schema}.{tableSchema.Name} created successfully");
            }
            catch (Exception ex)
            {
                Log($"Error creating table {tableSchema.Schema ?? "unknown"}.{tableSchema.Name}: {ex.Message}");
                throw;
            }
        }
        
        // This method has been removed as we're now using direct SQL commands instead of script files
        
        // This method has been removed as we're no longer doing special handling of default values

        public async Task CreateIndexesAsync(DbConnection connection, TableSchema tableSchema)
        {
            try
            {
                // First verify if table exists
                using (var sqlConn = new SqlConnection(connection.ConnectionString))
                {
                    await sqlConn.OpenAsync();
                    
                    // Check if table exists
                    string checkSql = $"SELECT OBJECT_ID('[{tableSchema.Schema}].[{tableSchema.Name}]', 'U') as TableId";
                    bool tableExists = false;
                    
                    using (var cmd = new SqlCommand(checkSql, sqlConn))
                    {
                        var result = await cmd.ExecuteScalarAsync();
                        tableExists = (result != null && result != DBNull.Value);
                    }
                    
                    if (!tableExists)
                    {
                        Log($"Cannot create indexes: Table [{tableSchema.Schema}].[{tableSchema.Name}] does not exist");
                        return;
                    }
                    
                    // Create indexes using the same connection
                    foreach (var index in tableSchema.Indexes)
                    {
                        if (index.Columns.Count == 0)
                            continue;

                        var indexType = index.IsClustered ? "CLUSTERED" : "NONCLUSTERED";
                        var uniqueness = index.IsUnique ? "UNIQUE" : "";
                        var columns = string.Join(", ", index.Columns.Select(c => $"[{c}]"));
                        var fullTableName = $"[{tableSchema.Schema}].[{tableSchema.Name}]";
                        
                        var sql = $"CREATE {uniqueness} {indexType} INDEX [{index.Name}] ON {fullTableName} ({columns})";
                        
                        try
                        {
                            using (var cmd = new SqlCommand(sql, sqlConn))
                            {
                                await cmd.ExecuteNonQueryAsync();
                                Log($"Created index {index.Name} on table {tableSchema.Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Error creating index {index.Name}: {ex.Message}");
                            // Continue with other indexes
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error in CreateIndexesAsync: {ex.Message}");
                throw;
            }
        }

        public async Task CreateConstraintsAsync(DbConnection connection, TableSchema tableSchema)
        {
            try
            {
                // First verify if table exists
                using (var sqlConn = new SqlConnection(connection.ConnectionString))
                {
                    await sqlConn.OpenAsync();
                    
                    // Check if table exists
                    string checkSql = $"SELECT OBJECT_ID('[{tableSchema.Schema}].[{tableSchema.Name}]', 'U') as TableId";
                    bool tableExists = false;
                    
                    using (var cmd = new SqlCommand(checkSql, sqlConn))
                    {
                        var result = await cmd.ExecuteScalarAsync();
                        tableExists = (result != null && result != DBNull.Value);
                    }
                    
                    if (!tableExists)
                    {
                        Log($"Cannot create constraints: Table [{tableSchema.Schema}].[{tableSchema.Name}] does not exist");
                        return;
                    }
                    
                    // Create CHECK constraints
                    foreach (var constraint in tableSchema.Constraints.Where(c => c.Type == "CHECK"))
                    {
                        var fullTableName = $"[{tableSchema.Schema}].[{tableSchema.Name}]";
                        var sql = $"ALTER TABLE {fullTableName} ADD CONSTRAINT [{constraint.Name}] CHECK {constraint.Definition}";
                        
                        try
                        {
                            using (var cmd = new SqlCommand(sql, sqlConn))
                            {
                                await cmd.ExecuteNonQueryAsync();
                                Log($"Created CHECK constraint {constraint.Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Error creating CHECK constraint {constraint.Name}: {ex.Message}");
                            // Continue with other constraints
                        }
                    }

                    // Create UNIQUE constraints
                    foreach (var constraint in tableSchema.Constraints.Where(c => c.Type == "UNIQUE"))
                    {
                        if (constraint.Columns.Count == 0)
                            continue;

                        var columns = string.Join(", ", constraint.Columns.Select(c => $"[{c}]"));
                        var fullTableName = $"[{tableSchema.Schema}].[{tableSchema.Name}]";
                        
                        var sql = $"ALTER TABLE {fullTableName} ADD CONSTRAINT [{constraint.Name}] UNIQUE ({columns})";
                        
                        try
                        {
                            using (var cmd = new SqlCommand(sql, sqlConn))
                            {
                                await cmd.ExecuteNonQueryAsync();
                                Log($"Created UNIQUE constraint {constraint.Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Error creating UNIQUE constraint {constraint.Name}: {ex.Message}");
                            // Continue with other constraints
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error in CreateConstraintsAsync: {ex.Message}");
                throw;
            }
        }

        public async Task CreateForeignKeysAsync(DbConnection connection, TableSchema tableSchema)
        {
            try
            {
                // First verify if table exists
                using (var sqlConn = new SqlConnection(connection.ConnectionString))
                {
                    await sqlConn.OpenAsync();
                    
                    // Check if table exists
                    string checkSql = $"SELECT OBJECT_ID('[{tableSchema.Schema}].[{tableSchema.Name}]', 'U') as TableId";
                    bool tableExists = false;
                    
                    using (var cmd = new SqlCommand(checkSql, sqlConn))
                    {
                        var result = await cmd.ExecuteScalarAsync();
                        tableExists = (result != null && result != DBNull.Value);
                    }
                    
                    if (!tableExists)
                    {
                        Log($"Cannot create foreign keys: Table [{tableSchema.Schema}].[{tableSchema.Name}] does not exist");
                        return;
                    }
                    
                    // Create foreign keys
                    foreach (var fk in tableSchema.ForeignKeys)
                    {
                        if (fk.Columns.Count == 0 || fk.ReferencedColumns.Count == 0)
                            continue;
                        
                        // Check if referenced table exists
                        string refCheckSql = $"SELECT OBJECT_ID('[{fk.ReferencedTableSchema}].[{fk.ReferencedTableName}]', 'U') as TableId";
                        bool refTableExists = false;
                        
                        using (var cmd = new SqlCommand(refCheckSql, sqlConn))
                        {
                            var result = await cmd.ExecuteScalarAsync();
                            refTableExists = (result != null && result != DBNull.Value);
                        }
                        
                        if (!refTableExists)
                        {
                            Log($"Cannot create foreign key {fk.Name}: Referenced table [{fk.ReferencedTableSchema}].[{fk.ReferencedTableName}] does not exist");
                            continue;
                        }

                        var columns = string.Join(", ", fk.Columns.Select(c => $"[{c}]"));
                        var refColumns = string.Join(", ", fk.ReferencedColumns.Select(c => $"[{c}]"));
                        var fullTableName = $"[{tableSchema.Schema}].[{tableSchema.Name}]";
                        var fullRefTableName = $"[{fk.ReferencedTableSchema}].[{fk.ReferencedTableName}]";
                        
                        var sql = $@"
                            ALTER TABLE {fullTableName} 
                            ADD CONSTRAINT [{fk.Name}] 
                            FOREIGN KEY ({columns}) 
                            REFERENCES {fullRefTableName} ({refColumns})";

                        if (!string.IsNullOrEmpty(fk.UpdateRule) && fk.UpdateRule != "NO_ACTION")
                        {
                            sql += $" ON UPDATE {fk.UpdateRule}";
                        }

                        if (!string.IsNullOrEmpty(fk.DeleteRule) && fk.DeleteRule != "NO_ACTION")
                        {
                            sql += $" ON DELETE {fk.DeleteRule}";
                        }

                        try
                        {
                            using (var cmd = new SqlCommand(sql, sqlConn))
                            {
                                await cmd.ExecuteNonQueryAsync();
                                Log($"Created foreign key {fk.Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Error creating foreign key {fk.Name}: {ex.Message}");
                            // Continue with other foreign keys
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error in CreateForeignKeysAsync: {ex.Message}");
                throw;
            }
        }

        public async Task ImportDataAsync(DbConnection connection, string tableName, string? schema, IAsyncEnumerable<RowData> data, int batchSize = 1000)
        {
            schema ??= "dbo";
            var fullTableName = $"[{schema}].[{tableName}]";
            
            Log($"Starting import for table {fullTableName}");
            
            try
            {
                var tableSchema = await GetTableSchemaAsync(connection, tableName, schema);
                var columnsList = tableSchema.Columns.Select(c => c.Name).ToList();
                
                var columnsFormatted = string.Join(", ", columnsList.Select(c => $"[{c}]"));
                
                // Check if table has identity columns that need special handling
                bool hasIdentityColumns = tableSchema.Columns.Any(c => c.IsIdentity);
                if (hasIdentityColumns)
                {
                    try
                    {
                        await connection.ExecuteAsync($"SET IDENTITY_INSERT {fullTableName} ON");
                    }
                    catch (Exception ex)
                    {
                        Log($"Warning: Failed to enable IDENTITY_INSERT: {ex.Message}");
                    }
                }
                
                var batch = new List<string>();
                var count = 0;
                int totalProcessed = 0;
                bool hasRows = false;
                
                await foreach (var row in data)
                {
                    hasRows = true;
                    totalProcessed++;
                    
                    if (totalProcessed == 1)
                    {
                        // Check for column name mismatches
                        var missingColumns = columnsList.Except(row.Values.Keys).ToList();
                        var extraColumns = row.Values.Keys.Except(columnsList).ToList();
                        
                        if (missingColumns.Any())
                        {
                            Log($"Warning: Row is missing columns defined in the schema: {string.Join(", ", missingColumns)}");
                        }
                        
                        if (extraColumns.Any())
                        {
                            Log($"Warning: Row contains extra columns not in the schema: {string.Join(", ", extraColumns)}");
                        }
                    }
                    
                    var insertSql = GenerateInsertScript(tableSchema, row);
                    
                    batch.Add(insertSql);
                    count++;
                    
                    if (count >= batchSize)
                    {
                        await ExecuteBatchAsync(connection, batch);
                        batch.Clear();
                        count = 0;
                    }
                }
                
                if (!hasRows)
                {
                    Log($"Warning: No rows were received from the data source for table {fullTableName}");
                }
                
                if (batch.Count > 0)
                {
                    await ExecuteBatchAsync(connection, batch);
                }
                
                // Disable IDENTITY_INSERT if it was enabled
                if (hasIdentityColumns)
                {
                    try
                    {
                        await connection.ExecuteAsync($"SET IDENTITY_INSERT {fullTableName} OFF");
                    }
                    catch (Exception ex)
                    {
                        Log($"Warning: Failed to disable IDENTITY_INSERT: {ex.Message}");
                    }
                }
                
                Log($"Completed import for table {fullTableName}");
            }
            catch (Exception ex)
            {
                Log($"Error importing data to table {fullTableName}: {ex.Message}");
                throw;
            }
        }

        private async Task ExecuteBatchAsync(DbConnection connection, List<string> batch)
        {
            if (batch == null || batch.Count == 0)
            {
                return;
            }
            
            try
            {
                var sql = string.Join(";\r\n", batch);
                
                if (sql.Length > 100000)
                {
                    Log($"Warning: Very large SQL batch: {sql.Length} characters. Consider reducing batch size.");
                }
                
                try
                {
                    int rowsAffected = await connection.ExecuteAsync(sql);
                    
                    // Validate that rows were actually inserted
                    if (rowsAffected == 0 && batch.Count > 0)
                    {
                        throw new InvalidOperationException($"Batch execution returned 0 affected rows for {batch.Count} INSERT statements");
                    }

                    if (rowsAffected != batch.Count)
                    {
                        Log($"Warning: Expected {batch.Count} affected rows but got {rowsAffected}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error executing batch: {ex.Message}");
                    
                    // Try each statement individually for debugging
                    if (batch.Count > 1)
                    {
                        int successfulIndividual = 0;
                        for (int i = 0; i < batch.Count; i++)
                        {
                            try
                            {
                                int individualResult = await connection.ExecuteAsync(batch[i]);
                                if (individualResult > 0) successfulIndividual++;
                            }
                            catch (Exception individualEx)
                            {
                                Log($"Statement {i+1} failed: {individualEx.Message}");
                            }
                        }
                        Log($"{successfulIndividual}/{batch.Count} statements succeeded individually");
                    }
                    
                    throw;
                }
            }
            catch (Exception ex)
            {
                Log($"Unexpected error in batch execution: {ex.Message}");
                throw;
            }
        }

        public string GenerateTableCreationScript(TableSchema tableSchema)
        {
            var sb = new StringBuilder();
            var fullTableName = $"[{tableSchema.Schema}].[{tableSchema.Name}]";
            
            sb.AppendLine($"CREATE TABLE {fullTableName} (");
            
            var columnDefs = new List<string>();
            foreach (var column in tableSchema.Columns)
            {
                // Escape any column names that might be mistaken for parameters
                var safeColumnName = EscapeIdentifier(column.Name);
                var columnDef = $"    {safeColumnName} {SanitizeDataType(column.DataType)}";
                
                if (column.MaxLength.HasValue)
                {
                    if (column.MaxLength == -1)
                    {
                        columnDef += "(MAX)";
                    }
                    else if (column.DataType.Contains("char"))
                    {
                        // Safely handle division to prevent divide by zero exception
                        int charLength = 0;
                        if (column.DataType.StartsWith("n") && column.MaxLength.HasValue) 
                        {
                            if (column.MaxLength.Value > 0)
                            {
                                charLength = column.MaxLength.Value / 2;
                            }
                            else
                            {
                                // If MaxLength is 0, use 0 for charLength without division
                                charLength = 0;
                            }
                        }
                        else
                        {
                            charLength = column.MaxLength ?? 0;
                        }
                        columnDef += $"({charLength})";
                    }
                    else
                    {
                        columnDef += $"({column.MaxLength})";
                    }
                }
                else if (column.Precision.HasValue && column.Scale.HasValue)
                {
                    // Handle precision/scale based on data type
                    var typeName = column.DataType.ToLowerInvariant();
                    
                    // decimal/numeric: Use both precision and scale
                    if (typeName.Contains("decimal") || typeName.Contains("numeric"))
                    {
                        columnDef += $"({column.Precision}, {column.Scale})";
                    }
                    // float/real: Use only precision (no scale)
                    else if (typeName.Contains("float") || typeName.Contains("real"))
                    {
                        columnDef += $"({column.Precision})";
                    }
                    // For other types like int, money, etc., don't add precision/scale even if they're present
                }
                
                if (!column.IsNullable)
                {
                    columnDef += " NOT NULL";
                }
                else
                {
                    columnDef += " NULL";
                }
                
                if (column.IsIdentity)
                {
                    columnDef += " IDENTITY(1,1)";
                }
                
                if (!string.IsNullOrEmpty(column.DefaultValue))
                {
                    // Apply default value directly
                    columnDef += $" DEFAULT {column.DefaultValue}";
                }
                
                columnDefs.Add(columnDef);
            }
            
            // Add primary key constraint
            var pkColumns = tableSchema.Columns.Where(c => c.IsPrimaryKey).ToList();
            if (pkColumns.Any())
            {
                var pkName = tableSchema.Constraints.FirstOrDefault(c => c.Type == "PRIMARY KEY")?.Name ?? $"PK_{tableSchema.Name}";
                var pkColumnNames = string.Join(", ", pkColumns.Select(c => $"[{c.Name}]"));
                columnDefs.Add($"    CONSTRAINT [{pkName}] PRIMARY KEY ({pkColumnNames})");
            }
            
            sb.AppendLine(string.Join(",\r\n", columnDefs));
            sb.AppendLine(")");
            
            return sb.ToString();
        }
        
        // Helper method to sanitize data types to prevent SQL injection
        private string SanitizeDataType(string dataType)
        {
            // Make sure data type is valid SQL Server data type
            var validTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "bigint", "binary", "bit", "char", "date", "datetime", "datetime2", "datetimeoffset", 
                "decimal", "float", "geography", "geometry", "hierarchyid", "image", "int", "money", 
                "nchar", "ntext", "numeric", "nvarchar", "real", "smalldatetime", "smallint", 
                "smallmoney", "sql_variant", "text", "time", "timestamp", "tinyint", "uniqueidentifier", 
                "varbinary", "varchar", "xml"
            };
            
            // Extract base type (removing parentheses and parameters)
            string baseType = dataType.Split('(')[0].Trim();
            
            if (validTypes.Contains(baseType))
            {
                return dataType; // Return original if valid
            }
            else
            {
                return "nvarchar(max)"; // Default to safe type if invalid
            }
        }
        
        // This method has been removed as we're no longer doing special handling of default values

        public string GenerateInsertScript(TableSchema tableSchema, RowData row)
        {
            try
            {
                var fullTableName = $"[{tableSchema.Schema}].[{tableSchema.Name}]";
                
                if (row == null || row.Values == null || row.Values.Count == 0)
                {
                    return $"-- Empty row data for {fullTableName}, no INSERT generated";
                }
                
                var columns = row.Values.Keys.ToList();
                var columnsSql = string.Join(", ", columns.Select(c => $"[{c}]"));
                
                var values = new List<string>();
                
                foreach (var column in columns)
                {
                    try
                    {
                        var columnSchema = tableSchema.Columns.FirstOrDefault(c => c.Name == column);
                        var value = row.Values[column];
                        string formattedValue;
                        
                        if (value == null || value == DBNull.Value)
                        {
                            formattedValue = "NULL";
                        }
                        else if (value is string strValue)
                        {
                            formattedValue = $"'{strValue.Replace("'", "''")}'";
                        }
                        else if (value is DateTime dateValue)
                        {
                            formattedValue = $"'{dateValue:yyyy-MM-dd HH:mm:ss.fff}'";
                        }
                        else if (value is bool boolValue)
                        {
                            formattedValue = boolValue ? "1" : "0";
                        }
                        else if (value is byte[] byteValue)
                        {
                            formattedValue = $"0x{BitConverter.ToString(byteValue).Replace("-", "")}";
                        }
                        else
                        {
                            formattedValue = value.ToString()!;
                            
                            // Validate numeric types (but don't log warnings in production)
                            if (columnSchema != null)
                            {
                                string dataType = columnSchema.DataType.ToLowerInvariant();
                                
                                // For numeric types, ensure proper formatting
                                if ((dataType.Contains("int") || dataType.Contains("float") || 
                                     dataType.Contains("decimal") || dataType.Contains("money") ||
                                     dataType.Contains("numeric") || dataType.Contains("real")))
                                {
                                    // Remove quotes if accidentally added to numeric values
                                    if (formattedValue.StartsWith("'") && formattedValue.EndsWith("'"))
                                    {
                                        formattedValue = formattedValue.Trim('\'');
                                    }
                                }
                            }
                        }
                        
                        values.Add(formattedValue);
                    }
                    catch (Exception ex)
                    {
                        Log($"Error formatting value for column {column}: {ex.Message}");
                        values.Add("NULL"); // Use NULL as a fallback to avoid SQL syntax errors
                    }
                }
                
                var valuesSql = string.Join(", ", values);
                
                if (columns.Count == 0 || values.Count == 0)
                {
                    return $"-- No columns or values to insert for {fullTableName}";
                }
                
                return $"INSERT INTO {fullTableName} ({columnsSql}) VALUES ({valuesSql})";
            }
            catch (Exception ex)
            {
                Log($"Error generating INSERT: {ex.Message}");
                return $"-- Error generating INSERT: {ex.Message}";
            }
        }

        public string EscapeIdentifier(string identifier)
        {
            return $"[{identifier}]";
        }
        
        /// <summary>
        /// Handles default value parameter detection issues generically without hardcoding column names
        /// </summary>
        private string HandleDefaultValueParameterIssues(string defaultValue)
        {
            if (string.IsNullOrEmpty(defaultValue))
                return "NULL";
            
            Log($"===DEFAULT HANDLER=== Processing default value: {defaultValue}");
                
            // Handle common patterns that cause SQL parameter detection issues
            
            // Case 1: Values starting with @ (which SQL Server treats as parameters)
            if (defaultValue.Contains('@'))
            {
                Log("===DEFAULT HANDLER=== Found @ character, escaping it");
                // Double all @ characters to escape them in SQL Server
                return defaultValue.Replace("@", "@@");
            }
            
            // Case 2: Default values using N'string' notation
            if (defaultValue.StartsWith("N'") && defaultValue.EndsWith("'"))
            {
                Log("===DEFAULT HANDLER=== Found Unicode string literal (N'...'), leaving as is");
                // Already properly formatted as Unicode string literal
                return defaultValue;
            }
            
            // Case 3: Plain string values without N prefix
            if (defaultValue.StartsWith("'") && defaultValue.EndsWith("'"))
            {
                Log("===DEFAULT HANDLER=== Found string literal without N prefix, adding N prefix");
                // Add the N prefix to make it a proper Unicode string literal
                return "N" + defaultValue;
            }
            
            // General character analysis for any default value (no special handling for specific columns)
            Log("===DEFAULT HANDLER=== Character analysis:");
            for (int i = 0; i < defaultValue.Length; i++)
            {
                char c = defaultValue[i];
                Log($"  Char at {i}: '{c}' (ASCII: {(int)c})");
            }
            
            // Case 5: Numbers, functions, or other special values
            Log("===DEFAULT HANDLER=== Using default value as is");
            return defaultValue;
        }

        private class DatabaseReaderAsyncEnumerable : IAsyncEnumerable<RowData>
        {
            private readonly DbDataReader _reader;
            private readonly string[] _columnNames;

            public DatabaseReaderAsyncEnumerable(DbDataReader reader)
            {
                _reader = reader ?? throw new ArgumentNullException(nameof(reader));
                
                if (_reader.IsClosed)
                {
                    throw new InvalidOperationException("Cannot create enumerator with closed reader");
                }
                
                // Extract column information immediately to avoid issues later
                var columnNames = new string[_reader.FieldCount];
                for (var i = 0; i < _reader.FieldCount; i++)
                {
                    columnNames[i] = _reader.GetName(i);
                }
                _columnNames = columnNames;
            }

            public IAsyncEnumerator<RowData> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                // Check reader state before creating enumerator
                if (_reader.IsClosed)
                {
                    return new EmptyRowDataAsyncEnumerator();
                }
                
                return new DatabaseReaderAsyncEnumerator(_reader, _columnNames, cancellationToken);
            }

            private class DatabaseReaderAsyncEnumerator : IAsyncEnumerator<RowData>
            {
                private readonly DbDataReader _reader;
                private readonly CancellationToken _cancellationToken;
                private readonly string[] _columnNames;

                public RowData Current { get; private set; } = new();

                public DatabaseReaderAsyncEnumerator(DbDataReader reader, string[] columnNames, CancellationToken cancellationToken)
                {
                    _reader = reader ?? throw new ArgumentNullException(nameof(reader));
                    _columnNames = columnNames ?? throw new ArgumentNullException(nameof(columnNames));
                    _cancellationToken = cancellationToken;
                }

                public async ValueTask<bool> MoveNextAsync()
                {
                    if (_reader.IsClosed)
                    {
                        return false;
                    }
                    
                    try
                    {
                        if (await _reader.ReadAsync(_cancellationToken))
                        {
                            var values = new Dictionary<string, object?>();
                            
                            for (var i = 0; i < _reader.FieldCount; i++)
                            {
                                var value = _reader.IsDBNull(i) ? null : _reader.GetValue(i);
                                values[_columnNames[i]] = value;
                            }
                            
                            Current = new RowData { Values = values };
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"Error reading from data reader: {ex.Message}", ex);
                    }
                    
                    return false;
                }

                public ValueTask DisposeAsync()
                {
                    try 
                    {
                        if (!_reader.IsClosed)
                        {
                            _reader.Dispose();
                        }
                    }
                    catch
                    {
                        // Suppress any errors during disposal
                    }
                    return new ValueTask();
                }
            }
        }
        
        private class EmptyRowDataAsyncEnumerable : IAsyncEnumerable<RowData>
        {
            public IAsyncEnumerator<RowData> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                return new EmptyRowDataAsyncEnumerator();
            }
        }
        
        private class EmptyRowDataAsyncEnumerator : IAsyncEnumerator<RowData>
        {
            public RowData Current => new RowData { Values = new Dictionary<string, object?>() };
            
            public ValueTask<bool> MoveNextAsync()
            {
                return new ValueTask<bool>(false);
            }
            
            public ValueTask DisposeAsync()
            {
                return new ValueTask();
            }
        }
    
    /// <summary>
    /// Determines the appropriate parameter value from a default value string
    /// </summary>
    private object DetermineParameterValue(string defaultValue)
    {
        if (string.IsNullOrEmpty(defaultValue))
            return DBNull.Value;
        
        // Remove any SQL-specific syntax
        string cleanValue = defaultValue;
        
        // Handle string literals: N'value' or 'value'
        if ((cleanValue.StartsWith("N'") && cleanValue.EndsWith("'")) || 
            (cleanValue.StartsWith("'") && cleanValue.EndsWith("'")))
        {
            // Extract just the value inside the quotes
            int startIndex = cleanValue.IndexOf("'") + 1;
            int endIndex = cleanValue.LastIndexOf("'");
            if (endIndex > startIndex)
            {
                cleanValue = cleanValue.Substring(startIndex, endIndex - startIndex);
                // Replace any doubled single quotes with a single quote
                cleanValue = cleanValue.Replace("''", "'");
                return cleanValue;
            }
        }
        
        // Handle numeric literals
        if (int.TryParse(cleanValue, out int intValue))
            return intValue;
        
        if (decimal.TryParse(cleanValue, out decimal decimalValue))
            return decimalValue;
        
        if (bool.TryParse(cleanValue, out bool boolValue))
            return boolValue;
        
        if (DateTime.TryParse(cleanValue, out DateTime dateTimeValue))
            return dateTimeValue;
        
        // Default to string
        return cleanValue;
    }
    
    /// <summary>
    /// Converts a default value to a literal SQL value based on data type
    /// </summary>
    private string LiteralizeDefaultValue(string defaultValue, string dataType)
    {
        if (string.IsNullOrEmpty(defaultValue))
            return "NULL";
        
        // If it's already a properly formatted SQL literal, return as is
        if (defaultValue.Equals("NULL", StringComparison.OrdinalIgnoreCase) ||
            defaultValue.StartsWith("GETDATE()") ||
            defaultValue.StartsWith("NEWID()") ||
            defaultValue.StartsWith("CONVERT(") ||
            defaultValue.StartsWith("CAST("))
        {
            return defaultValue;
        }
        
        // Handle string types
        if (dataType.Contains("char") || dataType.Contains("text") || dataType.Contains("xml"))
        {
            // If it's already a string literal, return as is or add N prefix
            if (defaultValue.StartsWith("'") && defaultValue.EndsWith("'"))
            {
                if (dataType.StartsWith("n") && !defaultValue.StartsWith("N'"))
                    return "N" + defaultValue;
                return defaultValue;
            }
            
            // Otherwise, create a string literal
            string escaped = defaultValue.Replace("'", "''");
            if (dataType.StartsWith("n"))
                return $"N'{escaped}'";
            return $"'{escaped}'";
        }
        
        // Handle date types
        if (dataType.Contains("date") || dataType.Contains("time"))
        {
            if (defaultValue.StartsWith("'") && defaultValue.EndsWith("'"))
                return defaultValue;
            return $"'{defaultValue}'";
        }
        
        // Handle binary types
        if (dataType.Contains("binary") || dataType.Contains("image"))
        {
            if (defaultValue.StartsWith("0x"))
                return defaultValue;
            return $"0x{defaultValue}";
        }
        
        // Handle bit type
        if (dataType.Equals("bit", StringComparison.OrdinalIgnoreCase))
        {
            if (defaultValue.Equals("1") || defaultValue.Equals("0"))
                return defaultValue;
            
            if (bool.TryParse(defaultValue, out bool boolValue))
                return boolValue ? "1" : "0";
        }
        
        // For all other types (numeric, etc.), return as is
        return defaultValue;
    }
}
}