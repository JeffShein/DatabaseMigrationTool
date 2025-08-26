using DatabaseMigrationTool.Models;
using Dapper;
using Npgsql;
using System.Data;
using System.Data.Common;
using System.Text;

namespace DatabaseMigrationTool.Providers
{
    public class PostgreSqlProvider : IDatabaseProvider
    {
        public string ProviderName => "PostgreSQL";
        
        // Logging delegate
        private Action<string>? _logger;

        public DbConnection CreateConnection(string connectionString)
        {
            return new NpgsqlConnection(connectionString);
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
                tableFilter = $" AND t.table_name IN ({string.Join(",", quotedNames)})";
            }

            var sql = $@"
                SELECT 
                    t.table_name AS Name,
                    t.table_schema AS Schema
                FROM 
                    information_schema.tables t
                WHERE 
                    t.table_type = 'BASE TABLE'
                    AND t.table_schema NOT IN ('pg_catalog', 'information_schema'){tableFilter}
                ORDER BY 
                    t.table_schema, t.table_name";

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
            schema ??= "public";

            var sql = @"
                SELECT 
                    t.table_name AS Name,
                    t.table_schema AS Schema
                FROM 
                    information_schema.tables t
                WHERE 
                    t.table_name = @TableName
                    AND t.table_schema = @Schema";

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
            schema ??= "public";

            var sql = @"
                SELECT 
                    c.column_name AS Name,
                    c.data_type AS DataType,
                    CASE WHEN c.is_nullable = 'YES' THEN 1 ELSE 0 END AS IsNullable,
                    CASE WHEN pk.column_name IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey,
                    CASE WHEN c.column_default LIKE 'nextval%' THEN 1 ELSE 0 END AS IsIdentity,
                    c.column_default AS DefaultValue,
                    c.character_maximum_length AS MaxLength,
                    c.numeric_precision AS Precision,
                    c.numeric_scale AS Scale,
                    c.ordinal_position AS OrdinalPosition
                FROM 
                    information_schema.columns c
                    LEFT JOIN (
                        SELECT 
                            kcu.column_name,
                            kcu.table_name,
                            kcu.table_schema
                        FROM 
                            information_schema.table_constraints tc
                            JOIN information_schema.key_column_usage kcu 
                                ON tc.constraint_name = kcu.constraint_name 
                                AND tc.table_schema = kcu.table_schema
                        WHERE 
                            tc.constraint_type = 'PRIMARY KEY'
                    ) pk ON c.column_name = pk.column_name 
                        AND c.table_name = pk.table_name 
                        AND c.table_schema = pk.table_schema
                WHERE 
                    c.table_name = @TableName
                    AND c.table_schema = @Schema
                ORDER BY 
                    c.ordinal_position";

            var columns = await connection.QueryAsync<ColumnDefinition>(
                sql,
                new { TableName = tableName, Schema = schema });

            // Get additional PostgreSQL-specific details like sequences
            foreach (var column in columns)
            {
                if (column.IsIdentity)
                {
                    var seqSql = @"
                        SELECT 
                            pg_get_serial_sequence(@Schema || '.' || @TableName, @ColumnName) as seq_name";
                    
                    var seqName = await connection.ExecuteScalarAsync<string>(
                        seqSql, 
                        new { Schema = schema, TableName = tableName, ColumnName = column.Name });
                    
                    if (!string.IsNullOrEmpty(seqName))
                    {
                        column.AdditionalProperties["sequence"] = seqName;
                    }
                }
            }

            return columns.ToList();
        }

