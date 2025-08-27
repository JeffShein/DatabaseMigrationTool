using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using DatabaseMigrationTool.Models;

namespace DatabaseMigrationTool.Utilities
{
    public enum ErrorSeverity
    {
        Warning,
        Error,
        Critical
    }

    public class ErrorInfo
    {
        public string Message { get; set; } = string.Empty;
        public string? Details { get; set; }
        public Exception? Exception { get; set; }
        public ErrorSeverity Severity { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string? Context { get; set; }
        public bool IsRecoverable { get; set; } = true;
        public string? SuggestedAction { get; set; }
    }

    public static class ErrorHandler
    {
        private static readonly List<ErrorInfo> _errorHistory = new();
        private static string? _logFilePath;

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
        }

        public static ErrorInfo HandleError(Exception ex, string context, ErrorSeverity severity = ErrorSeverity.Error, bool showUserMessage = true)
        {
            var errorInfo = new ErrorInfo
            {
                Message = GetUserFriendlyMessage(ex),
                Details = ex.ToString(),
                Exception = ex,
                Severity = severity,
                Context = context,
                IsRecoverable = DetermineRecoverability(ex),
                SuggestedAction = GetSuggestedAction(ex)
            };

            return HandleError(errorInfo, showUserMessage);
        }

        public static ErrorInfo HandleError(ErrorInfo errorInfo, bool showUserMessage = true)
        {
            // Add to error history
            _errorHistory.Add(errorInfo);

            // Log to file
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
            if (!errorInfo.IsRecoverable)
            {
                return false;
            }

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    await Task.Delay(1000 * attempt); // Exponential backoff
                    
                    if (await retryAction())
                    {
                        LogToFile(new ErrorInfo
                        {
                            Message = $"Recovery successful after {attempt} attempts",
                            Context = errorInfo.Context,
                            Severity = ErrorSeverity.Warning
                        });
                        return true;
                    }
                }
                catch (Exception retryEx)
                {
                    LogToFile(new ErrorInfo
                    {
                        Message = $"Retry attempt {attempt} failed: {retryEx.Message}",
                        Context = errorInfo.Context,
                        Severity = ErrorSeverity.Warning,
                        Exception = retryEx
                    });
                }
            }

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
            _errorHistory.Clear();
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