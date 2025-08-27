using System;
using System.Collections.Generic;

namespace DatabaseMigrationTool.Models
{
    public class ProgressInfo
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool IsIndeterminate { get; set; }
        public double ProgressPercentage => Total > 0 ? (double)Current / Total * 100 : 0;
        
        // Enhanced progress information
        public string CurrentTable { get; set; } = string.Empty;
        public int CurrentTableIndex { get; set; }
        public int TotalTables { get; set; }
        public long CurrentTableRows { get; set; }
        public long TotalTableRows { get; set; }
        public long ProcessedBytes { get; set; }
        public long TotalBytes { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EstimatedCompletion { get; set; }
        public TimeSpan ElapsedTime => DateTime.Now - StartTime;
        public string CurrentOperation { get; set; } = string.Empty;
        public ProgressStage Stage { get; set; } = ProgressStage.Initializing;
        public List<string> Warnings { get; set; } = new();
        public List<string> CompletedTables { get; set; } = new();
        public List<string> SkippedTables { get; set; } = new();
        
        // Rate calculations
        public double RowsPerSecond => ElapsedTime.TotalSeconds > 0 ? CurrentTableRows / ElapsedTime.TotalSeconds : 0;
        public double BytesPerSecond => ElapsedTime.TotalSeconds > 0 ? ProcessedBytes / ElapsedTime.TotalSeconds : 0;
        
        // Formatted properties for display
        public string FormattedElapsedTime => $"{ElapsedTime:hh\\:mm\\:ss}";
        public string FormattedEstimatedCompletion => EstimatedCompletion?.ToString("HH:mm:ss") ?? "Calculating...";
        public string FormattedBytesPerSecond => FormatBytes((long)BytesPerSecond) + "/s";
        public string FormattedTotalBytes => FormatBytes(TotalBytes);
        public string FormattedProcessedBytes => FormatBytes(ProcessedBytes);
        
        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int suffixIndex = 0;
            
            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }
            
            return $"{size:F1} {suffixes[suffixIndex]}";
        }
        
        public void UpdateEstimatedCompletion()
        {
            if (Total > 0 && Current > 0 && ElapsedTime.TotalSeconds > 0)
            {
                double completionRatio = (double)Current / Total;
                double totalEstimatedSeconds = ElapsedTime.TotalSeconds / completionRatio;
                double remainingSeconds = totalEstimatedSeconds - ElapsedTime.TotalSeconds;
                
                EstimatedCompletion = DateTime.Now.AddSeconds(remainingSeconds);
            }
        }
    }
    
    public enum ProgressStage
    {
        Initializing,
        ConnectingDatabase,
        DiscoveringTables,
        CalculatingDependencies,
        ExportingSchema,
        ExportingData,
        ImportingSchema,
        ImportingData,
        CreatingIndexes,
        CreatingForeignKeys,
        Finalizing,
        Completed,
        Error,
        Cancelled
    }

    public delegate void ProgressReportHandler(ProgressInfo progress);
}