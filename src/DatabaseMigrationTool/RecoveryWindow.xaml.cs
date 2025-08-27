using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DatabaseMigrationTool
{
    public partial class RecoveryWindow : Window
    {
        private readonly OperationStateManager _stateManager;
        private List<OperationState> _operations;
        
        public OperationState? SelectedOperation { get; private set; }
        
        public RecoveryWindow()
        {
            InitializeComponent();
            _stateManager = new OperationStateManager();
            _operations = new List<OperationState>();
            
            LoadRecoverableOperations();
        }
        
        private void LoadRecoverableOperations()
        {
            _operations = _stateManager.GetRecoverableOperations();
            OperationsListView.ItemsSource = _operations;
            
            OperationCountText.Text = _operations.Count.ToString();
            
            if (_operations.Count == 0)
            {
                OperationsListView.Visibility = Visibility.Collapsed;
                NoOperationsText.Visibility = Visibility.Visible;
            }
            else
            {
                OperationsListView.Visibility = Visibility.Visible;
                NoOperationsText.Visibility = Visibility.Collapsed;
            }
        }
        
        private void OperationsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedOperation = OperationsListView.SelectedItem as OperationState;
            
            if (selectedOperation != null)
            {
                ShowOperationDetails(selectedOperation);
                ResumeButton.IsEnabled = selectedOperation.CanResume;
                DeleteButton.IsEnabled = true;
            }
            else
            {
                HideOperationDetails();
                ResumeButton.IsEnabled = false;
                DeleteButton.IsEnabled = false;
            }
        }
        
        private void ShowOperationDetails(OperationState operation)
        {
            DetailsGroup.Visibility = Visibility.Visible;
            
            DetailOperationId.Text = operation.OperationId;
            DetailPath.Text = !string.IsNullOrEmpty(operation.OutputPath) ? operation.OutputPath : operation.InputPath;
            DetailCompleted.Text = $"{operation.CompletedTables.Count} tables";
            DetailFailed.Text = $"{operation.FailedTables.Count} tables";
            DetailRemaining.Text = $"{operation.RemainingTables.Count} tables";
            DetailWarnings.Text = $"{operation.Warnings.Count} warnings";
            
            var lastError = operation.Errors.LastOrDefault();
            if (lastError != null)
            {
                DetailLastError.Text = lastError.ErrorMessage;
            }
            else
            {
                DetailLastError.Text = "No errors";
                DetailLastError.Foreground = Brushes.Green;
            }
        }
        
        private void HideOperationDetails()
        {
            DetailsGroup.Visibility = Visibility.Collapsed;
        }
        
        private void Resume_Click(object sender, RoutedEventArgs e)
        {
            var selectedOperation = OperationsListView.SelectedItem as OperationState;
            if (selectedOperation != null && selectedOperation.CanResume)
            {
                var confirmResult = MessageBox.Show(
                    $"Resume the {selectedOperation.OperationType.ToLower()} operation?\n\n" +
                    $"Progress: {selectedOperation.ProgressPercentage:F1}%\n" +
                    $"Remaining: {selectedOperation.RemainingTables.Count} tables\n\n" +
                    $"The operation will continue from where it left off.",
                    "Confirm Resume",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (confirmResult == MessageBoxResult.Yes)
                {
                    SelectedOperation = selectedOperation;
                    DialogResult = true;
                    Close();
                }
            }
        }
        
        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var selectedOperation = OperationsListView.SelectedItem as OperationState;
            if (selectedOperation != null)
            {
                var confirmResult = MessageBox.Show(
                    $"Delete the {selectedOperation.OperationType.ToLower()} operation state?\n\n" +
                    $"This will permanently remove the operation history and you won't be able to resume it.\n\n" +
                    $"Are you sure?",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                
                if (confirmResult == MessageBoxResult.Yes)
                {
                    _stateManager.DeleteOperationState(selectedOperation.OperationId);
                    LoadRecoverableOperations();
                    HideOperationDetails();
                }
            }
        }
        
        private void CleanupOld_Click(object sender, RoutedEventArgs e)
        {
            var confirmResult = MessageBox.Show(
                "Clean up completed and cancelled operations older than 30 days?\n\n" +
                "This will only remove completed or cancelled operations, not failed or paused ones.",
                "Confirm Cleanup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            
            if (confirmResult == MessageBoxResult.Yes)
            {
                _stateManager.CleanupOldStates(TimeSpan.FromDays(30));
                LoadRecoverableOperations();
                MessageBox.Show("Cleanup completed.", "Cleanup Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
    
    public class StatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string status)
            {
                return status switch
                {
                    "Completed" => Brushes.Green,
                    "Failed" => Brushes.Red,
                    "Cancelled" => Brushes.Orange,
                    "Paused" => Brushes.Blue,
                    "InProgress" => Brushes.DarkBlue,
                    _ => Brushes.Black
                };
            }
            return Brushes.Black;
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}