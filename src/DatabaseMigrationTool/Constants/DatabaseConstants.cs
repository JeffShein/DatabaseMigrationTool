namespace DatabaseMigrationTool.Constants
{
    public static class DatabaseConstants
    {
        public const int DefaultBatchSize = 100000;
        public const int DefaultCommandTimeout = 300;
        public const int DefaultConnectionTimeout = 30;
        public const int MaxRetryAttempts = 3;
        public const int RetryDelayBaseMs = 1000;
        public const int QuickCommandTimeout = 3;
        public const int LongOperationTimeout = 300;
        public const int ConnectionCloseTimeout = 10;
        public const double LoadingAnimationDurationSeconds = 1.0;
        public const int MaxFileDisplayCount = 10;
        public const int HeaderSampleSize = 16;
        public const int DecompressedSampleSize = 32;
        public const int TextSampleSize = 100;
        public const int MinValidFileSize = 10;
        
        public static class ProviderNames
        {
            public const string SqlServer = "SqlServer";
            public const string MySQL = "MySQL";
            public const string PostgreSQL = "PostgreSQL";
            public const string Firebird = "Firebird";
        }
        
        public static class SchemaNames
        {
            public const string DefaultSqlServer = "dbo";
            public const string DefaultFirebird = "";
            public const string DefaultMySQL = "";
            public const string DefaultPostgreSQL = "public";
        }
        
        public static class FileExtensions
        {
            public const string Binary = ".bin";
            public const string Json = ".json";
            public const string Sql = ".sql";
            public const string Log = ".log";
            public const string Text = ".txt";
        }
        
        public static class FileNames
        {
            public const string ExportManifest = "export_manifest.json";
            public const string Dependencies = "dependencies.json";
            public const string Metadata = "metadata.bin";
            public const string ExportLog = "export_log.txt";
            public const string ImportLog = "import_log.txt";
        }
        
        public static class DirectoryNames
        {
            public const string Data = "data";
            public const string TableMetadata = "table_metadata";
            public const string Scripts = "scripts";
            public const string Logs = "logs";
        }
        
        public static class CompressionSignatures
        {
            public static readonly byte[] GZip = { 0x1F, 0x8B };
            public static readonly byte[] BZip2 = { (byte)'B', (byte)'Z', (byte)'h' };
        }
        
        public static class SqlKeywords
        {
            public const string SetIdentityInsert = "SET IDENTITY_INSERT";
            public const string CreateTable = "CREATE TABLE";
            public const string AlterTable = "ALTER TABLE";
            public const string CreateIndex = "CREATE INDEX";
            public const string CreateConstraint = "CONSTRAINT";
            public const string ForeignKey = "FOREIGN KEY";
        }
        
        public static class TableNamePatterns
        {
            public const string BatchFilePattern = "_batch*.bin";
            public const string MetadataFilePattern = "*.meta";
            public const string TableDataPattern = "{0}_{1}.bin";
        }
        
        public static class ConfigurationKeys
        {
            public const string ApplicationDataFolder = "DatabaseMigrationTool";
            public const string ConnectionProfilesFile = "connection_profiles.json";
            public const string PasswordKeyFile = "password.key";
            public const string ErrorLogPattern = "errors_{0:yyyyMMdd}.log";
        }
        
        public static class ProgressMessages
        {
            public const string StartingExport = "Starting database export...";
            public const string StartingImport = "Starting database import...";
            public const string ConnectingToDatabase = "Connecting to database...";
            public const string LoadingTables = "Loading table information...";
            public const string CreatingSchema = "Creating database schema...";
            public const string ExportingData = "Exporting table data...";
            public const string ImportingData = "Importing table data...";
            public const string CompletedSuccessfully = "Operation completed successfully!";
        }
        
        public static class ErrorMessages
        {
            public const string FileNotFound = "The specified file could not be found.";
            public const string DirectoryNotFound = "The specified directory could not be found.";
            public const string AccessDenied = "Access to the file or directory is denied. Please check permissions.";
            public const string ConnectionFailed = "Database connection error. Please verify your connection settings.";
            public const string InvalidTableName = "Invalid table specification. Please check table names and try again.";
            public const string OutOfMemory = "The system ran out of memory. Try reducing batch size or closing other applications.";
            public const string OperationTimeout = "The operation timed out. Please check your connection and try again.";
        }
        
        public static class SuggestedActions
        {
            public const string VerifyFilePath = "Verify the file path and ensure the file exists.";
            public const string CreateDirectory = "Create the directory or verify the path is correct.";
            public const string CheckPermissions = "Run as administrator or check file/folder permissions.";
            public const string CheckConnection = "Check your network connection and increase timeout if needed.";
            public const string VerifyConnectionString = "Verify connection string and database availability.";
            public const string CheckTableNames = "Check table names and ensure they exist in the database.";
            public const string ReduceBatchSize = "Reduce batch size, close other applications, or add more RAM.";
            public const string ContactSupport = "Review the error details and contact support if the issue persists.";
        }
        
        public static class UIConstants
        {
            public const int LoadingWindowWidth = 300;
            public const int LoadingWindowHeight = 150;
            public const int DefaultWindowWidth = 800;
            public const int DefaultWindowHeight = 600;
            public const string LoadingTableInfoTitle = "Loading Table Information...";
            public const string LoadingExportDataTitle = "Loading Export Data...";
        }
        
        public static class ValidationConstants
        {
            public const int MinBatchSize = 1;
            public const int MaxBatchSize = 1000000;
            public const int MinCommandTimeout = 30;
            public const int MaxCommandTimeout = 3600;
        }
    }
}