        public async Task<List<IndexDefinition>> GetIndexesAsync(DbConnection connection, string tableName, string? schema = null)
        {
            schema ??= "public";

            var sql = @"
                SELECT 
                    i.indexname AS Name,
                    i.indisunique AS IsUnique,
                    false AS IsClustered
                FROM 
                    pg_indexes i
                WHERE 
                    i.tablename = @TableName
                    AND i.schemaname = @Schema
                    AND i.indexname NOT IN (
                        SELECT 
                            c.conname
                        FROM 
                            pg_constraint c
                            JOIN pg_class cl ON c.conrelid = cl.oid
                            JOIN pg_namespace n ON cl.relnamespace = n.oid
                        WHERE 
                            cl.relname = @TableName
                            AND n.nspname = @Schema
                            AND c.contype = 'p'
                    )";

            var indexes = await connection.QueryAsync<IndexDefinition>(
                sql, 
                new { TableName = tableName, Schema = schema });

            var result = indexes.ToList();

            foreach (var index in result)
            {
                var columnSql = @"
                    SELECT 
                        a.attname as column_name
                    FROM 
                        pg_index i
                        JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
                        JOIN pg_class cl ON i.indexrelid = cl.oid
                        JOIN pg_namespace n ON cl.relnamespace = n.oid
                        JOIN pg_class tab ON i.indrelid = tab.oid
                    WHERE 
                        cl.relname = @IndexName
                        AND tab.relname = @TableName
                        AND n.nspname = @Schema
                    ORDER BY 
                        array_position(i.indkey, a.attnum)";

                var columns = await connection.QueryAsync<string>(
                    columnSql, 
                    new { IndexName = index.Name, TableName = tableName, Schema = schema });
                
                index.Columns = columns.ToList();
            }

            return result;
        }

        public async Task<List<ForeignKeyDefinition>> GetForeignKeysAsync(DbConnection connection, string tableName, string? schema = null)
        {
            schema ??= "public";

            var sql = @"
                SELECT 
                    c.conname AS Name,
                    n2.nspname AS ReferencedTableSchema,
                    c2.relname AS ReferencedTableName,
                    CASE c.confupdtype 
                        WHEN 'a' THEN 'NO ACTION'
                        WHEN 'r' THEN 'RESTRICT'
                        WHEN 'c' THEN 'CASCADE'
                        WHEN 'n' THEN 'SET NULL'
                        WHEN 'd' THEN 'SET DEFAULT'
                    END AS UpdateRule,
                    CASE c.confdeltype 
                        WHEN 'a' THEN 'NO ACTION'
                        WHEN 'r' THEN 'RESTRICT'
                        WHEN 'c' THEN 'CASCADE'
                        WHEN 'n' THEN 'SET NULL'
                        WHEN 'd' THEN 'SET DEFAULT'
                    END AS DeleteRule
                FROM 
                    pg_constraint c
                    JOIN pg_class c1 ON c.conrelid = c1.oid
                    JOIN pg_namespace n1 ON c1.relnamespace = n1.oid
                    JOIN pg_class c2 ON c.confrelid = c2.oid
                    JOIN pg_namespace n2 ON c2.relnamespace = n2.oid
                WHERE 
                    c.contype = 'f'
                    AND c1.relname = @TableName
                    AND n1.nspname = @Schema";

            var foreignKeys = await connection.QueryAsync<ForeignKeyDefinition>(
                sql, 
                new { TableName = tableName, Schema = schema });

            var result = foreignKeys.ToList();

            foreach (var fk in result)
            {
                var columnsSql = @"
                    SELECT 
                        a.attname as column_name
                    FROM 
                        pg_constraint c
                        JOIN pg_class cl ON c.conrelid = cl.oid
                        JOIN pg_namespace n ON cl.relnamespace = n.oid
                        JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY(c.conkey)
                    WHERE 
                        c.conname = @ForeignKeyName
                        AND cl.relname = @TableName
                        AND n.nspname = @Schema
                    ORDER BY 
                        array_position(c.conkey, a.attnum)";

                var columns = await connection.QueryAsync<string>(
                    columnsSql, 
                    new { ForeignKeyName = fk.Name, TableName = tableName, Schema = schema });
                
                fk.Columns = columns.ToList();

                var refColumnsSql = @"
                    SELECT 
                        a.attname as column_name
                    FROM 
                        pg_constraint c
                        JOIN pg_class cl ON c.conrelid = cl.oid
                        JOIN pg_namespace n ON cl.relnamespace = n.oid
                        JOIN pg_class cl2 ON c.confrelid = cl2.oid
                        JOIN pg_attribute a ON a.attrelid = c.confrelid AND a.attnum = ANY(c.confkey)
                    WHERE 
                        c.conname = @ForeignKeyName
                        AND cl.relname = @TableName
                        AND n.nspname = @Schema
                    ORDER BY 
                        array_position(c.confkey, a.attnum)";

                var refColumns = await connection.QueryAsync<string>(
                    refColumnsSql, 
                    new { ForeignKeyName = fk.Name, TableName = tableName, Schema = schema });
                
                fk.ReferencedColumns = refColumns.ToList();
            }

            return result;
        }

