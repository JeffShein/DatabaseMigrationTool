using System.Windows;

namespace DatabaseMigrationTool.Utilities
{
    /// <summary>
    /// Centralized helper for common dialog operations to eliminate duplicate MessageBox patterns
    /// </summary>
    public static class DialogHelper
    {
        /// <summary>
        /// Shows an error message dialog with consistent styling
        /// </summary>
        public static void ShowError(string message, string title = "Error")
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        /// <summary>
        /// Shows an error message dialog from an exception
        /// </summary>
        public static void ShowError(Exception ex, string title = "Error", bool showDetails = false)
        {
            var message = showDetails ? $"{ex.Message}\n\nDetails:\n{ex}" : ex.Message;
            ShowError(message, title);
        }

        /// <summary>
        /// Shows a warning message dialog
        /// </summary>
        public static void ShowWarning(string message, string title = "Warning")
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>
        /// Shows an information message dialog
        /// </summary>
        public static void ShowInfo(string message, string title = "Information")
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Shows a confirmation dialog and returns the user's choice
        /// </summary>
        public static bool ShowConfirmation(string message, string title = "Confirm")
        {
            var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            return result == MessageBoxResult.Yes;
        }

        /// <summary>
        /// Shows a confirmation dialog with Yes/No/Cancel options
        /// </summary>
        public static MessageBoxResult ShowConfirmationWithCancel(string message, string title = "Confirm")
        {
            return MessageBox.Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        }

        /// <summary>
        /// Shows a success message dialog
        /// </summary>
        public static void ShowSuccess(string message, string title = "Success")
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Shows an error dialog for connection failures
        /// </summary>
        public static void ShowConnectionError(string provider, Exception ex)
        {
            ShowError($"Failed to connect to {provider} database.\n\nError: {ex.Message}", "Connection Error");
        }

        /// <summary>
        /// Shows an error dialog for validation failures
        /// </summary>
        public static void ShowValidationError(string field, string message)
        {
            ShowError($"Validation failed for {field}:\n{message}", "Validation Error");
        }

        /// <summary>
        /// Shows an error dialog for file operation failures
        /// </summary>
        public static void ShowFileError(string operation, string fileName, Exception ex)
        {
            ShowError($"Failed to {operation} file '{fileName}'.\n\nError: {ex.Message}", "File Error");
        }
    }
}