using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Services;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace DatabaseMigrationTool.Utilities
{
    public enum ErrorSeverity
    {
        Warning,
        Error,
        Critical
    }
    
    public enum ErrorCategory
    {
        Unknown,
        Database,
        FileSystem,
        Network,
        Memory,
        Configuration,
        Validation,
        Security,
        UserInterface
    }
    
    public class ErrorAnalysis
    {
        public int TotalErrors { get; set; }
        public TimeSpan TimeWindow { get; set; }
        public DateTime AnalysisTimestamp { get; set; }
        public Dictionary<ErrorCategory, int> ErrorsByCategory { get; set; } = new();
        public Dictionary<ErrorSeverity, int> ErrorsBySeverity { get; set; } = new();
        public Dictionary<string, int> MostCommonErrors { get; set; } = new();
        public Dictionary<DateTime, int> ErrorTrends { get; set; } = new();
        public double? RecoveryRate { get; set; }
    }

    public class ErrorInfo
    {
        public string Message { get; set; } = string.Empty;
        public string? Details { get; set; }
        public Exception? Exception { get; set; }
        public ErrorSeverity Severity { get; set; }
        public ErrorCategory Category { get; set; } = ErrorCategory.Unknown;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string? Context { get; set; }
        public bool IsRecoverable { get; set; } = true;
        public string? SuggestedAction { get; set; }
        public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
        public Dictionary<string, object> Properties { get; set; } = new();
        public int RetryCount { get; set; } = 0;
        public string? OperationId { get; set; }
        public string? UserId { get; set; }
    }

    public static class ErrorHandler
    {
        private static readonly List<ErrorInfo> _errorHistory = new();
        private static string? _logFilePath;
        private static ILogger? _logger;
        private static readonly object _lockObject = new();

        public static void Initialize(string? logFilePath = null)
        {
            if (!string.IsNullOrEmpty(logFilePath))
            {
                _logFilePath = logFilePath;
            }
            else
            {
                var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DatabaseMigrationTool");
                Directory.CreateDirectory(appDataPath);
                _logFilePath = Path.Combine(appDataPath, $"errors_{DateTime.Now:yyyyMMdd}.log");
            }
            
            // Initialize structured logging
            var loggerFactory = LoggingService.CreateLoggerFactory();
            _logger = loggerFactory.CreateLogger("ErrorHandler");
        }

        public static ErrorInfo HandleError(Exception ex, string context, ErrorSeverity severity = ErrorSeverity.Error, bool showUserMessage = true, string? operationId = null, string? userId = null)
        {
            ArgumentNullException.ThrowIfNull(ex);
            ArgumentNullException.ThrowIfNull(context);
            
            var errorInfo = new ErrorInfo
            {
                Message = GetUserFriendlyMessage(ex),
                Details = ex.ToString(),
                Exception = ex,
                Severity = severity,
                Category = CategorizeError(ex),
                Context = context,
                IsRecoverable = DetermineRecoverability(ex),
                SuggestedAction = GetSuggestedAction(ex),
                OperationId = operationId,
                UserId = userId
            };
            
            // Add exception-specific properties
            EnrichErrorWithExceptionData(errorInfo, ex);

            return HandleError(errorInfo, showUserMessage);
        }

        public static ErrorInfo HandleError(ErrorInfo errorInfo, bool showUserMessage = true)
        {
            ArgumentNullException.ThrowIfNull(errorInfo);
            
            lock (_lockObject)
            {
                // Add to error history
                _errorHistory.Add(errorInfo);
            }

            // Log with structured logging
            LogStructured(errorInfo);
            
            // Log to file (legacy support)
            LogToFile(errorInfo);

            // Show user message if requested
            if (showUserMessage)
            {
                ShowUserMessage(errorInfo);
            }

            return errorInfo;
        }

        public static async Task<bool> TryRecoverAsync(ErrorInfo errorInfo, Func<Task<bool>> retryAction, int maxRetries = 3)
        {
            ArgumentNullException.ThrowIfNull(errorInfo);
            ArgumentNullException.ThrowIfNull(retryAction);
            
            if (!errorInfo.IsRecoverable)
            {
                LogStructured(new ErrorInfo
                {
                    Message = "Recovery not attempted - error is not recoverable",
                    Context = errorInfo.Context,
                    Severity = ErrorSeverity.Warning,
                    CorrelationId = errorInfo.CorrelationId,
                    Category = errorInfo.Category
                });
                return false;
            }

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Enhanced exponential backoff with jitter
                    var baseDelay = 1000 * Math.Pow(2, attempt - 1);
                    var jitter = new Random().Next(0, (int)(baseDelay * 0.1));
                    await Task.Delay((int)baseDelay + jitter);
                    
                    errorInfo.RetryCount = attempt;
                    
                    if (await retryAction())
                    {
                        var recoveryInfo = new ErrorInfo
                        {
                            Message = $"Recovery successful after {attempt} attempts",
                            Context = errorInfo.Context,
                            Severity = ErrorSeverity.Warning,
                            CorrelationId = errorInfo.CorrelationId,
                            Category = errorInfo.Category,
                            RetryCount = attempt
                        };
                        recoveryInfo.Properties["RecoveryAttempts"] = attempt;
                        recoveryInfo.Properties["OriginalErrorId"] = errorInfo.CorrelationId;
                        
                        LogStructured(recoveryInfo);
                        return true;
                    }
                }
                catch (Exception retryEx)
                {
                    var retryErrorInfo = new ErrorInfo
                    {
                        Message = $"Retry attempt {attempt} of {maxRetries} failed: {retryEx.Message}",
                        Context = errorInfo.Context,
                        Severity = ErrorSeverity.Warning,
                        Exception = retryEx,
                        CorrelationId = errorInfo.CorrelationId,
                        Category = CategorizeError(retryEx),
                        RetryCount = attempt
                    };
                    retryErrorInfo.Properties["RetryAttempt"] = attempt;
                    retryErrorInfo.Properties["MaxRetries"] = maxRetries;
                    retryErrorInfo.Properties["OriginalErrorId"] = errorInfo.CorrelationId;
                    
                    LogStructured(retryErrorInfo);
                }
            }

            var failureInfo = new ErrorInfo
            {
                Message = $"Recovery failed after {maxRetries} attempts",
                Context = errorInfo.Context,
                Severity = ErrorSeverity.Error,
                CorrelationId = errorInfo.CorrelationId,
                Category = errorInfo.Category,
                RetryCount = maxRetries
            };
            failureInfo.Properties["MaxRetriesReached"] = true;
            failureInfo.Properties["OriginalErrorId"] = errorInfo.CorrelationId;
            
            LogStructured(failureInfo);
            return false;
        }

        public static List<ErrorInfo> GetErrorHistory(ErrorSeverity? minSeverity = null)
        {
            if (minSeverity == null)
            {
                return new List<ErrorInfo>(_errorHistory);
            }

            return _errorHistory.FindAll(e => e.Severity >= minSeverity);
        }

        public static void ClearErrorHistory()
        {
            lock (_lockObject)
            {
                _errorHistory.Clear();
            }
        }
        
        public static void RegisterUnhandledExceptionHandlers()
        {
            // Handle unhandled exceptions in the current AppDomain
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                if (ex != null)
                {
                    HandleError(ex, "Unhandled AppDomain Exception", ErrorSeverity.Critical, showUserMessage: false);
                    
                    // Try to show a final error message to the user
                    try
                    {
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show(
                                $"A critical error has occurred and the application must close.\\n\\nError: {ex.Message}\\n\\nDetails have been logged for investigation.",
                                "Critical Application Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Stop);
                        });
                    }
                    catch
                    {
                        // Last resort - can't even show message box
                    }
                }
            };
            
            // Handle unhandled exceptions in WPF dispatcher threads
            if (Application.Current != null)
            {
                Application.Current.DispatcherUnhandledException += (sender, args) =>
                {
                    var errorInfo = HandleError(args.Exception, "Unhandled WPF Dispatcher Exception", ErrorSeverity.Critical, showUserMessage: false);
                    
                    // Determine if we can recover from this exception
                    if (errorInfo.IsRecoverable)
                    {
                        args.Handled = true; // Prevent application shutdown
                        ShowUserMessage(errorInfo);
                    }
                    else
                    {
                        // Let the application crash for critical errors
                        args.Handled = false;
                    }
                };
            }
            
            // Handle unhandled exceptions in Task threads
            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                HandleError(args.Exception, "Unhandled Task Exception", ErrorSeverity.Error, showUserMessage: false);
                args.SetObserved(); // Prevent process termination
            };
        }
        
        public static ErrorAnalysis AnalyzeErrorPatterns(TimeSpan? timeWindow = null)
        {
            var window = timeWindow ?? TimeSpan.FromHours(24);
            var cutoff = DateTime.Now - window;
            
            List<ErrorInfo> relevantErrors;
            lock (_lockObject)
            {
                relevantErrors = _errorHistory.Where(e => e.Timestamp >= cutoff).ToList();
            }
            
            var analysis = new ErrorAnalysis
            {
                TotalErrors = relevantErrors.Count,
                TimeWindow = window,
                AnalysisTimestamp = DateTime.Now
            };
            
            if (relevantErrors.Count == 0)
            {
                return analysis;
            }
            
            // Category breakdown
            analysis.ErrorsByCategory = relevantErrors
                .GroupBy(e => e.Category)
                .ToDictionary(g => g.Key, g => g.Count());
            
            // Severity breakdown
            analysis.ErrorsBySeverity = relevantErrors
                .GroupBy(e => e.Severity)
                .ToDictionary(g => g.Key, g => g.Count());
            
            // Most common errors
            analysis.MostCommonErrors = relevantErrors
                .GroupBy(e => e.Message)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .ToDictionary(g => g.Key, g => g.Count());
            
            // Recovery rate
            var recoverableErrors = relevantErrors.Where(e => e.IsRecoverable).ToList();
            if (recoverableErrors.Count > 0)
            {
                var successfulRecoveries = recoverableErrors.Count(e => e.RetryCount > 0);
                analysis.RecoveryRate = (double)successfulRecoveries / recoverableErrors.Count;
            }
            
            // Error trends
            analysis.ErrorTrends = relevantErrors
                .GroupBy(e => e.Timestamp.Date)
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.Count());
            
            return analysis;
        }
        
        public static string GenerateErrorReport(ErrorAnalysis? analysis = null)
        {
            analysis ??= AnalyzeErrorPatterns();
            
            var report = new StringBuilder();
            report.AppendLine("=== ERROR ANALYSIS REPORT ===");
            report.AppendLine($"Generated: {analysis.AnalysisTimestamp:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine($"Time Window: {analysis.TimeWindow.TotalHours:F1} hours");
            report.AppendLine($"Total Errors: {analysis.TotalErrors}");
            report.AppendLine();
            
            if (analysis.TotalErrors == 0)
            {
                report.AppendLine("No errors found in the specified time window.");
                return report.ToString();
            }
            
            report.AppendLine("ERRORS BY CATEGORY:");
            foreach (var kvp in analysis.ErrorsByCategory.OrderByDescending(x => x.Value))
            {
                report.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }
            report.AppendLine();
            
            report.AppendLine("ERRORS BY SEVERITY:");
            foreach (var kvp in analysis.ErrorsBySeverity.OrderByDescending(x => x.Value))
            {
                report.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }
            report.AppendLine();
            
            report.AppendLine("MOST COMMON ERRORS:");
            foreach (var kvp in analysis.MostCommonErrors)
            {
                report.AppendLine($"  [{kvp.Value}x] {kvp.Key}");
            }
            report.AppendLine();
            
            if (analysis.RecoveryRate.HasValue)
            {
                report.AppendLine($"RECOVERY RATE: {analysis.RecoveryRate.Value:P1}");
                report.AppendLine();
            }
            
            return report.ToString();
        }

        private static ErrorCategory CategorizeError(Exception ex)
        {
            return ex switch
            {
                // Database-related exceptions (most specific first)
                System.Data.Common.DbException => ErrorCategory.Database,
                InvalidOperationException when ex.Message.Contains("connection") => ErrorCategory.Database,
                TimeoutException when ex.Message.Contains("database") || ex.Message.Contains("connection") => ErrorCategory.Database,
                
                // Security exceptions (before general UnauthorizedAccessException)
                System.Security.SecurityException => ErrorCategory.Security,
                
                // File system exceptions
                FileNotFoundException => ErrorCategory.FileSystem,
                DirectoryNotFoundException => ErrorCategory.FileSystem,
                PathTooLongException => ErrorCategory.FileSystem,
                UnauthorizedAccessException => ErrorCategory.FileSystem, // File system permission issues
                IOException => ErrorCategory.FileSystem,
                
                // Network exceptions (specific network types first)
                System.Net.NetworkInformation.NetworkInformationException => ErrorCategory.Network,
                System.Net.Sockets.SocketException => ErrorCategory.Network,
                System.Net.WebException => ErrorCategory.Network,
                
                // Memory exceptions (specific first)
                InsufficientMemoryException => ErrorCategory.Memory,
                OutOfMemoryException => ErrorCategory.Memory,
                
                // Configuration exceptions (specific first)
                System.Configuration.ConfigurationException => ErrorCategory.Configuration,
                ArgumentException when ex.Message.Contains("configuration") => ErrorCategory.Configuration,
                
                // Validation exceptions (specific argument exceptions)
                ArgumentNullException => ErrorCategory.Validation,
                ArgumentOutOfRangeException => ErrorCategory.Validation,
                ArgumentException => ErrorCategory.Validation, // General ArgumentException last
                FormatException => ErrorCategory.Validation,
                
                // UI exceptions
                InvalidOperationException when ex.Message.Contains("thread") => ErrorCategory.UserInterface,
                
                // General timeout (after specific timeouts)
                TimeoutException => ErrorCategory.Network,
                
                _ => ErrorCategory.Unknown
            };
        }
        
        private static void EnrichErrorWithExceptionData(ErrorInfo errorInfo, Exception ex)
        {
            // Add common properties
            errorInfo.Properties["ExceptionType"] = ex.GetType().Name;
            errorInfo.Properties["ExceptionSource"] = ex.Source ?? "Unknown";
            
            if (ex.Data != null && ex.Data.Count > 0)
            {
                foreach (var key in ex.Data.Keys)
                {
                    if (key != null)
                    {
                        errorInfo.Properties[$"ExceptionData_{key}"] = ex.Data[key]?.ToString() ?? "null";
                    }
                }
            }
            
            // Add category-specific enrichment
            switch (errorInfo.Category)
            {
                case ErrorCategory.Database:
                    if (ex is System.Data.Common.DbException dbEx)
                    {
                        errorInfo.Properties["DatabaseErrorCode"] = dbEx.ErrorCode;
                        if (!string.IsNullOrEmpty(dbEx.SqlState))
                        {
                            errorInfo.Properties["SqlState"] = dbEx.SqlState;
                        }
                    }
                    break;
                    
                case ErrorCategory.FileSystem:
                    if (ex is FileNotFoundException fileEx)
                    {
                        errorInfo.Properties["FileName"] = fileEx.FileName ?? "Unknown";
                    }
                    break;
                    
                case ErrorCategory.Network:
                    if (ex is System.Net.Sockets.SocketException sockEx)
                    {
                        errorInfo.Properties["SocketErrorCode"] = sockEx.SocketErrorCode;
                    }
                    break;
            }
        }
        
        private static void LogStructured(ErrorInfo errorInfo)
        {
            if (_logger == null) return;
            
            using (LogContext.PushProperty("CorrelationId", errorInfo.CorrelationId))
            using (LogContext.PushProperty("ErrorCategory", errorInfo.Category))
            using (LogContext.PushProperty("Context", errorInfo.Context))
            using (LogContext.PushProperty("IsRecoverable", errorInfo.IsRecoverable))
            using (LogContext.PushProperty("RetryCount", errorInfo.RetryCount))
            {
                if (!string.IsNullOrEmpty(errorInfo.OperationId))
                {
                    using (LogContext.PushProperty("OperationId", errorInfo.OperationId))
                    {
                        LogWithLevel(errorInfo);
                    }
                }
                else
                {
                    LogWithLevel(errorInfo);
                }
            }
        }
        
        private static void LogWithLevel(ErrorInfo errorInfo)
        {
            var message = "{Message} | Suggested Action: {SuggestedAction} | Properties: {@Properties}";
            var args = new object[] { errorInfo.Message, errorInfo.SuggestedAction ?? "None", errorInfo.Properties };
            
            switch (errorInfo.Severity)
            {
                case ErrorSeverity.Warning:
                    _logger?.LogWarning(errorInfo.Exception, message, args);
                    break;
                case ErrorSeverity.Error:
                    _logger?.LogError(errorInfo.Exception, message, args);
                    break;
                case ErrorSeverity.Critical:
                    _logger?.LogCritical(errorInfo.Exception, message, args);
                    break;
            }
        }

        private static string GetUserFriendlyMessage(Exception ex)
        {
            return ex switch
            {
                FileNotFoundException => "The specified file could not be found.",
                DirectoryNotFoundException => "The specified directory could not be found.",
                UnauthorizedAccessException => "Access to the file or directory is denied. Please check permissions.",
                TimeoutException => "The operation timed out. Please check your connection and try again.",
                InvalidOperationException when ex.Message.Contains("connection") => "Database connection error. Please verify your connection settings.",
                ArgumentException when ex.Message.Contains("table") => "Invalid table specification. Please check table names and try again.",
                OutOfMemoryException => "The system ran out of memory. Try reducing batch size or closing other applications.",
                _ => $"An error occurred: {ex.Message}"
            };
        }

        private static bool DetermineRecoverability(Exception ex)
        {
            return ex switch
            {
                TimeoutException => true,
                InvalidOperationException when ex.Message.Contains("connection") => true,
                OutOfMemoryException => true,
                IOException => true,
                _ => false
            };
        }

        private static string GetSuggestedAction(Exception ex)
        {
            return ex switch
            {
                FileNotFoundException => "Verify the file path and ensure the file exists.",
                DirectoryNotFoundException => "Create the directory or verify the path is correct.",
                UnauthorizedAccessException => "Run as administrator or check file/folder permissions.",
                TimeoutException => "Check your network connection and increase timeout if needed.",
                InvalidOperationException when ex.Message.Contains("connection") => "Verify connection string and database availability.",
                ArgumentException when ex.Message.Contains("table") => "Check table names and ensure they exist in the database.",
                OutOfMemoryException => "Reduce batch size, close other applications, or add more RAM.",
                _ => "Review the error details and contact support if the issue persists."
            };
        }

        private static void LogToFile(ErrorInfo errorInfo)
        {
            if (string.IsNullOrEmpty(_logFilePath))
            {
                return;
            }

            try
            {
                var logEntry = new StringBuilder();
                logEntry.AppendLine($"[{errorInfo.Timestamp:yyyy-MM-dd HH:mm:ss}] [{errorInfo.Severity}] {errorInfo.Context}");
                logEntry.AppendLine($"Message: {errorInfo.Message}");
                
                if (!string.IsNullOrEmpty(errorInfo.SuggestedAction))
                {
                    logEntry.AppendLine($"Suggested Action: {errorInfo.SuggestedAction}");
                }
                
                if (!string.IsNullOrEmpty(errorInfo.Details))
                {
                    logEntry.AppendLine($"Details: {errorInfo.Details}");
                }
                
                logEntry.AppendLine(new string('-', 80));

                File.AppendAllText(_logFilePath, logEntry.ToString());
            }
            catch
            {
                // Ignore logging errors to prevent infinite recursion
            }
        }

        private static void ShowUserMessage(ErrorInfo errorInfo)
        {
            try
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    var icon = errorInfo.Severity switch
                    {
                        ErrorSeverity.Warning => MessageBoxImage.Warning,
                        ErrorSeverity.Error => MessageBoxImage.Error,
                        ErrorSeverity.Critical => MessageBoxImage.Stop,
                        _ => MessageBoxImage.Information
                    };

                    var title = errorInfo.Severity switch
                    {
                        ErrorSeverity.Warning => "Warning",
                        ErrorSeverity.Error => "Error",
                        ErrorSeverity.Critical => "Critical Error",
                        _ => "Information"
                    };

                    var message = errorInfo.Message;
                    if (!string.IsNullOrEmpty(errorInfo.SuggestedAction))
                    {
                        message += $"\n\nSuggested action: {errorInfo.SuggestedAction}";
                    }

                    MessageBox.Show(message, title, MessageBoxButton.OK, icon);
                });
            }
            catch
            {
                // Ignore UI errors to prevent crashes during error handling
            }
        }
    }
}