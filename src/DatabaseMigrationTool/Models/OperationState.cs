using System;
using System.Collections.Generic;
using DatabaseMigrationTool.Services;

namespace DatabaseMigrationTool.Models
{
    public class OperationState
    {
        public string OperationId { get; set; } = Guid.NewGuid().ToString();
        public string OperationType { get; set; } = string.Empty; // "Export" or "Import"
        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime? EndTime { get; set; }
        public string Status { get; set; } = "InProgress"; // InProgress, Completed, Failed, Cancelled, Paused
        public string ConnectionString { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public string InputPath { get; set; } = string.Empty;
        
        // Progress tracking
        public List<string> CompletedTables { get; set; } = new();
        public List<string> SkippedTables { get; set; } = new();
        public List<string> FailedTables { get; set; } = new();
        public List<string> RemainingTables { get; set; } = new();
        public string? CurrentTable { get; set; }
        public long ProcessedRows { get; set; }
        public long TotalRows { get; set; }
        public long ProcessedBytes { get; set; }
        public long TotalBytes { get; set; }
        
        // Configuration
        public ExportOptions? ExportOptions { get; set; }
        public ImportOptions? ImportOptions { get; set; }
        
        // Error tracking
        public List<OperationError> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        
        // Resume information
        public bool CanResume => Status == "Paused" || (Status == "Failed" && RemainingTables.Count > 0);
        public double ProgressPercentage => TotalRows > 0 ? (double)ProcessedRows / TotalRows * 100 : 0;
        public TimeSpan Duration => (EndTime ?? DateTime.Now) - StartTime;
        
        public void MarkTableCompleted(string tableName)
        {
            if (RemainingTables.Contains(tableName))
            {
                RemainingTables.Remove(tableName);
                CompletedTables.Add(tableName);
            }
        }
        
        public void MarkTableSkipped(string tableName, string reason)
        {
            if (RemainingTables.Contains(tableName))
            {
                RemainingTables.Remove(tableName);
                SkippedTables.Add(tableName);
                Warnings.Add($"Table {tableName} skipped: {reason}");
            }
        }
        
        public void MarkTableFailed(string tableName, string error)
        {
            if (RemainingTables.Contains(tableName))
            {
                RemainingTables.Remove(tableName);
                FailedTables.Add(tableName);
                Errors.Add(new OperationError
                {
                    TableName = tableName,
                    ErrorMessage = error,
                    Timestamp = DateTime.Now,
                    ErrorType = "TableProcessingError"
                });
            }
        }
        
        public void AddError(string errorMessage, string? tableName = null, string errorType = "General")
        {
            Errors.Add(new OperationError
            {
                TableName = tableName,
                ErrorMessage = errorMessage,
                Timestamp = DateTime.Now,
                ErrorType = errorType
            });
        }
        
        public void AddWarning(string warning)
        {
            Warnings.Add($"[{DateTime.Now:HH:mm:ss}] {warning}");
        }
    }
    
    public class OperationError
    {
        public string? TableName { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string ErrorType { get; set; } = string.Empty;
        public string? StackTrace { get; set; }
        public bool IsRecoverable { get; set; } = true;
    }
    
}