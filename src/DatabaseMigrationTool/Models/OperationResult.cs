namespace DatabaseMigrationTool.Models
{
    public class OperationResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; init; }
        public Exception? Exception { get; init; }
        public string? Context { get; init; }
        
        public static OperationResult Ok() => new() { Success = true };
        
        public static OperationResult Fail(string errorMessage, Exception? exception = null, string? context = null) =>
            new() { Success = false, ErrorMessage = errorMessage, Exception = exception, Context = context };
        
        public static OperationResult Fail(Exception exception, string? context = null) =>
            new() { Success = false, ErrorMessage = exception.Message, Exception = exception, Context = context };
    }
    
    public class OperationResult<T> : OperationResult
    {
        public T? Data { get; init; }
        
        public new static OperationResult<T> Ok() => new() { Success = true };
        
        public static OperationResult<T> Ok(T data) => new() { Success = true, Data = data };
        
        public new static OperationResult<T> Fail(string errorMessage, Exception? exception = null, string? context = null) =>
            new() { Success = false, ErrorMessage = errorMessage, Exception = exception, Context = context };
        
        public new static OperationResult<T> Fail(Exception exception, string? context = null) =>
            new() { Success = false, ErrorMessage = exception.Message, Exception = exception, Context = context };
    }
    
    public class ExportResult : OperationResult
    {
        public int TablesExported { get; init; }
        public long TotalRows { get; init; }
        public long TotalBytes { get; init; }
        public TimeSpan Duration { get; init; }
        public string? OutputPath { get; init; }
        
        public static ExportResult Create(int tablesExported, long totalRows, long totalBytes, TimeSpan duration, string outputPath)
        {
            var result = new ExportResult
            {
                TablesExported = tablesExported,
                TotalRows = totalRows,
                TotalBytes = totalBytes,
                Duration = duration,
                OutputPath = outputPath
            };
            ((OperationResult)result).Success = true;
            return result;
        }
        
        public new static ExportResult Fail(string errorMessage, Exception? exception = null, string? context = null)
        {
            var result = new ExportResult { ErrorMessage = errorMessage, Exception = exception, Context = context };
            ((OperationResult)result).Success = false;
            return result;
        }
        
        public new static ExportResult Fail(Exception exception, string? context = null)
        {
            var result = new ExportResult { ErrorMessage = exception.Message, Exception = exception, Context = context };
            ((OperationResult)result).Success = false;
            return result;
        }
    }
    
    public class ImportResult : OperationResult
    {
        public int TablesImported { get; init; }
        public long RowsImported { get; init; }
        public TimeSpan Duration { get; init; }
        
        public static ImportResult Create(int tablesImported, long rowsImported, TimeSpan duration)
        {
            var result = new ImportResult
            {
                TablesImported = tablesImported,
                RowsImported = rowsImported,
                Duration = duration
            };
            ((OperationResult)result).Success = true;
            return result;
        }
        
        public new static ImportResult Fail(string errorMessage, Exception? exception = null, string? context = null)
        {
            var result = new ImportResult { ErrorMessage = errorMessage, Exception = exception, Context = context };
            ((OperationResult)result).Success = false;
            return result;
        }
        
        public new static ImportResult Fail(Exception exception, string? context = null)
        {
            var result = new ImportResult { ErrorMessage = exception.Message, Exception = exception, Context = context };
            ((OperationResult)result).Success = false;
            return result;
        }
    }
    
    public class ValidationResult : OperationResult
    {
        public List<string> Warnings { get; init; } = new();
        public List<string> Errors { get; init; } = new();
        
        public bool HasWarnings => Warnings.Any();
        public bool HasErrors => Errors.Any();
        
        public static ValidationResult Valid() => new() { Success = true };
        
        public static ValidationResult Invalid(IEnumerable<string> errors, IEnumerable<string>? warnings = null) =>
            new()
            {
                Success = false,
                Errors = errors.ToList(),
                Warnings = warnings?.ToList() ?? new List<string>(),
                ErrorMessage = $"Validation failed with {errors.Count()} errors"
            };
        
        public ValidationResult AddWarning(string warning)
        {
            Warnings.Add(warning);
            return this;
        }
        
        public ValidationResult AddError(string error)
        {
            Errors.Add(error);
            return this;
        }
    }
    
    public class ConnectionResult : OperationResult<System.Data.Common.DbConnection>
    {
        public string? ConnectionString { get; init; }
        public string? ProviderName { get; init; }
        
        public static ConnectionResult Create(System.Data.Common.DbConnection connection, string connectionString, string providerName)
        {
            var result = new ConnectionResult
            {
                Data = connection,
                ConnectionString = connectionString,
                ProviderName = providerName
            };
            ((OperationResult)result).Success = true;
            return result;
        }
        
        public new static ConnectionResult Fail(string errorMessage, Exception? exception = null, string? context = null)
        {
            var result = new ConnectionResult { ErrorMessage = errorMessage, Exception = exception, Context = context };
            ((OperationResult)result).Success = false;
            return result;
        }
        
        public new static ConnectionResult Fail(Exception exception, string? context = null)
        {
            var result = new ConnectionResult { ErrorMessage = exception.Message, Exception = exception, Context = context };
            ((OperationResult)result).Success = false;
            return result;
        }
    }
}