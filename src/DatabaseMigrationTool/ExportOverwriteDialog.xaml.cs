using DatabaseMigrationTool.Utilities;
using System.Windows;

namespace DatabaseMigrationTool
{
    public partial class ExportOverwriteDialog : Window
    {
        public bool ShouldOverwrite { get; private set; } = false;
        
        public ExportOverwriteDialog(ExportOverwriteResult overwriteResult)
        {
            InitializeComponent();
            
            // Update title and messages based on the type of overwrite
            if (overwriteResult.ConflictingTables.Count > 0)
            {
                this.Title = overwriteResult.ConflictingTables.Count == 1 
                    ? "Table Already Exported" 
                    : "Tables Already Exported";
                    
                TitleTextBlock.Text = overwriteResult.ConflictingTables.Count == 1 
                    ? "Table Already Exported" 
                    : "Tables Already Exported";
                    
                MainMessageTextBlock.Text = overwriteResult.ConflictingTables.Count == 1
                    ? "The selected table has already been exported to this directory. The following files will be overwritten:"
                    : "Some of the selected tables have already been exported to this directory. The following files will be overwritten:";
            }
            else
            {
                // Keep default messages for general export overwrite
                MainMessageTextBlock.Text = "An export already exists in the selected directory. The following files will be overwritten:";
            }
            
            // Populate the file list
            FilesListBox.ItemsSource = overwriteResult.ExistingFiles;
            
            // Set summary text
            SummaryTextBlock.Text = overwriteResult.GetSummaryText();
            
            // Focus on Cancel button by default for safety
            CancelButton.Focus();
        }
        
        private void OverwriteButton_Click(object sender, RoutedEventArgs e)
        {
            ShouldOverwrite = true;
            DialogResult = true;
            Close();
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            ShouldOverwrite = false;
            DialogResult = false;
            Close();
        }
    }
}