using DatabaseMigrationTool.Models;
using System.Data.Common;

namespace DatabaseMigrationTool.Providers
{
    /// <summary>
    /// Base class for database providers that provides common functionality
    /// </summary>
    public abstract class BaseDatabaseProvider : IDatabaseProvider
    {
        protected Action<string>? _logger;

        public abstract string ProviderName { get; }

        public virtual void SetLogger(Action<string> logger)
        {
            _logger = logger;
        }

        protected virtual void Log(string message)
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

        protected virtual void ValidateConnectionString(string connectionString, string parameterName = "connectionString")
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("Connection string cannot be null or empty.", parameterName);
            }
        }

        protected virtual string BuildTableFilter(IEnumerable<string> tableNames, string columnName, string tableAlias = "")
        {
            ArgumentNullException.ThrowIfNull(tableNames, nameof(tableNames));
            ArgumentException.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));
            
            // Validate and sanitize column name to prevent SQL injection
            if (!IsValidSqlIdentifier(columnName))
            {
                throw new ArgumentException($"Invalid column name: {columnName}. Only alphanumeric characters, underscores, and dots are allowed.", nameof(columnName));
            }
            
            // Validate table alias if provided
            if (!string.IsNullOrEmpty(tableAlias) && !IsValidSqlIdentifier(tableAlias))
            {
                throw new ArgumentException($"Invalid table alias: {tableAlias}. Only alphanumeric characters and underscores are allowed.", nameof(tableAlias));
            }
            
            var tableNamesList = tableNames.ToList();
            if (tableNamesList.Count == 0)
            {
                return string.Empty;
            }
            
            // Validate and escape table names
            var quotedNames = new List<string>();
            foreach (var tableName in tableNamesList)
            {
                if (string.IsNullOrWhiteSpace(tableName))
                {
                    continue;
                }
                
                // Allow schema.table format (with dots) but validate each part
                var parts = tableName.Split('.');
                if (parts.Length > 2)
                {
                    throw new ArgumentException($"Invalid table name format: {tableName}. Maximum of one dot allowed for schema.table format.", nameof(tableNames));
                }
                
                foreach (var part in parts)
                {
                    if (!IsValidTableNamePart(part))
                    {
                        throw new ArgumentException($"Invalid table name part: {part}. Only alphanumeric characters, underscores, and hyphens are allowed.", nameof(tableNames));
                    }
                }
                
                // Properly escape the table name for SQL
                quotedNames.Add($"'{tableName.Replace("'", "''")}'");
            }
            
            if (quotedNames.Count == 0)
            {
                return string.Empty;
            }
            
            var fullColumnName = string.IsNullOrEmpty(tableAlias) ? columnName : $"{tableAlias}.{columnName}";
            return $" AND {fullColumnName} IN ({string.Join(",", quotedNames)})";
        }
        
        /// <summary>
        /// Validates that a string contains only valid SQL identifier characters
        /// </summary>
        private static bool IsValidSqlIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return false;
            
            // Must start with letter or underscore
            if (!char.IsLetter(identifier[0]) && identifier[0] != '_')
                return false;
                
            // Allow letters, digits, underscores, and dots (for column references like table.column)
            // but limit to maximum of one dot to prevent injection through multiple dots
            int dotCount = identifier.Count(c => c == '.');
            if (dotCount > 1)
                return false;
                
            return identifier.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '.');
        }
        
        /// <summary>
        /// Validates that a table name part contains only valid characters
        /// </summary>
        private static bool IsValidTableNamePart(string namePart)
        {
            if (string.IsNullOrWhiteSpace(namePart))
                return false;
                
            // Allow letters, digits, underscores, and hyphens for table names
            return namePart.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-');
        }
        
        /// <summary>
        /// Safely escapes and validates SQL identifiers to prevent injection
        /// </summary>
        protected virtual string EscapeSqlIdentifier(string identifier, string identifierType = "identifier")
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier, nameof(identifier));
            
            // Validate the identifier contains only safe characters
            if (!IsValidSqlIdentifier(identifier))
            {
                throw new ArgumentException($"Invalid {identifierType}: {identifier}. Only alphanumeric characters, underscores, and dots are allowed.", nameof(identifier));
            }
            
            // Remove any potentially dangerous characters and limit length
            if (identifier.Length > 128)
            {
                throw new ArgumentException($"Invalid {identifierType}: {identifier}. Maximum length is 128 characters.", nameof(identifier));
            }
            
            return identifier;
        }
        
        /// <summary>
        /// Creates a safe full table name with proper escaping
        /// </summary>
        protected virtual string GetSafeTableName(string? schema, string tableName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tableName, nameof(tableName));
            
            // Validate and escape table name
            var safeTableName = EscapeSqlIdentifier(tableName, "table name");
            
            if (string.IsNullOrWhiteSpace(schema))
            {
                return $"[{safeTableName}]";
            }
            
            // Validate and escape schema name
            var safeSchema = EscapeSqlIdentifier(schema, "schema name");
            return $"[{safeSchema}].[{safeTableName}]";
        }

        // Abstract methods that must be implemented by concrete providers
        public abstract DbConnection CreateConnection(string connectionString);
        public abstract Task<List<TableSchema>> GetTablesAsync(DbConnection connection, IEnumerable<string>? tableNames = null);
        public abstract Task<TableSchema> GetTableSchemaAsync(DbConnection connection, string tableName, string? schema = null);
        public abstract Task<List<ColumnDefinition>> GetColumnsAsync(DbConnection connection, string tableName, string? schema = null);
        public abstract Task<List<IndexDefinition>> GetIndexesAsync(DbConnection connection, string tableName, string? schema = null);
        public abstract Task<List<ForeignKeyDefinition>> GetForeignKeysAsync(DbConnection connection, string tableName, string? schema = null);
        public abstract Task<List<ConstraintDefinition>> GetConstraintsAsync(DbConnection connection, string tableName, string? schema = null);
        public abstract Task<IAsyncEnumerable<RowData>> GetTableDataAsync(DbConnection connection, string tableName, string? schema = null, string? whereClause = null, int batchSize = 1000);
        public abstract Task CreateTableAsync(DbConnection connection, TableSchema tableSchema);
        public abstract Task CreateIndexesAsync(DbConnection connection, TableSchema tableSchema);
        public abstract Task CreateConstraintsAsync(DbConnection connection, TableSchema tableSchema);
        public abstract Task CreateForeignKeysAsync(DbConnection connection, TableSchema tableSchema);
        public abstract Task ImportDataAsync(DbConnection connection, string tableName, string? schema, IAsyncEnumerable<RowData> data, int batchSize = 1000);
        public abstract string GenerateTableCreationScript(TableSchema tableSchema);
        public abstract string GenerateInsertScript(TableSchema tableSchema, RowData row);
        public abstract string EscapeIdentifier(string identifier);
    }
}