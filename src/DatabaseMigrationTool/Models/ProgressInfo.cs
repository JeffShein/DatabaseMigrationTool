using System;

namespace DatabaseMigrationTool.Models
{
    public class ProgressInfo
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool IsIndeterminate { get; set; }
        public double ProgressPercentage => Total > 0 ? (double)Current / Total * 100 : 0;
    }

    public delegate void ProgressReportHandler(ProgressInfo progress);
}