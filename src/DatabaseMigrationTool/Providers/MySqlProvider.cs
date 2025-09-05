using DatabaseMigrationTool.Models;
using Dapper;
using MySqlConnector;
using System.Data;
using System.Data.Common;
using System.Text;

namespace DatabaseMigrationTool.Providers
{
    public class MySqlProvider : BaseDatabaseProvider
    {
        public override string ProviderName => "MySQL";

        public override DbConnection CreateConnection(string connectionString)
        {
            ValidateConnectionString(connectionString);
            return new MySqlConnection(connectionString);
        }

        public override async Task<List<TableSchema>> GetTablesAsync(DbConnection connection, IEnumerable<string>? tableNames = null)
        {
            string tableFilter = "";
            if (tableNames != null && tableNames.Any())
            {
                tableFilter = BuildTableFilter(tableNames, "TABLE_NAME", "t");
            }

            var sql = $@"
                SELECT 
                    t.TABLE_NAME AS Name,
                    t.TABLE_SCHEMA AS `Schema`
                FROM 
                    INFORMATION_SCHEMA.TABLES t
                WHERE 
                    t.TABLE_TYPE = 'BASE TABLE'
                    AND t.TABLE_SCHEMA NOT IN ('mysql', 'performance_schema', 'information_schema', 'sys'){tableFilter}
                ORDER BY 
                    t.TABLE_SCHEMA, t.TABLE_NAME";

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

        public override async Task<TableSchema> GetTableSchemaAsync(DbConnection connection, string tableName, string? schema = null)
        {
            var sql = @"
                SELECT 
                    t.TABLE_NAME AS Name,
                    t.TABLE_SCHEMA AS `Schema`
                FROM 
                    INFORMATION_SCHEMA.TABLES t
                WHERE 
                    t.TABLE_NAME = @TableName
                    AND t.TABLE_SCHEMA = @Schema";

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

        public override async Task<List<ColumnDefinition>> GetColumnsAsync(DbConnection connection, string tableName, string? schema = null)
        {
            var sql = @"
                SELECT 
                    c.COLUMN_NAME AS Name,
                    c.DATA_TYPE AS DataType,
                    CASE WHEN c.IS_NULLABLE = 'YES' THEN 1 ELSE 0 END AS IsNullable,
                    CASE WHEN tc.CONSTRAINT_TYPE = 'PRIMARY KEY' THEN 1 ELSE 0 END AS IsPrimaryKey,
                    CASE WHEN EXTRA = 'auto_increment' THEN 1 ELSE 0 END AS IsIdentity,
                    c.COLUMN_DEFAULT AS DefaultValue,
                    c.CHARACTER_MAXIMUM_LENGTH AS MaxLength,
                    c.NUMERIC_PRECISION AS Precision,
                    c.NUMERIC_SCALE AS Scale,
                    c.ORDINAL_POSITION AS OrdinalPosition
                FROM 
                    INFORMATION_SCHEMA.COLUMNS c
                    LEFT JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu 
                        ON c.TABLE_SCHEMA = kcu.TABLE_SCHEMA 
                        AND c.TABLE_NAME = kcu.TABLE_NAME 
                        AND c.COLUMN_NAME = kcu.COLUMN_NAME
                    LEFT JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc 
                        ON kcu.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA 
                        AND kcu.CONSTRAINT_NAME = tc.CONSTRAINT_NAME 
                        AND tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
                WHERE 
                    c.TABLE_NAME = @TableName
                    AND c.TABLE_SCHEMA = @Schema
                ORDER BY 
                    c.ORDINAL_POSITION";

            var columns = await connection.QueryAsync<ColumnDefinition>(
                sql,
                new { TableName = tableName, Schema = schema });

            return columns.ToList();
        }

        public override async Task<List<IndexDefinition>> GetIndexesAsync(DbConnection connection, string tableName, string? schema = null)
        {
            var sql = @"
                SELECT 
                    i.INDEX_NAME AS Name,
                    CASE WHEN i.NON_UNIQUE = 0 THEN 1 ELSE 0 END AS IsUnique,
                    CASE WHEN i.INDEX_TYPE = 'BTREE' AND i.INDEX_NAME = 'PRIMARY' THEN 1 ELSE 0 END AS IsClustered
                FROM 
                    INFORMATION_SCHEMA.STATISTICS i
                WHERE 
                    i.TABLE_NAME = @TableName
                    AND i.TABLE_SCHEMA = @Schema
                    AND i.INDEX_NAME != 'PRIMARY'
                GROUP BY 
                    i.INDEX_NAME, i.NON_UNIQUE, i.INDEX_TYPE";

            var indexes = await connection.QueryAsync<IndexDefinition>(
                sql, 
                new { TableName = tableName, Schema = schema });

            var result = indexes.ToList();

            foreach (var index in result)
            {
                var columnSql = @"
                    SELECT 
                        i.COLUMN_NAME
                    FROM 
                        INFORMATION_SCHEMA.STATISTICS i
                    WHERE 
                        i.INDEX_NAME = @IndexName
                        AND i.TABLE_NAME = @TableName
                        AND i.TABLE_SCHEMA = @Schema
                    ORDER BY 
                        i.SEQ_IN_INDEX";

                var columns = await connection.QueryAsync<string>(
                    columnSql, 
                    new { IndexName = index.Name, TableName = tableName, Schema = schema });
                
                index.Columns = columns.ToList();
            }

            return result;
        }

        public override async Task<List<ForeignKeyDefinition>> GetForeignKeysAsync(DbConnection connection, string tableName, string? schema = null)
        {
            var sql = @"
                SELECT 
                    rc.CONSTRAINT_NAME AS Name,
                    kcu.REFERENCED_TABLE_SCHEMA AS ReferencedTableSchema,
                    kcu.REFERENCED_TABLE_NAME AS ReferencedTableName,
                    rc.UPDATE_RULE AS UpdateRule,
                    rc.DELETE_RULE AS DeleteRule
                FROM 
                    INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
                    JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu 
                        ON rc.CONSTRAINT_SCHEMA = kcu.CONSTRAINT_SCHEMA 
                        AND rc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                WHERE 
                    kcu.TABLE_NAME = @TableName
                    AND kcu.TABLE_SCHEMA = @Schema
                GROUP BY 
                    rc.CONSTRAINT_NAME, kcu.REFERENCED_TABLE_SCHEMA, 
                    kcu.REFERENCED_TABLE_NAME, rc.UPDATE_RULE, rc.DELETE_RULE";

            var foreignKeys = await connection.QueryAsync<ForeignKeyDefinition>(
                sql, 
                new { TableName = tableName, Schema = schema });

            var result = foreignKeys.ToList();

            foreach (var fk in result)
            {
                var columnSql = @"
                    SELECT 
                        kcu.COLUMN_NAME
                    FROM 
                        INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                    WHERE 
                        kcu.CONSTRAINT_NAME = @ForeignKeyName
                        AND kcu.TABLE_NAME = @TableName
                        AND kcu.TABLE_SCHEMA = @Schema
                    ORDER BY 
                        kcu.ORDINAL_POSITION";

                var columns = await connection.QueryAsync<string>(
                    columnSql, 
                    new { ForeignKeyName = fk.Name, TableName = tableName, Schema = schema });
                
                fk.Columns = columns.ToList();

                var refColumnSql = @"
                    SELECT 
                        kcu.REFERENCED_COLUMN_NAME
                    FROM 
                        INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                    WHERE 
                        kcu.CONSTRAINT_NAME = @ForeignKeyName
                        AND kcu.TABLE_NAME = @TableName
                        AND kcu.TABLE_SCHEMA = @Schema
                    ORDER BY 
                        kcu.ORDINAL_POSITION";

                var refColumns = await connection.QueryAsync<string>(
                    refColumnSql, 
                    new { ForeignKeyName = fk.Name, TableName = tableName, Schema = schema });
                
                fk.ReferencedColumns = refColumns.ToList();
            }

            return result;
        }

        public override async Task<List<ConstraintDefinition>> GetConstraintsAsync(DbConnection connection, string tableName, string? schema = null)
        {
            var sql = @"
                SELECT 
                    tc.CONSTRAINT_NAME AS Name,
                    tc.CONSTRAINT_TYPE AS Type
                FROM 
                    INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                WHERE 
                    tc.TABLE_NAME = @TableName
                    AND tc.TABLE_SCHEMA = @Schema
                    AND tc.CONSTRAINT_TYPE IN ('PRIMARY KEY', 'UNIQUE')";

            var constraints = await connection.QueryAsync<ConstraintDefinition>(
                sql, 
                new { TableName = tableName, Schema = schema });

            var result = constraints.ToList();

            foreach (var constraint in result)
            {
                var columnSql = @"
                    SELECT 
                        kcu.COLUMN_NAME
                    FROM 
                        INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                    WHERE 
                        kcu.CONSTRAINT_NAME = @ConstraintName
                        AND kcu.TABLE_NAME = @TableName
                        AND kcu.TABLE_SCHEMA = @Schema
                    ORDER BY 
                        kcu.ORDINAL_POSITION";

                var columns = await connection.QueryAsync<string>(
                    columnSql, 
                    new { ConstraintName = constraint.Name, TableName = tableName, Schema = schema });
                
                constraint.Columns = columns.ToList();
            }

            // Add CHECK constraints (MySQL 8.0+)
            try
            {
                var checkSql = @"
                    SELECT 
                        cc.CONSTRAINT_NAME AS Name,
                        'CHECK' AS Type,
                        cc.CHECK_CLAUSE AS Definition
                    FROM 
                        INFORMATION_SCHEMA.CHECK_CONSTRAINTS cc
                        JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc 
                            ON cc.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA 
                            AND cc.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
                    WHERE 
                        tc.TABLE_NAME = @TableName
                        AND tc.TABLE_SCHEMA = @Schema
                        AND tc.CONSTRAINT_TYPE = 'CHECK'";

                var checkConstraints = await connection.QueryAsync<ConstraintDefinition>(
                    checkSql, 
                    new { TableName = tableName, Schema = schema });
                
                result.AddRange(checkConstraints);
            }
            catch
            {
                // Older MySQL versions don't support CHECK constraints
            }

            return result;
        }

        public override async Task<IAsyncEnumerable<RowData>> GetTableDataAsync(DbConnection connection, string tableName, string? schema = null, string? whereClause = null, int batchSize = 1000)
        {
            var fullTableName = $"`{schema}`.`{tableName}`";
            
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

        public override async Task CreateTableAsync(DbConnection connection, TableSchema tableSchema)
        {
            var script = GenerateTableCreationScript(tableSchema);
            await connection.ExecuteAsync(script);
        }

        public override async Task CreateIndexesAsync(DbConnection connection, TableSchema tableSchema)
        {
            foreach (var index in tableSchema.Indexes)
            {
                if (index.Columns.Count == 0)
                    continue;

                var uniqueness = index.IsUnique ? "UNIQUE " : "";
                var columns = string.Join(", ", index.Columns.Select(c => $"`{c}`"));
                var fullTableName = $"`{tableSchema.Schema}`.`{tableSchema.Name}`";
                
                var sql = $"CREATE {uniqueness}INDEX `{index.Name}` ON {fullTableName} ({columns})";
                await connection.ExecuteAsync(sql);
            }
        }

        public override async Task CreateConstraintsAsync(DbConnection connection, TableSchema tableSchema)
        {
            foreach (var constraint in tableSchema.Constraints.Where(c => c.Type == "UNIQUE"))
            {
                if (constraint.Columns.Count == 0)
                    continue;

                var columns = string.Join(", ", constraint.Columns.Select(c => $"`{c}`"));
                var fullTableName = $"`{tableSchema.Schema}`.`{tableSchema.Name}`";
                
                var sql = $"ALTER TABLE {fullTableName} ADD CONSTRAINT `{constraint.Name}` UNIQUE ({columns})";
                await connection.ExecuteAsync(sql);
            }

            // MySQL 8.0+ supports CHECK constraints
            foreach (var constraint in tableSchema.Constraints.Where(c => c.Type == "CHECK"))
            {
                if (string.IsNullOrEmpty(constraint.Definition))
                    continue;

                var fullTableName = $"`{tableSchema.Schema}`.`{tableSchema.Name}`";
                var sql = $"ALTER TABLE {fullTableName} ADD CONSTRAINT `{constraint.Name}` CHECK ({constraint.Definition})";
                
                try
                {
                    await connection.ExecuteAsync(sql);
                }
                catch
                {
                    // Older MySQL versions don't support CHECK constraints
                    Console.WriteLine($"WARNING: CHECK constraint '{constraint.Name}' not created (MySQL version may not support it)");
                }
            }
        }

        public override async Task CreateForeignKeysAsync(DbConnection connection, TableSchema tableSchema)
        {
            foreach (var fk in tableSchema.ForeignKeys)
            {
                if (fk.Columns.Count == 0 || fk.ReferencedColumns.Count == 0)
                    continue;
                
                var columns = string.Join(", ", fk.Columns.Select(c => $"`{c}`"));
                var refColumns = string.Join(", ", fk.ReferencedColumns.Select(c => $"`{c}`"));
                var fullTableName = $"`{tableSchema.Schema}`.`{tableSchema.Name}`";
                var fullRefTableName = $"`{fk.ReferencedTableSchema}`.`{fk.ReferencedTableName}`";
                
                var sql = $@"
                    ALTER TABLE {fullTableName} 
                    ADD CONSTRAINT `{fk.Name}` 
                    FOREIGN KEY ({columns}) 
                    REFERENCES {fullRefTableName} ({refColumns})";

                if (!string.IsNullOrEmpty(fk.UpdateRule))
                {
                    sql += $" ON UPDATE {fk.UpdateRule}";
                }

                if (!string.IsNullOrEmpty(fk.DeleteRule))
                {
                    sql += $" ON DELETE {fk.DeleteRule}";
                }

                await connection.ExecuteAsync(sql);
            }
        }

        public override async Task ImportDataAsync(DbConnection connection, string tableName, string? schema, IAsyncEnumerable<RowData> data, int batchSize = 1000)
        {
            var fullTableName = $"`{schema}`.`{tableName}`";
            
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

        public override string GenerateTableCreationScript(TableSchema tableSchema)
        {
            var sb = new StringBuilder();
            var fullTableName = $"`{tableSchema.Schema}`.`{tableSchema.Name}`";
            
            sb.AppendLine($"CREATE TABLE {fullTableName} (");
            
            var columnDefs = new List<string>();
            foreach (var column in tableSchema.Columns)
            {
                var columnDef = $"    `{column.Name}` {column.DataType}";
                
                if (column.MaxLength.HasValue && column.MaxLength > 0)
                {
                    if (column.DataType.Contains("char") || column.DataType.Contains("binary"))
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
                    columnDef += " AUTO_INCREMENT";
                }
                
                if (!string.IsNullOrEmpty(column.DefaultValue))
                {
                    columnDef += $" DEFAULT {column.DefaultValue}";
                }
                
                columnDefs.Add(columnDef);
            }
            
            // Add primary key constraint
            var pkColumns = tableSchema.Columns.Where(c => c.IsPrimaryKey).ToList();
            if (pkColumns.Any())
            {
                var pkColumnNames = string.Join(", ", pkColumns.Select(c => $"`{c.Name}`"));
                columnDefs.Add($"    PRIMARY KEY ({pkColumnNames})");
            }
            
            sb.AppendLine(string.Join(",\r\n", columnDefs));
            sb.AppendLine(") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci");
            
            return sb.ToString();
        }

        public override string GenerateInsertScript(TableSchema tableSchema, RowData row)
        {
            var fullTableName = $"`{tableSchema.Schema}`.`{tableSchema.Name}`";
            
            var columns = row.Values.Keys.ToList();
            var columnsSql = string.Join(", ", columns.Select(c => $"`{c}`"));
            
            var values = new List<string>();
            foreach (var column in columns)
            {
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
                    values.Add(boolValue ? "1" : "0");
                }
                else if (value is byte[] byteValue)
                {
                    var hex = BitConverter.ToString(byteValue).Replace("-", "");
                    values.Add($"0x{hex}");
                }
                else
                {
                    values.Add(value.ToString()!);
                }
            }
            
            var valuesSql = string.Join(", ", values);
            
            return $"INSERT INTO {fullTableName} ({columnsSql}) VALUES ({valuesSql})";
        }

        public override string EscapeIdentifier(string identifier)
        {
            return $"`{identifier}`";
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