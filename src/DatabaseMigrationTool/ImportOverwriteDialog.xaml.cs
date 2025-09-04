using DatabaseMigrationTool.Utilities;
using System.Linq;
using System.Windows;

namespace DatabaseMigrationTool
{
    public partial class ImportOverwriteDialog : Window
    {
        public bool ShouldProceed { get; private set; } = false;
        
        public ImportOverwriteDialog(ImportOverwriteResult overwriteResult)
        {
            InitializeComponent();
            
            // Set summary text
            SummaryTextBlock.Text = overwriteResult.GetSummaryText();
            
            // Show conflicting tables if any
            if (overwriteResult.ConflictingTables.Any())
            {
                ConflictHeaderTextBlock.Visibility = Visibility.Visible;
                ConflictingTablesListBox.Visibility = Visibility.Visible;
                
                // Create display objects for conflicting tables
                var conflictDisplayItems = overwriteResult.ConflictingTables.Select(t => new
                {
                    TableName = t.TableName,
                    Description = t.GetDescription()
                }).ToList();
                
                ConflictingTablesListBox.ItemsSource = conflictDisplayItems;
            }
            
            // Show new tables if any
            var newTables = overwriteResult.TablesToImport
                .Where(t => !overwriteResult.ConflictingTables.Any(c => 
                    c.TableName.Equals(t, StringComparison.OrdinalIgnoreCase)))
                .ToList();
                
            if (newTables.Any())
            {
                NewTablesHeaderTextBlock.Visibility = Visibility.Visible;
                NewTablesListBox.Visibility = Visibility.Visible;
                NewTablesListBox.ItemsSource = newTables;
            }
            
            // Focus on Cancel button by default for safety
            CancelButton.Focus();
        }
        
        private void ProceedButton_Click(object sender, RoutedEventArgs e)
        {
            ShouldProceed = true;
            DialogResult = true;
            Close();
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            ShouldProceed = false;
            DialogResult = false;
            Close();
        }
    }
}