        public async Task<List<ConstraintDefinition>> GetConstraintsAsync(DbConnection connection, string tableName, string? schema = null)
        {
            schema ??= "public";

            var sql = @"
                SELECT 
                    c.conname AS Name,
                    CASE c.contype 
                        WHEN 'p' THEN 'PRIMARY KEY'
                        WHEN 'u' THEN 'UNIQUE'
                        WHEN 'c' THEN 'CHECK'
                    END AS Type,
                    pg_get_constraintdef(c.oid) AS Definition
                FROM 
                    pg_constraint c
                    JOIN pg_class cl ON c.conrelid = cl.oid
                    JOIN pg_namespace n ON cl.relnamespace = n.oid
                WHERE 
                    c.contype IN ('p', 'u', 'c')
                    AND cl.relname = @TableName
                    AND n.nspname = @Schema";

            var constraints = await connection.QueryAsync<ConstraintDefinition>(
                sql, 
                new { TableName = tableName, Schema = schema });

            var result = constraints.ToList();

            foreach (var constraint in result)
            {
                if (constraint.Type == "PRIMARY KEY" || constraint.Type == "UNIQUE")
                {
                    var columnsSql = @"
                        SELECT 
                            a.attname as column_name
                        FROM 
                            pg_constraint c
                            JOIN pg_class cl ON c.conrelid = cl.oid
                            JOIN pg_namespace n ON cl.relnamespace = n.oid
                            JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY(c.conkey)
                        WHERE 
                            c.conname = @ConstraintName
                            AND cl.relname = @TableName
                            AND n.nspname = @Schema
                        ORDER BY 
                            array_position(c.conkey, a.attnum)";

                    var columns = await connection.QueryAsync<string>(
                        columnsSql, 
                        new { ConstraintName = constraint.Name, TableName = tableName, Schema = schema });
                    
                    constraint.Columns = columns.ToList();
                }
                else if (constraint.Type == "CHECK")
                {
                    // Extract check definition from the constraint definition
                    var checkDef = constraint.Definition;
                    if (checkDef != null && checkDef.StartsWith("CHECK "))
                    {
                        checkDef = checkDef.Substring(6);
                        constraint.Definition = checkDef.Trim('(', ')');
                    }
                }
            }

            return result;
        }

        public async Task<IAsyncEnumerable<RowData>> GetTableDataAsync(DbConnection connection, string tableName, string? schema = null, string? whereClause = null, int batchSize = 1000)
        {
            schema ??= "public";
            var fullTableName = $"\"{schema}\".\"{tableName}\"";
            
            var sql = $"SELECT * FROM {fullTableName}";
            if (!string.IsNullOrEmpty(whereClause))
            {
                sql += $" WHERE {whereClause}";
            }

            var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandType = CommandType.Text;
            
            await connection.OpenAsync();
            var reader = await command.ExecuteReaderAsync();

            return new DatabaseReaderAsyncEnumerable(reader);
        }

        public async Task CreateTableAsync(DbConnection connection, TableSchema tableSchema)
        {
            var script = GenerateTableCreationScript(tableSchema);
            await connection.ExecuteAsync(script);
        }

        public async Task CreateIndexesAsync(DbConnection connection, TableSchema tableSchema)
        {
            foreach (var index in tableSchema.Indexes)
            {
                if (index.Columns.Count == 0)
                    continue;

                var uniqueness = index.IsUnique ? "UNIQUE " : "";
                var columns = string.Join(", ", index.Columns.Select(c => $"\"{c}\""));
                var fullTableName = $"\"{tableSchema.Schema}\".\"{tableSchema.Name}\"";
                
                var sql = $"CREATE {uniqueness}INDEX \"{index.Name}\" ON {fullTableName} ({columns})";
                await connection.ExecuteAsync(sql);
            }
        }

