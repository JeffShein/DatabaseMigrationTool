using DatabaseMigrationTool.Constants;

namespace DatabaseMigrationTool.Utilities
{
    public static class StringUtilities
    {
        public static List<string> ParseTableNames(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new List<string>();
                
            return input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Where(name => !string.IsNullOrWhiteSpace(name))
                       .ToList();
        }
        
        public static string JoinTableNames(IEnumerable<string> tableNames)
        {
            return string.Join(", ", tableNames.Where(name => !string.IsNullOrWhiteSpace(name)));
        }
        
        public static (string schema, string tableName) ParseSchemaAndTable(string fullTableName, string defaultSchema = DatabaseConstants.SchemaNames.DefaultSqlServer)
        {
            if (string.IsNullOrWhiteSpace(fullTableName))
                return (defaultSchema, string.Empty);
                
            var parts = fullTableName.Split('.', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length switch
            {
                1 => (defaultSchema, parts[0].Trim()),
                2 => (parts[0].Trim(), parts[1].Trim()),
                _ => (defaultSchema, fullTableName.Trim())
            };
        }
        
        public static string BuildFullTableName(string schema, string tableName)
        {
            if (string.IsNullOrWhiteSpace(schema))
                return tableName;
                
            return $"{schema}.{tableName}";
        }
        
        public static string MaskConnectionString(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
                return string.Empty;
                
            var keywords = new[] { "password", "pwd", "user id", "uid", "username" };
            var result = connectionString;
            
            foreach (var keyword in keywords)
            {
                var pattern = @$"({keyword}\s*=\s*)[^;]*";
                result = System.Text.RegularExpressions.Regex.Replace(result, pattern, "$1***", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            
            return result;
        }
        
        public static string TruncateForDisplay(string text, int maxLength = 50)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;
                
            return text.Substring(0, maxLength - 3) + "...";
        }
        
        public static bool IsValidProviderName(string providerName)
        {
            return providerName switch
            {
                DatabaseConstants.ProviderNames.SqlServer => true,
                DatabaseConstants.ProviderNames.MySQL => true,
                DatabaseConstants.ProviderNames.PostgreSQL => true,
                DatabaseConstants.ProviderNames.Firebird => true,
                _ => false
            };
        }
        
        public static string GetDefaultSchema(string providerName)
        {
            return providerName switch
            {
                DatabaseConstants.ProviderNames.SqlServer => DatabaseConstants.SchemaNames.DefaultSqlServer,
                DatabaseConstants.ProviderNames.PostgreSQL => DatabaseConstants.SchemaNames.DefaultPostgreSQL,
                DatabaseConstants.ProviderNames.MySQL => DatabaseConstants.SchemaNames.DefaultMySQL,
                DatabaseConstants.ProviderNames.Firebird => DatabaseConstants.SchemaNames.DefaultFirebird,
                _ => DatabaseConstants.SchemaNames.DefaultSqlServer
            };
        }
    }
}