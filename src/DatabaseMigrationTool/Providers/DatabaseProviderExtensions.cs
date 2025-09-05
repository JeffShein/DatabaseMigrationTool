using System;
using System.Data.Common;
using System.Threading.Tasks;
using DatabaseMigrationTool.Constants;
using DatabaseMigrationTool.Models;

namespace DatabaseMigrationTool.Providers
{
    /// <summary>
    /// Extension methods for database providers
    /// </summary>
    public static class DatabaseProviderExtensions
    {
        /// <summary>
        /// Get the row count for a table
        /// </summary>
        public static Task<int> GetTableRowCountAsync(this IDatabaseProvider provider, DbConnection connection, string schema, string tableName)
        {
            // Use specific handling for Firebird since it's causing issues
            if (provider.ProviderName.Equals("Firebird", System.StringComparison.OrdinalIgnoreCase))
            {
                return GetFirebirdTableRowCountAsync(connection, tableName);
            }
            
            // For other providers
            return GetStandardTableRowCountAsync(provider, connection, schema, tableName);
        }
        
        /// <summary>
        /// Special handling for Firebird table row counts to handle permission issues
        /// </summary>
        private static async Task<int> GetFirebirdTableRowCountAsync(DbConnection connection, string tableName)
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM \"{tableName}\"";
                
                // Add a timeout to prevent long-running queries
                cmd.CommandTimeout = DatabaseConstants.QuickCommandTimeout;
                