        public async Task CreateConstraintsAsync(DbConnection connection, TableSchema tableSchema)
        {
            foreach (var constraint in tableSchema.Constraints.Where(c => c.Type == "UNIQUE"))
            {
                if (constraint.Columns.Count == 0)
                    continue;

                var columns = string.Join(", ", constraint.Columns.Select(c => $"\"{c}\""));
                var fullTableName = $"\"{tableSchema.Schema}\".\"{tableSchema.Name}\"";
                
                var sql = $"ALTER TABLE {fullTableName} ADD CONSTRAINT \"{constraint.Name}\" UNIQUE ({columns})";
                await connection.ExecuteAsync(sql);
            }

            foreach (var constraint in tableSchema.Constraints.Where(c => c.Type == "CHECK"))
            {
                if (string.IsNullOrEmpty(constraint.Definition))
                    continue;

                var fullTableName = $"\"{tableSchema.Schema}\".\"{tableSchema.Name}\"";
                var sql = $"ALTER TABLE {fullTableName} ADD CONSTRAINT \"{constraint.Name}\" CHECK ({constraint.Definition})";
                await connection.ExecuteAsync(sql);
            }
        }

        public async Task CreateForeignKeysAsync(DbConnection connection, TableSchema tableSchema)
        {
            foreach (var fk in tableSchema.ForeignKeys)
            {
                if (fk.Columns.Count == 0 || fk.ReferencedColumns.Count == 0)
                    continue;
                
                var columns = string.Join(", ", fk.Columns.Select(c => $"\"{c}\""));
                var refColumns = string.Join(", ", fk.ReferencedColumns.Select(c => $"\"{c}\""));
                var fullTableName = $"\"{tableSchema.Schema}\".\"{tableSchema.Name}\"";
                var fullRefTableName = $"\"{fk.ReferencedTableSchema}\".\"{fk.ReferencedTableName}\"";
                
                var sql = $@"
                    ALTER TABLE {fullTableName} 
                    ADD CONSTRAINT ""{fk.Name}"" 
                    FOREIGN KEY ({columns}) 
                    REFERENCES {fullRefTableName} ({refColumns})";

                if (!string.IsNullOrEmpty(fk.UpdateRule) && fk.UpdateRule != "NO ACTION")
                {
                    sql += $" ON UPDATE {fk.UpdateRule}";
                }

                if (!string.IsNullOrEmpty(fk.DeleteRule) && fk.DeleteRule != "NO ACTION")
                {
                    sql += $" ON DELETE {fk.DeleteRule}";
                }

                await connection.ExecuteAsync(sql);
            }
        }

