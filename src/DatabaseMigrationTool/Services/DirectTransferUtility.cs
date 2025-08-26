using DatabaseMigrationTool.Providers;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;

namespace DatabaseMigrationTool.Services
{
    /// <summary>
    /// Utility class for direct database table transfer between source and destination
    /// Bypasses the normal batch export/import process for maximum reliability
    /// </summary>
    public static class DirectTransferUtility
    {
        private static Action<string>? _logger;
        
        public static void SetLogger(Action<string> logger)
        {
            _logger = logger;
        }
        
        /// <summary>
        /// Directly transfers data from source to destination without batch files
        /// </summary>
        public static async Task DirectTransferTableAsync(
            IDatabaseProvider provider,
            string sourceConnectionString,
            string destinationConnectionString,
            string schemaName,
            string tableName,
            int? limit = null)
        {
            if (string.IsNullOrEmpty(sourceConnectionString) || 
                string.IsNullOrEmpty(destinationConnectionString) ||
                string.IsNullOrEmpty(tableName))
            {
                Log("Error: Missing required parameters");
                return;
            }
            
            Log($"Starting direct transfer for {schemaName}.{tableName}");
            
            // Create connections to both databases
            using var sourceConnection = provider.CreateConnection(sourceConnectionString);
            using var destConnection = provider.CreateConnection(destinationConnectionString);
            
            try
            {
                await sourceConnection.OpenAsync();
                Log($"Connected to source database: {sourceConnection.Database}");
                
                await destConnection.OpenAsync();
                Log($"Connected to destination database: {destConnection.Database}");
                
                // Get table schema
                var tableSchema = await provider.GetTableSchemaAsync(sourceConnection, tableName, schemaName);
                
                if (tableSchema == null)
                {
                    Log($"Error: Table {schemaName}.{tableName} not found in source database");
                    return;
                }
                
                Log($"Retrieved schema for {tableSchema.FullName} with {tableSchema.Columns.Count} columns");
                
                // Create table in destination if needed
                Log("Creating table in destination if needed...");
                await provider.CreateTableAsync(destConnection, tableSchema);
                
                // Read source data and write to destination in batches
                // We'll use direct ADO.NET for this to ensure maximum reliability
                await TransferDataDirectlyAsync(provider, sourceConnection, destConnection, tableSchema, limit);
            }
            catch (Exception ex)
            {
                Log($"Error during direct transfer: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Log($"Inner error: {ex.InnerException.Message}");
                }
            }
            finally
            {
                if (sourceConnection.State == ConnectionState.Open)
                    await sourceConnection.CloseAsync();
                    
                if (destConnection.State == ConnectionState.Open)
                    await destConnection.CloseAsync();
            }
        }
        
