using DatabaseMigrationTool.Constants;
using DatabaseMigrationTool.Utilities;

namespace DatabaseMigrationTool.Tests.Utilities
{
    public class StringUtilitiesTests
    {
        [Fact]
        public void ParseTableNames_WithValidInput_ReturnsCorrectList()
        {
            // Arrange
            const string input = "Table1, Table2, Table3";
            
            // Act
            var result = StringUtilities.ParseTableNames(input);
            
            // Assert
            Assert.Equal(3, result.Count);
            Assert.Contains("Table1", result);
            Assert.Contains("Table2", result);
            Assert.Contains("Table3", result);
        }
        
        [Fact]
        public void ParseTableNames_WithEmptyInput_ReturnsEmptyList()
        {
            // Arrange
            const string input = "";
            
            // Act
            var result = StringUtilities.ParseTableNames(input);
            
            // Assert
            Assert.Empty(result);
        }
        
        [Fact]
        public void ParseTableNames_WithNullInput_ReturnsEmptyList()
        {
            // Arrange
            string? input = null;
            
            // Act
            var result = StringUtilities.ParseTableNames(input);
            
            // Assert
            Assert.Empty(result);
        }
        
        [Theory]
        [InlineData("schema.table", "schema", "table")]
        [InlineData("table", "dbo", "table")]
        [InlineData("schema.table.extra", "dbo", "schema.table.extra")]
        public void ParseSchemaAndTable_WithVariousInputs_ReturnsCorrectParts(string input, string expectedSchema, string expectedTable)
        {
            // Act
            var (schema, tableName) = StringUtilities.ParseSchemaAndTable(input);
            
            // Assert
            Assert.Equal(expectedSchema, schema);
            Assert.Equal(expectedTable, tableName);
        }
        
        [Fact]
        public void BuildFullTableName_WithSchemaAndTable_ReturnsCorrectFormat()
        {
            // Arrange
            const string schema = "TestSchema";
            const string tableName = "TestTable";
            
            // Act
            var result = StringUtilities.BuildFullTableName(schema, tableName);
            
            // Assert
            Assert.Equal("TestSchema.TestTable", result);
        }
        
        [Fact]
        public void MaskConnectionString_WithPassword_MasksPassword()
        {
            // Arrange
            const string connectionString = "Server=localhost;Database=TestDB;User=testuser;Password=secret123;";
            
            // Act
            var result = StringUtilities.MaskConnectionString(connectionString);
            
            // Assert
            Assert.Contains("Password=***", result);
            Assert.DoesNotContain("secret123", result);
        }
        
        [Theory]
        [InlineData(DatabaseConstants.ProviderNames.SqlServer, true)]
        [InlineData(DatabaseConstants.ProviderNames.MySQL, true)]
        [InlineData(DatabaseConstants.ProviderNames.PostgreSQL, true)]
        [InlineData(DatabaseConstants.ProviderNames.Firebird, true)]
        [InlineData("InvalidProvider", false)]
        [InlineData("", false)]
        public void IsValidProviderName_WithVariousProviders_ReturnsCorrectResult(string providerName, bool expected)
        {
            // Act
            var result = StringUtilities.IsValidProviderName(providerName);
            
            // Assert
            Assert.Equal(expected, result);
        }
        
        [Theory]
        [InlineData(DatabaseConstants.ProviderNames.SqlServer, DatabaseConstants.SchemaNames.DefaultSqlServer)]
        [InlineData(DatabaseConstants.ProviderNames.PostgreSQL, DatabaseConstants.SchemaNames.DefaultPostgreSQL)]
        [InlineData(DatabaseConstants.ProviderNames.MySQL, DatabaseConstants.SchemaNames.DefaultMySQL)]
        [InlineData(DatabaseConstants.ProviderNames.Firebird, DatabaseConstants.SchemaNames.DefaultFirebird)]
        public void GetDefaultSchema_WithValidProvider_ReturnsCorrectSchema(string providerName, string expectedSchema)
        {
            // Act
            var result = StringUtilities.GetDefaultSchema(providerName);
            
            // Assert
            Assert.Equal(expectedSchema, result);
        }
    }
}