                var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                return result != null ? System.Convert.ToInt32(result) : 0;
            }
            catch (Exception fbEx) when (
                fbEx.Message.Contains("no permission") ||
                fbEx.Message.Contains("permission denied") ||
                fbEx.Message.Contains("not authorized") ||
                fbEx.Message.Contains("access to TABLE"))
            {
                // Handle permission errors
                Console.WriteLine($"Permission error accessing table {tableName}: {fbEx.Message}");
                return -2; // Permission issue code
            }
            catch (Exception ex)
            {
                // Handle other errors
                Console.WriteLine($"Error getting row count for Firebird table {tableName}: {ex.Message}");
                return -1; // General error code
            }
        }
        
        /// <summary>
        /// Standard handling for other database providers
        /// </summary>
        private static async Task<int> GetStandardTableRowCountAsync(IDatabaseProvider provider, DbConnection connection, string schema, string tableName)
        {
            try
            {
                using var command = connection.CreateCommand();
                
                // Determine the right count query based on provider
                if (provider.ProviderName.Equals("SqlServer", System.StringComparison.OrdinalIgnoreCase))
                {
                    command.CommandText = $"SELECT COUNT(1) FROM [{schema}].[{tableName}] WITH (NOLOCK)";
                }
                else if (provider.ProviderName.Equals("MySql", System.StringComparison.OrdinalIgnoreCase))
                {
                    command.CommandText = $"SELECT COUNT(*) FROM `{tableName}`";
                }
                else if (provider.ProviderName.Equals("PostgreSql", System.StringComparison.OrdinalIgnoreCase))
                {
                    command.CommandText = $"SELECT COUNT(*) FROM \"{schema}\".\"{tableName}\"";
                }
                else
                {
                    // Generic fallback
                    command.CommandText = $"SELECT COUNT(*) FROM {schema}.{tableName}";
                }
                
                var resultScalar = await command.ExecuteScalarAsync().ConfigureAwait(false);
                return resultScalar != null ? System.Convert.ToInt32(resultScalar) : 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting row count for table {tableName}: {ex.Message}");
                return -1; // General error
            }
        }
        
        /// <summary>
        /// Get estimated table size in bytes based on average row size and row count
        /// </summary>
        public static async Task<long> EstimateTableSizeAsync(this IDatabaseProvider provider, DbConnection connection, string schema, string tableName, TableSchema tableSchema)
        {
            try
            {
                // Get row count
                int rowCount = await provider.GetTableRowCountAsync(connection, schema, tableName);
                if (rowCount == -2) // Permission issue
                    return -2; // Pass through permission issue indicator
                else if (rowCount < 0)
                    return -1; // General error indicator
                if (rowCount == 0)
                    return 0;
                
                // Calculate estimated row size based on column definitions
                int estimatedRowSize = 0;
                foreach (var column in tableSchema.Columns)
                {
                    // Add base column overhead (column header, null bit, etc.)
                    estimatedRowSize += 4; 
                    
                    // Add size based on data type
                    switch (column.DataType.ToUpperInvariant())
                    {
                        case "INT":
                        case "INTEGER":
                            estimatedRowSize += 4;
                            break;
                        case "BIGINT":
                            estimatedRowSize += 8;
                            break;
                        case "SMALLINT":
                            estimatedRowSize += 2;
                            break;
                        case "TINYINT":
                            estimatedRowSize += 1;
                            break;
                        case "BIT":
                            estimatedRowSize += 1;
                            break;
                        case "DECIMAL":
                        case "NUMERIC":
                        case "MONEY":
                            estimatedRowSize += 8;
                            break;
                        case "FLOAT":
                        case "DOUBLE":
                            estimatedRowSize += 8;
                            break;
                        case "DATE":
                        case "TIME":
                            estimatedRowSize += 4;
                            break;
                        case "DATETIME":
                        case "TIMESTAMP":
                            estimatedRowSize += 8;
                            break;
                        case "CHAR":
                        case "VARCHAR":
                        case "NVARCHAR":
                        case "TEXT":
                            // For string types, use defined max length if available, otherwise estimate
                            if (column.MaxLength.HasValue && column.MaxLength > 0)
                                estimatedRowSize += Math.Min(column.MaxLength.Value, 255);
                            else
                                estimatedRowSize += 50; // Default estimate for strings
                            break;
                        case "BINARY":
                        case "VARBINARY":
                        case "BLOB":
                            // For binary types, use defined max length or default estimate
                            if (column.MaxLength.HasValue && column.MaxLength > 0)
                                estimatedRowSize += Math.Min(column.MaxLength.Value, 255);
                            else
                                estimatedRowSize += 100; // Default estimate for binary
                            break;
                        default:
                            // For unknown types, use a reasonable default
                            estimatedRowSize += 16;
                            break;
                    }
                }
                
                // Calculate total size (row size * row count)
                long totalSizeBytes = (long)estimatedRowSize * rowCount;
                
                // Add table overhead (indexes, etc.) - rough estimate
                totalSizeBytes = (long)(totalSizeBytes * 1.2); // Add 20% for overhead
                
                return totalSizeBytes;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error estimating size for table {tableName}: {ex.Message}");
                return 0;
            }
        }
        
        /// <summary>
        /// Gets table last modification date (if available)
        /// </summary>
        public static async Task<DateTime?> GetTableLastModifiedAsync(this IDatabaseProvider provider, DbConnection connection, string schema, string tableName)
        {
            try
            {
                using var command = connection.CreateCommand();
                
                // Provider-specific queries to get last modification date
                if (provider.ProviderName.Equals("SqlServer", System.StringComparison.OrdinalIgnoreCase))
                {
                    command.CommandText = $"SELECT ISNULL(last_user_update, create_date) FROM sys.tables t JOIN sys.dm_db_index_usage_stats s ON t.object_id = s.object_id WHERE t.name = '{tableName}'";
                }
                else if (provider.ProviderName.Equals("PostgreSql", System.StringComparison.OrdinalIgnoreCase))
                {
                    command.CommandText = $"SELECT GREATEST(last_vacuum, last_autovacuum, last_analyze, last_autoanalyze) FROM pg_stat_user_tables WHERE schemaname = '{schema}' AND relname = '{tableName}'";
                }
                else if (provider.ProviderName.Equals("MySql", System.StringComparison.OrdinalIgnoreCase))
                {
                    command.CommandText = $"SELECT UPDATE_TIME FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = '{tableName}'";
                }
                else
                {
                    // For other providers including Firebird where this info might not be accessible
                    return null;
                }
                
                var result = await command.ExecuteScalarAsync();
                return result is DateTime dt ? dt : null;
            }
            catch (Exception)
            {
                // Silently fail and return null, as this is an optional statistic
                return null;
            }
        }
    }
}