        public async Task ImportDataAsync(DbConnection connection, string tableName, string? schema, IAsyncEnumerable<RowData> data, int batchSize = 1000)
        {
            schema ??= "public";
            var fullTableName = $"\"{schema}\".\"{tableName}\"";
            
            var tableSchema = await GetTableSchemaAsync(connection, tableName, schema);
            var batch = new List<string>();
            var count = 0;
            
            await foreach (var row in data)
            {
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
            
            if (batch.Count > 0)
            {
                await ExecuteBatchAsync(connection, batch);
            }
        }

        private async Task ExecuteBatchAsync(DbConnection connection, List<string> batch)
        {
            var sql = string.Join(";\r\n", batch);
            await connection.ExecuteAsync(sql);
        }

        public string GenerateTableCreationScript(TableSchema tableSchema)
        {
            var sb = new StringBuilder();
            var fullTableName = $"\"{tableSchema.Schema}\".\"{tableSchema.Name}\"";
            
            sb.AppendLine($"CREATE TABLE {fullTableName} (");
            
            var columnDefs = new List<string>();
            foreach (var column in tableSchema.Columns)
            {
                var columnDef = $"    \"{column.Name}\" {column.DataType}";
                
                if (column.MaxLength.HasValue && column.MaxLength > 0)
                {
                    if (column.DataType.Contains("char") || column.DataType.Contains("text") || column.DataType.Contains("bytea"))
                    {
                        columnDef += $"({column.MaxLength})";
                    }
                }
                else if (column.Precision.HasValue && column.Scale.HasValue)
                {
                    columnDef += $"({column.Precision}, {column.Scale})";
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
                    // PostgreSQL uses sequences or SERIAL types
                    if (column.AdditionalProperties.TryGetValue("sequence", out var seqName))
                    {
                        // Use sequence
                        if (column.DefaultValue?.Contains(seqName) == true)
                        {
                            columnDef += $" DEFAULT {column.DefaultValue}";
                        }
                        else
                        {
                            columnDef += $" DEFAULT nextval('\"{seqName}\"')";
                        }
                    }
                    else
                    {
                        // Use SERIAL type if no sequence defined
                        var dataType = column.DataType.ToLower();
                        if (dataType == "integer")
                        {
                            columnDef = $"    \"{column.Name}\" SERIAL";
                        }
                        else if (dataType == "bigint")
                        {
                            columnDef = $"    \"{column.Name}\" BIGSERIAL";
                        }
                        else if (dataType == "smallint")
                        {
                            columnDef = $"    \"{column.Name}\" SMALLSERIAL";
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(column.DefaultValue))
                {
                    columnDef += $" DEFAULT {column.DefaultValue}";
                }
                
                columnDefs.Add(columnDef);
            }
            
            // Add primary key constraint
            var pkConstraint = tableSchema.Constraints.FirstOrDefault(c => c.Type == "PRIMARY KEY");
            if (pkConstraint != null && pkConstraint.Columns.Any())
            {
                var pkColumnNames = string.Join(", ", pkConstraint.Columns.Select(c => $"\"{c}\""));
                columnDefs.Add($"    CONSTRAINT \"{pkConstraint.Name}\" PRIMARY KEY ({pkColumnNames})");
            }
            
            sb.AppendLine(string.Join(",\r\n", columnDefs));
            sb.AppendLine(")");
            
            return sb.ToString();
        }

        public string GenerateInsertScript(TableSchema tableSchema, RowData row)
        {
            var fullTableName = $"\"{tableSchema.Schema}\".\"{tableSchema.Name}\"";
            
            var columns = row.Values.Keys.ToList();
            var columnsSql = string.Join(", ", columns.Select(c => $"\"{c}\""));
            
            var values = new List<string>();
            foreach (var column in columns)
            {
                var columnSchema = tableSchema.Columns.FirstOrDefault(c => c.Name == column);
                var value = row.Values[column];
                
                if (value == null || value == DBNull.Value)
                {
                    values.Add("NULL");
                }
                else if (value is string strValue)
                {
                    values.Add($"'{strValue.Replace("'", "''")}'");
                }
                else if (value is DateTime dateValue)
                {
                    values.Add($"'{dateValue:yyyy-MM-dd HH:mm:ss}'");
                }
                else if (value is bool boolValue)
                {
                    values.Add(boolValue ? "TRUE" : "FALSE");
                }
                else if (value is byte[] byteValue)
                {
                    var hex = BitConverter.ToString(byteValue).Replace("-", "");
                    values.Add($"'\\x{hex}'::bytea");
                }
                else
                {
                    values.Add(value.ToString()!);
                }
            }
            
            var valuesSql = string.Join(", ", values);
            
            return $"INSERT INTO {fullTableName} ({columnsSql}) VALUES ({valuesSql})";
        }

        public string EscapeIdentifier(string identifier)
        {
            return $"\"{identifier}\"";
        }

        private class DatabaseReaderAsyncEnumerable : IAsyncEnumerable<RowData>
        {
            private readonly DbDataReader _reader;

            public DatabaseReaderAsyncEnumerable(DbDataReader reader)
            {
                _reader = reader;
            }

            public IAsyncEnumerator<RowData> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                return new DatabaseReaderAsyncEnumerator(_reader, cancellationToken);
            }

            private class DatabaseReaderAsyncEnumerator : IAsyncEnumerator<RowData>
            {
                private readonly DbDataReader _reader;
                private readonly CancellationToken _cancellationToken;
                private readonly string[] _columnNames;

                public RowData Current { get; private set; } = new();

                public DatabaseReaderAsyncEnumerator(DbDataReader reader, CancellationToken cancellationToken)
                {
                    _reader = reader;
                    _cancellationToken = cancellationToken;
                    
                    var columnNames = new string[_reader.FieldCount];
                    for (var i = 0; i < _reader.FieldCount; i++)
                    {
                        columnNames[i] = _reader.GetName(i);
                    }
                    
                    _columnNames = columnNames;
                }

                public async ValueTask<bool> MoveNextAsync()
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
                    
                    return false;
                }

                public ValueTask DisposeAsync()
                {
                    _reader.Dispose();
                    return new ValueTask();
                }
            }
        }
    }
}