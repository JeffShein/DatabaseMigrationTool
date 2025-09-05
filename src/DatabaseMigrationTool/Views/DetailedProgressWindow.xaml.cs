using DatabaseMigrationTool.Models;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace DatabaseMigrationTool.Views
{
    public partial class DetailedProgressWindow : Window
    {
        private readonly ObservableCollection<string> _completedTables;
        private readonly ObservableCollection<string> _skippedTables;
        private readonly ObservableCollection<string> _warnings;
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isPaused = false;
        private string _logContent = "";
        
        public CancellationToken CancellationToken => _cancellationTokenSource?.Token ?? CancellationToken.None;
        public bool IsCancelled => _cancellationTokenSource?.Token.IsCancellationRequested == true;
        public bool IsPaused => _isPaused;
        
        public DetailedProgressWindow(string operationType = "Export")
        {
            InitializeComponent();
            
            _completedTables = new ObservableCollection<string>();
            _skippedTables = new ObservableCollection<string>();
            _warnings = new ObservableCollection<string>();
            _cancellationTokenSource = new CancellationTokenSource();
            
            // Bind collections to UI
            CompletedTablesList.ItemsSource = _completedTables;
            SkippedTablesList.ItemsSource = _skippedTables;
            WarningsList.ItemsSource = _warnings;
            
            // Set operation type
            OperationTitleText.Text = $"Database {operationType} in Progress";
            OperationSubtitleText.Text = $"{operationType}ing data...";
            
            // Handle window closing
            Closing += (s, e) =>
            {
                if (!IsCancelled && CloseButton.IsEnabled == false)
                {
                    e.Cancel = true;
                    var result = MessageBox.Show(
                        $"The {operationType.ToLower()} operation is still in progress. Do you want to cancel it?",
                        "Operation in Progress",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        CancelOperation();
                    }
                }
            };
        }
        
        public void UpdateProgress(ProgressInfo progress)
        {
            Dispatcher.Invoke(() =>
            {
                // Update overall progress
                OverallProgressBar.IsIndeterminate = progress.IsIndeterminate;
                if (!progress.IsIndeterminate)
                {
                    OverallProgressBar.Value = progress.ProgressPercentage;
                    OverallPercentageText.Text = $"{progress.ProgressPercentage:F1}%";
                }
                else
                {
                    OverallPercentageText.Text = "...";
                }
                
                OverallProgressText.Text = progress.Message;
                StageText.Text = progress.Stage.ToString();
                ElapsedTimeText.Text = progress.FormattedElapsedTime;
                EstimatedTimeText.Text = progress.FormattedEstimatedCompletion;
                
                // Update performance metrics
                if (progress.ElapsedTime.TotalSeconds > 0)
                {
                    PerformanceText.Text = $"{progress.RowsPerSecond:F0} rows/s | {progress.FormattedBytesPerSecond}";
                }
                
                // Update current table information
                if (!string.IsNullOrEmpty(progress.CurrentTable))
                {
                    CurrentTableGroup.Visibility = Visibility.Visible;
                    CurrentTableText.Text = progress.CurrentTable;
                    
                    if (progress.TotalTableRows > 0)
                    {
                        TableProgressBar.IsIndeterminate = false;
                        TableProgressBar.Maximum = progress.TotalTableRows;
                        TableProgressBar.Value = progress.CurrentTableRows;
                        TableProgressText.Text = $"{progress.CurrentTableRows:N0} / {progress.TotalTableRows:N0}";
                    }
                    else
                    {
                        TableProgressBar.IsIndeterminate = true;
                        TableProgressText.Text = "Processing...";
                    }
                    
                    TableSizeText.Text = $"{progress.FormattedProcessedBytes} / {progress.FormattedTotalBytes}";
                }
                else
                {
                    CurrentTableGroup.Visibility = Visibility.Collapsed;
                }
                
                // Update tables lists
                UpdateTablesList(_completedTables, progress.CompletedTables);
                UpdateTablesList(_skippedTables, progress.SkippedTables);
                UpdateTablesList(_warnings, progress.Warnings);
                
                // Update counts
                CompletedCountText.Text = progress.CompletedTables.Count.ToString();
                SkippedCountText.Text = progress.SkippedTables.Count.ToString();
                TotalTablesText.Text = progress.TotalTables.ToString();
                
                // Handle completion
                if (progress.Stage == ProgressStage.Completed || progress.Stage == ProgressStage.Error || progress.Stage == ProgressStage.Cancelled)
                {
                    PauseResumeButton.IsEnabled = false;
                    CancelButton.IsEnabled = false;
                    CloseButton.IsEnabled = true;
                    
                    if (progress.Stage == ProgressStage.Completed)
                    {
                        OperationTitleText.Text = "Operation Completed Successfully!";
                        OperationSubtitleText.Text = "All operations completed without errors.";
                    }
                    else if (progress.Stage == ProgressStage.Error)
                    {
                        OperationTitleText.Text = "Operation Failed";
                        OperationSubtitleText.Text = "The operation encountered errors and could not complete.";
                    }
                    else if (progress.Stage == ProgressStage.Cancelled)
                    {
                        OperationTitleText.Text = "Operation Cancelled";
                        OperationSubtitleText.Text = "The operation was cancelled by the user.";
                    }
                }
            });
        }
        
        private void UpdateTablesList(ObservableCollection<string> collection, List<string> newItems)
        {
            // Add new items that aren't already in the collection
            foreach (var item in newItems)
            {
                if (!collection.Contains(item))
                {
                    collection.Add(item);
                }
            }
        }
        
        public void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                _logContent += $"[{timestamp}] {message}\n";
                LogTextBox.Text = _logContent;
                
                // Auto-scroll to bottom
                LogScrollViewer.ScrollToEnd();
            });
        }
        
        private void PauseResume_Click(object sender, RoutedEventArgs e)
        {
            _isPaused = !_isPaused;
            PauseResumeButton.Content = _isPaused ? "Resume" : "Pause";
            
            AppendLog(_isPaused ? "Operation paused by user" : "Operation resumed by user");
        }
        
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to cancel the operation? This may leave the operation in an incomplete state.",
                "Confirm Cancellation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            
            if (result == MessageBoxResult.Yes)
            {
                CancelOperation();
            }
        }
        
        private void CancelOperation()
        {
            _cancellationTokenSource?.Cancel();
            PauseResumeButton.IsEnabled = false;
            CancelButton.IsEnabled = false;
            AppendLog("Cancellation requested - stopping operation...");
        }
        
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        
        protected override void OnClosed(EventArgs e)
        {
            _cancellationTokenSource?.Dispose();
            base.OnClosed(e);
        }
    }
}