        private static async Task TransferDataDirectlyAsync(
            IDatabaseProvider provider,
            DbConnection sourceConnection,
            DbConnection destConnection,
            Models.TableSchema tableSchema,
            int? limit = null)
        {
            // Construct query that explicitly names each column to avoid any confusion
            var columnNames = new List<string>();
            foreach (var column in tableSchema.Columns)
            {
                columnNames.Add(column.Name);
            }
            
            string columnList = string.Join(", ", columnNames.ConvertAll(c => $"[{c}]"));
            
            // Create a direct SQL query with SET NOCOUNT ON to ensure clean results
            string sqlQuery = $"SET NOCOUNT ON; SELECT {columnList} FROM [{tableSchema.Schema}].[{tableSchema.Name}]";
            
            // Add limit if specified
            if (limit.HasValue)
            {
                if (provider.ProviderName.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                {
                    sqlQuery += $" ORDER BY (SELECT NULL) OFFSET 0 ROWS FETCH NEXT {limit.Value} ROWS ONLY";
                }
                else if (provider.ProviderName.Equals("MySQL", StringComparison.OrdinalIgnoreCase))
                {
                    sqlQuery += $" LIMIT {limit.Value}";
                }
                else if (provider.ProviderName.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
                {
                    sqlQuery += $" LIMIT {limit.Value}";
                }
            }
            
            Log($"Executing query: {sqlQuery}");
            
            // Execute reader on source
            using var command = sourceConnection.CreateCommand();
            command.CommandText = sqlQuery;
            command.CommandTimeout = 600; // 10 minute timeout
            
            using var reader = await command.ExecuteReaderAsync();
            
            if (!reader.HasRows)
            {
                Log("No rows returned from source query");
                return;
            }
            
            Log($"Source query returned rows, starting data transfer");
            
            // Create a batch command for efficient insertion
            var insertSqlSb = new System.Text.StringBuilder();
            insertSqlSb.AppendLine($"INSERT INTO [{tableSchema.Schema}].[{tableSchema.Name}] ({columnList}) VALUES ");
            
            // Prepare field array for data
            var fieldCount = reader.FieldCount;
            object[] values = new object[fieldCount];
            
            Log($"Field count: {fieldCount}");
            
            // Process rows in batches
            int batchSize = 100;
            int rowsProcessed = 0;
            int totalRowsProcessed = 0;
            var valueSets = new List<string>();
            
            // Read and process rows
            while (await reader.ReadAsync())
            {
                if (rowsProcessed == 0)
                {
                    // Log field names for first row
                    for (int i = 0; i < fieldCount; i++)
                    {
                        string fieldName = reader.GetName(i);
                        Log($"Field {i}: {fieldName}");
                    }
                }
                
                // Get all field values
                reader.GetValues(values);
                
                // Create values part of the SQL
                var valuesSql = FormatValuesForInsert(values);
                valueSets.Add($"({valuesSql})");
                
                rowsProcessed++;
                totalRowsProcessed++;
                
                // Execute in batches
                if (rowsProcessed >= batchSize)
                {
                    await ExecuteInsertBatchAsync(provider, destConnection, insertSqlSb.ToString(), valueSets);
                    valueSets.Clear();
                    rowsProcessed = 0;
                    Log($"Processed {totalRowsProcessed} rows");
                }
            }
            
            // Insert any remaining rows
            if (valueSets.Count > 0)
            {
                await ExecuteInsertBatchAsync(provider, destConnection, insertSqlSb.ToString(), valueSets);
                Log($"Processed {totalRowsProcessed} rows total");
            }
            
            Log($"Direct transfer completed. Total rows transferred: {totalRowsProcessed}");
        }
        
        private static async Task ExecuteInsertBatchAsync(
            IDatabaseProvider provider, 
            DbConnection connection, 
            string insertSqlPrefix,
            List<string> valueSets)
        {
            if (valueSets.Count == 0)
                return;
                
            // Build full SQL with values
            string sql = insertSqlPrefix + string.Join(",\n", valueSets);
            
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 600; // 10 minute timeout
            
            try
            {
                int rowsAffected = await command.ExecuteNonQueryAsync();
                Log($"Insert batch completed: {rowsAffected} rows affected");
            }
            catch (Exception ex)
            {
                Log($"Error executing insert batch: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Log($"Inner exception: {ex.InnerException.Message}");
                }
                
                // Try inserting one by one if batch insert fails
                Log("Attempting row-by-row insert as fallback");
                int successCount = 0;
                
                foreach (var valueSet in valueSets)
                {
                    try
                    {
                        string singleInsertSql = insertSqlPrefix + valueSet;
                        using var singleCommand = connection.CreateCommand();
                        singleCommand.CommandText = singleInsertSql;
                        await singleCommand.ExecuteNonQueryAsync();
                        successCount++;
                    }
                    catch (Exception innerEx)
                    {
                        Log($"Error inserting single row: {innerEx.Message}");
                    }
                }
                
                Log($"Row-by-row insert completed: {successCount} of {valueSets.Count} rows successful");
            }
        }
        
        private static string FormatValuesForInsert(object[] values)
        {
            var formattedValues = new List<string>();
            
            foreach (var value in values)
            {
                if (value == null || value == DBNull.Value)
                {
                    formattedValues.Add("NULL");
                }
                else if (value is string strValue)
                {
                    // Escape single quotes for SQL
                    formattedValues.Add($"'{strValue.Replace("'", "''")}'");
                }
                else if (value is DateTime dateValue)
                {
                    formattedValues.Add($"'{dateValue:yyyy-MM-dd HH:mm:ss.fff}'");
                }
                else if (value is bool boolValue)
                {
                    formattedValues.Add(boolValue ? "1" : "0");
                }
                else if (value is byte[] byteValue)
                {
                    formattedValues.Add($"0x{BitConverter.ToString(byteValue).Replace("-", "")}");
                }
                else
                {
                    formattedValues.Add(value?.ToString() ?? "NULL");
                }
            }
            
            return string.Join(", ", formattedValues);
        }
        
        private static void Log(string message)
        {
            _logger?.Invoke(message);
        }
    }
}