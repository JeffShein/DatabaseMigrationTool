using DatabaseMigrationTool.Models;
using System.Data.Common;

namespace DatabaseMigrationTool.Providers
{
    public interface IDatabaseProvider
    {
        string ProviderName { get; }
        DbConnection CreateConnection(string connectionString);
        Task<List<TableSchema>> GetTablesAsync(DbConnection connection, IEnumerable<string>? tableNames = null);
        Task<TableSchema> GetTableSchemaAsync(DbConnection connection, string tableName, string? schema = null);
        Task<List<ColumnDefinition>> GetColumnsAsync(DbConnection connection, string tableName, string? schema = null);
        Task<List<IndexDefinition>> GetIndexesAsync(DbConnection connection, string tableName, string? schema = null);
        Task<List<ForeignKeyDefinition>> GetForeignKeysAsync(DbConnection connection, string tableName, string? schema = null);
        Task<List<ConstraintDefinition>> GetConstraintsAsync(DbConnection connection, string tableName, string? schema = null);
        Task<IAsyncEnumerable<RowData>> GetTableDataAsync(DbConnection connection, string tableName, string? schema = null, string? whereClause = null, int batchSize = 1000);
        Task CreateTableAsync(DbConnection connection, TableSchema tableSchema);
        Task CreateIndexesAsync(DbConnection connection, TableSchema tableSchema);
        Task CreateConstraintsAsync(DbConnection connection, TableSchema tableSchema);
        Task CreateForeignKeysAsync(DbConnection connection, TableSchema tableSchema);
        Task ImportDataAsync(DbConnection connection, string tableName, string? schema, IAsyncEnumerable<RowData> data, int batchSize = 1000);
        string GenerateTableCreationScript(TableSchema tableSchema);
        string GenerateInsertScript(TableSchema tableSchema, RowData row);
        string EscapeIdentifier(string identifier);
        void SetLogger(Action<string> logger);
    }

    public static class DatabaseProviderFactory
    {
        private static readonly Dictionary<string, Func<IDatabaseProvider>> _providers = new()
        {
            { "sqlserver", () => new SqlServerProvider() },
            { "mysql", () => new MySqlProvider() },
            { "postgresql", () => new PostgreSqlProvider() },
            { "firebird", () => new FirebirdProvider() }
        };

        public static IDatabaseProvider Create(string providerName)
        {
            if (string.IsNullOrWhiteSpace(providerName))
            {
                throw new ArgumentException("Provider name cannot be null or empty.", nameof(providerName));
            }
            
            if (!_providers.TryGetValue(providerName.ToLower(), out var factory))
            {
                throw new ArgumentException($"Unsupported provider: {providerName}. Supported providers: {string.Join(", ", _providers.Keys)}", nameof(providerName));
            }

            return factory();
        }

        public static IEnumerable<string> GetSupportedProviders()
        {
            return _providers.Keys;
        }
    }
}